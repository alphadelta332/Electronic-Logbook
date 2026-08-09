-- Client-encrypted recovery-code envelopes. The recovery code never leaves the client.

create unique index if not exists idx_key_envelopes_active_recovery_code
    on public.key_envelopes (logbook_id, recovery_method)
    where recovery_method = 'recovery-code-v1'
      and recipient_device_id is null
      and revoked_at is null;

create or replace function public.elb_get_recovery_setup_status(
    p_actor_account_id uuid,
    p_logbook_id uuid,
    p_device_id uuid
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
begin
    perform public.elb_assert_recovery_service_access(
        p_actor_account_id, p_logbook_id, p_device_id
    );
    return jsonb_build_object(
        'managed_envelope_configured', exists (
            select 1 from public.key_envelopes e
            where e.logbook_id = p_logbook_id
              and e.recovery_method = 'managed-service-v1'
              and e.recipient_device_id is null
              and e.revoked_at is null
        ),
        'recovery_code_configured', exists (
            select 1 from public.key_envelopes e
            where e.logbook_id = p_logbook_id
              and e.recovery_method = 'recovery-code-v1'
              and e.recipient_device_id is null
              and e.revoked_at is null
        )
    );
end;
$$;

create or replace function public.elb_upsert_recovery_code_envelope(
    p_actor_account_id uuid,
    p_logbook_id uuid,
    p_device_id uuid,
    p_wrapping_algorithm text,
    p_key_version_id text,
    p_ciphertext text,
    p_nonce text,
    p_salt text
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
        p_actor_account_id, p_logbook_id, p_device_id
    );
    if p_wrapping_algorithm <> 'PBKDF2-SHA256-600000+A256GCM'
       or p_key_version_id <> 'recovery-code-v1'
       or length(coalesce(p_ciphertext, '')) not between 64 and 512
       or length(coalesce(p_nonce, '')) not between 16 and 64
       or length(coalesce(p_salt, '')) not between 20 and 64 then
        raise exception 'recovery-code envelope is invalid';
    end if;

    perform pg_advisory_xact_lock(hashtextextended(p_logbook_id::text, 6));
    update public.key_envelopes
    set revoked_at = now()
    where logbook_id = p_logbook_id
      and recovery_method = 'recovery-code-v1'
      and recipient_device_id is null
      and revoked_at is null;

    insert into public.key_envelopes (
        logbook_id, recovery_method, wrapping_algorithm, key_version_id,
        ciphertext, nonce, created_by_device_id
    ) values (
        p_logbook_id, 'recovery-code-v1', p_wrapping_algorithm, p_key_version_id,
        p_ciphertext, p_salt || '.' || p_nonce, p_device_id
    ) returning * into stored;

    insert into public.security_events (
        account_id, logbook_id, device_id, event_type, actor_account_id,
        source_metadata, redacted_details
    ) values (
        p_actor_account_id, p_logbook_id, p_device_id,
        'recovery_code_envelope_created', p_actor_account_id,
        jsonb_build_object('channel', 'recovery_code'),
        jsonb_build_object('key_version_id', p_key_version_id)
    );
    return stored;
end;
$$;

create or replace function public.elb_read_recovery_code_envelope(
    p_actor_account_id uuid,
    p_logbook_id uuid,
    p_device_id uuid
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    stored public.key_envelopes%rowtype;
begin
    perform public.elb_assert_recovery_service_access(
        p_actor_account_id, p_logbook_id, p_device_id
    );
    if (
        select count(*)
        from public.security_events e
        where e.account_id = p_actor_account_id
          and e.event_type = 'recovery_code_envelope_requested'
          and e.created_at > now() - interval '15 minutes'
    ) >= 5 then
        raise exception 'recovery-code request rate limit exceeded';
    end if;

    insert into public.security_events (
        account_id, logbook_id, device_id, event_type, actor_account_id,
        source_metadata, redacted_details
    ) values (
        p_actor_account_id, p_logbook_id, p_device_id,
        'recovery_code_envelope_requested', p_actor_account_id,
        jsonb_build_object('channel', 'recovery_code'), '{}'::jsonb
    );

    select e.* into stored
    from public.key_envelopes e
    where e.logbook_id = p_logbook_id
      and e.recovery_method = 'recovery-code-v1'
      and e.recipient_device_id is null
      and e.revoked_at is null
      and (e.expires_at is null or e.expires_at > now())
    limit 1;

    if not found then
        raise exception 'recovery-code envelope is unavailable';
    end if;
    return jsonb_build_object(
        'ciphertext', stored.ciphertext,
        'nonce', split_part(stored.nonce, '.', 2),
        'recovery_salt', split_part(stored.nonce, '.', 1),
        'key_version_id', stored.key_version_id,
        'wrapping_algorithm', stored.wrapping_algorithm
    );
end;
$$;

revoke all on function public.elb_upsert_recovery_code_envelope(uuid, uuid, uuid, text, text, text, text, text) from public, anon, authenticated;
revoke all on function public.elb_read_recovery_code_envelope(uuid, uuid, uuid) from public, anon, authenticated;
revoke all on function public.elb_get_recovery_setup_status(uuid, uuid, uuid) from public, anon, authenticated;
grant execute on function public.elb_upsert_recovery_code_envelope(uuid, uuid, uuid, text, text, text, text, text) to service_role;
grant execute on function public.elb_read_recovery_code_envelope(uuid, uuid, uuid) to service_role;
grant execute on function public.elb_get_recovery_setup_status(uuid, uuid, uuid) to service_role;
