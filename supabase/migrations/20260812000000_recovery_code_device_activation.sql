-- Recovery-code replacement devices bind a device recovery key but do not receive a
-- managed-service device envelope. Their audited recovery-code envelope read plus a
-- complete hosted-history acknowledgement is the activation proof.

create or replace function public.elb_activate_recovered_device(
    p_actor_account_id uuid,
    p_logbook_id uuid,
    p_device_id uuid
)
returns public.devices
language plpgsql
security definer
set search_path = public
as $$
declare
    recovered public.devices%rowtype;
    hosted_highest bigint;
    acknowledged bigint;
    superseded_count integer;
    was_pending boolean;
    has_recovery_proof boolean;
begin
    perform public.elb_assert_recovery_service_access(
        p_actor_account_id, p_logbook_id, p_device_id
    );
    perform pg_advisory_xact_lock(hashtextextended(p_actor_account_id::text, 5));
    select * into recovered from public.devices where device_id = p_device_id for update;
    was_pending := recovered.status = 'pending';
    select exists (
        select 1 from public.key_envelopes e
        where e.logbook_id = p_logbook_id
          and e.recipient_device_id = p_device_id
          and e.revoked_at is null
          and (e.expires_at is null or e.expires_at > now())
    ) or exists (
        select 1 from public.security_events e
        where e.account_id = p_actor_account_id
          and e.logbook_id = p_logbook_id
          and e.device_id = p_device_id
          and e.event_type = 'recovery_code_envelope_requested'
    ) into has_recovery_proof;
    if recovered.status not in ('pending', 'active')
       or recovered.recovery_public_key is null
       or not has_recovery_proof then
        raise exception 'replacement device is not ready for activation';
    end if;

    select coalesce(max(o.revision), 0) into hosted_highest
    from public.operations o where o.logbook_id = p_logbook_id;
    select coalesce(max(a.highest_contiguous_revision), -1) into acknowledged
    from public.operation_acks a
    where a.logbook_id = p_logbook_id and a.device_id = p_device_id;
    if acknowledged < hosted_highest then
        raise exception 'replacement device has not acknowledged complete hosted history';
    end if;

    update public.devices
    set status = 'superseded', last_seen_at = now()
    where account_id = p_actor_account_id
      and device_id <> p_device_id
      and status = 'pending';
    get diagnostics superseded_count = row_count;

    update public.key_envelopes e
    set revoked_at = now()
    from public.devices d
    where e.recipient_device_id = d.device_id
      and d.account_id = p_actor_account_id
      and d.status = 'superseded'
      and e.revoked_at is null;

    update public.devices
    set status = 'active', last_seen_at = now(), revoked_at = null
    where device_id = p_device_id
    returning * into recovered;

    if was_pending then
        insert into public.security_events (
            account_id, logbook_id, device_id, event_type, actor_account_id,
            source_metadata, redacted_details
        ) values (
            p_actor_account_id, p_logbook_id, p_device_id,
            'replacement_device_activated', p_actor_account_id,
            jsonb_build_object('channel', 'managed_recovery_service'),
            jsonb_build_object(
                'acknowledged_revision', acknowledged,
                'superseded_pending_attempts', superseded_count
            )
        );
    end if;
    return recovered;
end;
$$;

revoke all on function public.elb_activate_recovered_device(uuid, uuid, uuid) from public, anon, authenticated;
grant execute on function public.elb_activate_recovered_device(uuid, uuid, uuid) to service_role;
