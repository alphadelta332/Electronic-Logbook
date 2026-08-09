-- Audited, idempotent replacement-device recovery.

-- Pending recovery devices may read their own short-lived envelope and acknowledge
-- pulled history. append_hosted_operation retains its separate active-only guard.
create or replace function public.elb_device_belongs_to_current_account(p_device_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
    select exists (
        select 1
        from public.devices d
        join public.accounts a on a.account_id = d.account_id
        where d.device_id = p_device_id
          and d.account_id = auth.uid()
          and d.status in ('active', 'pending')
          and a.status = 'active'
    )
$$;

create or replace function public.elb_register_pending_recovery_device(
    p_actor_account_id uuid,
    p_logbook_id uuid,
    p_device_id uuid,
    p_device_type public.elb_device_type,
    p_platform_label text
)
returns public.devices
language plpgsql
security definer
set search_path = public
as $$
declare
    existing public.devices%rowtype;
    stored public.devices%rowtype;
begin
    if auth.role() <> 'service_role' then
        raise exception 'managed recovery service access required';
    end if;

    if p_actor_account_id is null or p_logbook_id is null or p_device_id is null
       or length(trim(coalesce(p_platform_label, ''))) = 0 then
        raise exception 'managed recovery context is incomplete';
    end if;

    if not exists (
        select 1 from public.accounts a
        where a.account_id = p_actor_account_id and a.status = 'active'
    ) or not exists (
        select 1 from public.logbook_memberships m
        where m.logbook_id = p_logbook_id
          and m.account_id = p_actor_account_id
          and m.role = 'owner'
          and m.accepted_at is not null
          and m.revoked_at is null
    ) then
        raise exception 'managed recovery logbook access denied';
    end if;

    perform pg_advisory_xact_lock(hashtextextended(p_device_id::text, 4));
    select * into existing from public.devices where device_id = p_device_id for update;
    if found then
        if existing.account_id <> p_actor_account_id
           or existing.device_type <> p_device_type
           or existing.status not in ('pending', 'active') then
            raise exception 'managed recovery device registration conflicts with existing state';
        end if;
        return existing;
    end if;

    insert into public.devices (
        device_id, account_id, device_type, platform_label, last_seen_at, status
    ) values (
        p_device_id, p_actor_account_id, p_device_type, trim(p_platform_label), now(), 'pending'
    ) returning * into stored;

    insert into public.security_events (
        account_id, logbook_id, device_id, event_type, actor_account_id,
        source_metadata, redacted_details
    ) values (
        p_actor_account_id, p_logbook_id, p_device_id,
        'replacement_device_registered_pending', p_actor_account_id,
        jsonb_build_object('channel', 'managed_recovery_service'),
        jsonb_build_object('device_type', p_device_type, 'platform_label', trim(p_platform_label))
    );

    return stored;
end;
$$;

create or replace function public.elb_assert_recovery_service_access(
    p_actor_account_id uuid,
    p_logbook_id uuid,
    p_device_id uuid
)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
    if auth.role() <> 'service_role' then
        raise exception 'managed recovery service access required';
    end if;
    if p_actor_account_id is null or p_logbook_id is null or p_device_id is null then
        raise exception 'managed recovery context is incomplete';
    end if;
    if not exists (
        select 1 from public.accounts a
        where a.account_id = p_actor_account_id and a.status = 'active'
    ) or not exists (
        select 1 from public.logbook_memberships m
        where m.logbook_id = p_logbook_id
          and m.account_id = p_actor_account_id
          and m.role = 'owner'
          and m.accepted_at is not null
          and m.revoked_at is null
    ) then
        raise exception 'managed recovery logbook access denied';
    end if;
    if not exists (
        select 1 from public.devices d
        where d.device_id = p_device_id
          and d.account_id = p_actor_account_id
          and d.status in ('active', 'pending')
    ) then
        raise exception 'managed recovery device access denied';
    end if;
end;
$$;

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
begin
    perform public.elb_assert_recovery_service_access(
        p_actor_account_id, p_logbook_id, p_device_id
    );
    perform pg_advisory_xact_lock(hashtextextended(p_actor_account_id::text, 5));
    select * into recovered from public.devices where device_id = p_device_id for update;
    was_pending := recovered.status = 'pending';
    if recovered.status not in ('pending', 'active')
       or recovered.recovery_public_key is null
       or not exists (
           select 1 from public.key_envelopes e
           where e.logbook_id = p_logbook_id
             and e.recipient_device_id = p_device_id
             and e.revoked_at is null
             and (e.expires_at is null or e.expires_at > now())
       ) then
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

revoke all on function public.elb_register_pending_recovery_device(uuid, uuid, uuid, public.elb_device_type, text) from public, anon, authenticated;
revoke all on function public.elb_activate_recovered_device(uuid, uuid, uuid) from public, anon, authenticated;
grant execute on function public.elb_register_pending_recovery_device(uuid, uuid, uuid, public.elb_device_type, text) to service_role;
grant execute on function public.elb_activate_recovered_device(uuid, uuid, uuid) to service_role;
