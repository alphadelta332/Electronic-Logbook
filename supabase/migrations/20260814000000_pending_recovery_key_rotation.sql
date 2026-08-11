create or replace function public.elb_bind_device_recovery_key(
    p_actor_account_id uuid,
    p_logbook_id uuid,
    p_device_id uuid,
    p_public_key text,
    p_fingerprint text,
    p_algorithm text
)
returns public.devices
language plpgsql
security definer
set search_path = public
as $$
declare
    existing public.devices%rowtype;
    bound public.devices%rowtype;
    key_rebound boolean := false;
begin
    perform public.elb_assert_recovery_service_access(
        p_actor_account_id,
        p_logbook_id,
        p_device_id
    );

    if length(coalesce(p_public_key, '')) not between 256 and 8192
       or coalesce(p_fingerprint, '') !~ '^[0-9a-f]{64}$'
       or p_algorithm <> 'RSA-OAEP-256' then
        raise exception 'managed recovery public key is invalid';
    end if;

    perform pg_advisory_xact_lock(hashtextextended(p_device_id::text, 1));
    select * into existing
    from public.devices
    where device_id = p_device_id
    for update;

    if existing.recovery_public_key is not null
       and (
           existing.recovery_public_key <> p_public_key
           or existing.recovery_key_fingerprint <> p_fingerprint
           or existing.recovery_key_algorithm <> p_algorithm
       ) then
        if existing.status <> 'pending' then
            raise exception 'managed recovery key replacement requires device rotation';
        end if;
        key_rebound := true;
    end if;

    update public.devices
    set recovery_public_key = p_public_key,
        recovery_key_fingerprint = p_fingerprint,
        recovery_key_algorithm = p_algorithm,
        last_seen_at = now()
    where device_id = p_device_id
    returning * into bound;

    if existing.recovery_public_key is null then
        insert into public.security_events (
            account_id,
            logbook_id,
            device_id,
            event_type,
            actor_account_id,
            source_metadata,
            redacted_details
        )
        values (
            p_actor_account_id,
            p_logbook_id,
            p_device_id,
            'device_recovery_key_bound',
            p_actor_account_id,
            jsonb_build_object('channel', 'managed_recovery_service'),
            jsonb_build_object(
                'algorithm', p_algorithm,
                'fingerprint_suffix', right(p_fingerprint, 8)
            )
        );
    elsif key_rebound then
        insert into public.security_events (
            account_id,
            logbook_id,
            device_id,
            event_type,
            actor_account_id,
            source_metadata,
            redacted_details
        )
        values (
            p_actor_account_id,
            p_logbook_id,
            p_device_id,
            'pending_device_recovery_key_rebound',
            p_actor_account_id,
            jsonb_build_object('channel', 'managed_recovery_service'),
            jsonb_build_object(
                'algorithm', p_algorithm,
                'previous_fingerprint_suffix', right(existing.recovery_key_fingerprint, 8),
                'fingerprint_suffix', right(p_fingerprint, 8)
            )
        );
    end if;

    return bound;
end;
$$;

revoke all on function public.elb_bind_device_recovery_key(uuid, uuid, uuid, text, text, text)
    from public, anon, authenticated;
grant execute on function public.elb_bind_device_recovery_key(uuid, uuid, uuid, text, text, text)
    to service_role;
