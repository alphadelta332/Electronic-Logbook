alter table public.devices
    add column if not exists recovery_public_key text,
    add column if not exists recovery_key_fingerprint text,
    add column if not exists recovery_key_algorithm text;

alter table public.devices
    drop constraint if exists devices_recovery_key_complete;
alter table public.devices
    add constraint devices_recovery_key_complete check (
        (
            recovery_public_key is null
            and recovery_key_fingerprint is null
            and recovery_key_algorithm is null
        )
        or (
            length(recovery_public_key) between 256 and 8192
            and recovery_key_fingerprint ~ '^[0-9a-f]{64}$'
            and recovery_key_algorithm = 'RSA-OAEP-256'
        )
    );

create unique index if not exists idx_devices_account_recovery_fingerprint
    on public.devices (account_id, recovery_key_fingerprint)
    where recovery_key_fingerprint is not null;

create unique index if not exists idx_key_envelopes_active_managed_recovery
    on public.key_envelopes (logbook_id, recovery_method)
    where recovery_method = 'managed-service-v1'
      and recipient_device_id is null
      and revoked_at is null;

create unique index if not exists idx_key_envelopes_active_device_recovery
    on public.key_envelopes (logbook_id, recipient_device_id)
    where recipient_device_id is not null
      and revoked_at is null;

create or replace function public.elb_reject_authenticated_device_status_change()
returns trigger
language plpgsql
as $$
begin
    if current_user = 'authenticated' and new.status is distinct from old.status then
        raise exception 'device status changes require administrative access';
    end if;

    if current_user = 'authenticated'
       and (
           new.recovery_public_key is distinct from old.recovery_public_key
           or new.recovery_key_fingerprint is distinct from old.recovery_key_fingerprint
           or new.recovery_key_algorithm is distinct from old.recovery_key_algorithm
       ) then
        raise exception 'device recovery-key changes require managed service access';
    end if;

    return new;
end;
$$;

drop policy if exists devices_insert_self on public.devices;
create policy devices_insert_self on public.devices
    for insert
    with check (
        account_id = auth.uid()
        and public.elb_is_active_account(auth.uid())
        and recovery_public_key is null
        and recovery_key_fingerprint is null
        and recovery_key_algorithm is null
    );

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
        select 1
        from public.accounts a
        where a.account_id = p_actor_account_id
          and a.status = 'active'
    ) then
        raise exception 'managed recovery account access denied';
    end if;

    if not exists (
        select 1
        from public.logbook_memberships m
        where m.logbook_id = p_logbook_id
          and m.account_id = p_actor_account_id
          and m.role = 'owner'
          and m.accepted_at is not null
          and m.revoked_at is null
    ) then
        raise exception 'managed recovery logbook access denied';
    end if;

    if not exists (
        select 1
        from public.devices d
        where d.device_id = p_device_id
          and d.account_id = p_actor_account_id
          and d.status = 'active'
    ) then
        raise exception 'managed recovery device access denied';
    end if;
end;
$$;

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
        raise exception 'managed recovery key replacement requires device rotation';
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
    end if;

    return bound;
end;
$$;

create or replace function public.elb_upsert_managed_recovery_envelope(
    p_actor_account_id uuid,
    p_logbook_id uuid,
    p_device_id uuid,
    p_wrapping_algorithm text,
    p_key_version_id text,
    p_ciphertext text,
    p_nonce text
)
returns public.key_envelopes
language plpgsql
security definer
set search_path = public
as $$
declare
    existing public.key_envelopes%rowtype;
    stored public.key_envelopes%rowtype;
begin
    perform public.elb_assert_recovery_service_access(
        p_actor_account_id,
        p_logbook_id,
        p_device_id
    );

    if p_wrapping_algorithm <> 'AES-256-GCM'
       or length(trim(coalesce(p_key_version_id, ''))) not between 1 and 128
       or length(coalesce(p_ciphertext, '')) not between 48 and 512
       or length(coalesce(p_nonce, '')) not between 16 and 64 then
        raise exception 'managed recovery envelope is invalid';
    end if;

    perform pg_advisory_xact_lock(hashtextextended(p_logbook_id::text, 2));
    select * into existing
    from public.key_envelopes
    where logbook_id = p_logbook_id
      and recovery_method = 'managed-service-v1'
      and recipient_device_id is null
      and revoked_at is null
    for update;

    if found then
        return existing;
    end if;

    insert into public.key_envelopes (
        logbook_id,
        recovery_method,
        wrapping_algorithm,
        key_version_id,
        ciphertext,
        nonce,
        created_by_device_id
    )
    values (
        p_logbook_id,
        'managed-service-v1',
        p_wrapping_algorithm,
        p_key_version_id,
        p_ciphertext,
        p_nonce,
        p_device_id
    )
    returning * into stored;

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
        'managed_recovery_envelope_created',
        p_actor_account_id,
        jsonb_build_object('channel', 'managed_recovery_service'),
        jsonb_build_object(
            'key_version_id', p_key_version_id,
            'wrapping_algorithm', p_wrapping_algorithm
        )
    );

    return stored;
end;
$$;

create or replace function public.elb_read_managed_recovery_envelope(
    p_actor_account_id uuid,
    p_logbook_id uuid,
    p_device_id uuid
)
returns public.key_envelopes
language plpgsql
security definer
set search_path = public
as $$
declare
    stored public.key_envelopes%rowtype;
begin
    perform public.elb_assert_recovery_service_access(
        p_actor_account_id,
        p_logbook_id,
        p_device_id
    );

    select * into stored
    from public.key_envelopes
    where logbook_id = p_logbook_id
      and recovery_method = 'managed-service-v1'
      and recipient_device_id is null
      and revoked_at is null
      and (expires_at is null or expires_at > now());

    if not found then
        raise exception 'managed recovery envelope is unavailable';
    end if;

    return stored;
end;
$$;

create or replace function public.elb_upsert_device_recovery_envelope(
    p_actor_account_id uuid,
    p_logbook_id uuid,
    p_device_id uuid,
    p_key_version_id text,
    p_ciphertext text
)
returns public.key_envelopes
language plpgsql
security definer
set search_path = public
as $$
declare
    existing public.key_envelopes%rowtype;
    stored public.key_envelopes%rowtype;
begin
    perform public.elb_assert_recovery_service_access(
        p_actor_account_id,
        p_logbook_id,
        p_device_id
    );

    if length(trim(coalesce(p_key_version_id, ''))) not between 1 and 128
       or length(coalesce(p_ciphertext, '')) not between 256 and 8192
       or not exists (
           select 1
           from public.devices d
           where d.device_id = p_device_id
             and d.recovery_public_key is not null
             and d.recovery_key_fingerprint is not null
             and d.recovery_key_algorithm = 'RSA-OAEP-256'
       ) then
        raise exception 'device recovery envelope is invalid';
    end if;

    perform pg_advisory_xact_lock(hashtextextended(p_device_id::text, 3));
    select * into existing
    from public.key_envelopes
    where logbook_id = p_logbook_id
      and recipient_device_id = p_device_id
      and revoked_at is null
    for update;

    if found then
        update public.key_envelopes
        set wrapping_algorithm = 'RSA-OAEP-256',
            key_version_id = p_key_version_id,
            ciphertext = p_ciphertext,
            nonce = 'rsa-oaep-randomized',
            created_at = now(),
            created_by_device_id = p_device_id,
            expires_at = now() + interval '15 minutes'
        where key_envelope_id = existing.key_envelope_id
        returning * into stored;
    else
        insert into public.key_envelopes (
            logbook_id,
            recipient_device_id,
            wrapping_algorithm,
            key_version_id,
            ciphertext,
            nonce,
            created_by_device_id,
            expires_at
        )
        values (
            p_logbook_id,
            p_device_id,
            'RSA-OAEP-256',
            p_key_version_id,
            p_ciphertext,
            'rsa-oaep-randomized',
            p_device_id,
            now() + interval '15 minutes'
        )
        returning * into stored;
    end if;

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
        'device_recovery_envelope_issued',
        p_actor_account_id,
        jsonb_build_object('channel', 'managed_recovery_service'),
        jsonb_build_object(
            'key_version_id', p_key_version_id,
            'wrapping_algorithm', 'RSA-OAEP-256'
        )
    );

    return stored;
end;
$$;

revoke all on function public.elb_assert_recovery_service_access(uuid, uuid, uuid) from public, anon, authenticated;
revoke all on function public.elb_bind_device_recovery_key(uuid, uuid, uuid, text, text, text) from public, anon, authenticated;
revoke all on function public.elb_upsert_managed_recovery_envelope(uuid, uuid, uuid, text, text, text, text) from public, anon, authenticated;
revoke all on function public.elb_read_managed_recovery_envelope(uuid, uuid, uuid) from public, anon, authenticated;
revoke all on function public.elb_upsert_device_recovery_envelope(uuid, uuid, uuid, text, text) from public, anon, authenticated;

grant execute on function public.elb_assert_recovery_service_access(uuid, uuid, uuid) to service_role;
grant execute on function public.elb_bind_device_recovery_key(uuid, uuid, uuid, text, text, text) to service_role;
grant execute on function public.elb_upsert_managed_recovery_envelope(uuid, uuid, uuid, text, text, text, text) to service_role;
grant execute on function public.elb_read_managed_recovery_envelope(uuid, uuid, uuid) to service_role;
grant execute on function public.elb_upsert_device_recovery_envelope(uuid, uuid, uuid, text, text) to service_role;
