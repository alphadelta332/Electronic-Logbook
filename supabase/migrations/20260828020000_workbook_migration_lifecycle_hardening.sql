-- Reapply the two hardened RPC definitions for databases that received the
-- earlier lifecycle draft before these fail-closed checks were finalized.

create or replace function public.begin_workbook_migration(
    p_source_fingerprint text,
    p_logbook_display_name text,
    p_platform_label text,
    p_public_signing_key text default null,
    p_signing_key_fingerprint text default null
)
returns public.workbook_migrations
language plpgsql
security definer
set search_path = public
as $$
declare
    invited_account public.accounts%rowtype;
    authenticated_email public.citext;
    existing public.workbook_migrations%rowtype;
    created public.workbook_migrations%rowtype;
    created_device public.devices%rowtype;
    created_logbook public.logbooks%rowtype;
begin
    if auth.uid() is null then
        raise exception 'authentication required';
    end if;
    if coalesce(p_source_fingerprint, '') !~ '^[0-9a-f]{64}$' then
        raise exception 'workbook source fingerprint is invalid';
    end if;
    if length(trim(coalesce(p_logbook_display_name, ''))) = 0 then
        raise exception 'logbook display name is required';
    end if;
    if length(trim(coalesce(p_platform_label, ''))) = 0 then
        raise exception 'platform label is required';
    end if;

    perform pg_advisory_xact_lock(hashtextextended(auth.uid()::text, 7));
    select * into invited_account
    from public.accounts
    where account_id = auth.uid()
    for update;

    if not found then
        raise exception 'invitation required';
    end if;
    if invited_account.status = 'disabled' then
        raise exception 'account disabled';
    end if;
    if invited_account.status not in ('invited', 'active') then
        raise exception 'account cannot begin workbook migration';
    end if;
    if invited_account.onboarding_mode <> 'workbook_migration' then
        raise exception 'workbook migration invitation required';
    end if;

    select nullif(trim(email), '')::public.citext
    into authenticated_email
    from auth.users
    where id = auth.uid();
    if authenticated_email is null or authenticated_email <> invited_account.invited_email then
        raise exception 'signed-in account does not match invited email';
    end if;

    select * into existing
    from public.workbook_migrations
    where account_id = auth.uid()
    for update;

    if found then
        if existing.source_fingerprint <> p_source_fingerprint then
            raise exception 'a different workbook migration already exists for this account';
        end if;
        if existing.status = 'completed' then
            return existing;
        end if;
        if not exists (
            select 1
            from public.logbook_memberships m
            join public.devices d on d.device_id = existing.device_id
            where m.logbook_id = existing.logbook_id
              and m.account_id = auth.uid()
              and m.role = 'owner'
              and m.accepted_at is not null
              and m.revoked_at is null
              and d.account_id = auth.uid()
              and d.device_type = 'workbook'
              and d.status = 'active'
        ) then
            raise exception 'existing workbook migration resources are unavailable';
        end if;
        if existing.status = 'failed' then
            update public.workbook_migrations
            set status = 'pending',
                attempt_count = attempt_count + 1,
                failure_code = null,
                failed_at = null,
                updated_at = now()
            where migration_id = existing.migration_id
            returning * into existing;

            insert into public.security_events (
                account_id, logbook_id, device_id, event_type, actor_account_id,
                redacted_details
            ) values (
                auth.uid(), existing.logbook_id, existing.device_id,
                'workbook_migration_resumed', auth.uid(),
                jsonb_build_object('attempt_count', existing.attempt_count)
            );
        end if;
        return existing;
    end if;

    if exists (
        select 1 from public.logbook_memberships
        where account_id = auth.uid() and revoked_at is null
    ) then
        raise exception 'account already has a hosted logbook';
    end if;

    update public.accounts
    set status = 'active'
    where account_id = auth.uid() and status = 'invited';

    insert into public.devices (
        account_id, device_type, platform_label, public_signing_key,
        signing_key_fingerprint, last_seen_at, status
    ) values (
        auth.uid(), 'workbook', trim(p_platform_label),
        nullif(trim(p_public_signing_key), ''),
        nullif(trim(p_signing_key_fingerprint), ''), now(), 'active'
    ) returning * into created_device;

    insert into public.logbooks (owner_account_id, display_name)
    values (auth.uid(), trim(p_logbook_display_name))
    returning * into created_logbook;

    insert into public.logbook_memberships (
        logbook_id, account_id, role, granted_by_account_id, accepted_at
    ) values (
        created_logbook.logbook_id, auth.uid(), 'owner', auth.uid(), now()
    );

    insert into public.workbook_migrations (
        account_id, logbook_id, device_id, source_fingerprint
    ) values (
        auth.uid(), created_logbook.logbook_id, created_device.device_id,
        p_source_fingerprint
    ) returning * into created;

    insert into public.security_events (
        account_id, logbook_id, device_id, event_type, actor_account_id,
        redacted_details
    ) values (
        auth.uid(), created.logbook_id, created.device_id,
        'workbook_migration_started', auth.uid(),
        jsonb_build_object('attempt_count', created.attempt_count)
    );

    return created;
end;
$$;


create or replace function public.accept_hosted_invitation(
    p_display_name text,
    p_device_type public.elb_device_type,
    p_platform_label text,
    p_public_signing_key text default null,
    p_signing_key_fingerprint text default null
)
returns public.devices
language plpgsql
security definer
set search_path = public
as $$
declare
    account public.accounts%rowtype;
    authenticated_email public.citext;
    saved_device public.devices%rowtype;
begin
    if auth.uid() is null then
        raise exception 'authentication required';
    end if;

    select *
    into account
    from public.accounts
    where account_id = auth.uid();

    if not found then
        raise exception 'invitation required';
    end if;
    if account.status = 'disabled' then
        raise exception 'account disabled';
    end if;
    if account.status not in ('invited', 'active') then
        raise exception 'account cannot accept invitation';
    end if;

    select nullif(trim(email), '')::public.citext
    into authenticated_email
    from auth.users
    where id = auth.uid();

    if authenticated_email is null or authenticated_email <> account.invited_email then
        raise exception 'signed-in account does not match invited email';
    end if;

    if account.onboarding_mode = 'workbook_migration'
       and p_device_type = 'workbook' then
        raise exception 'begin workbook migration through the migration service';
    end if;
    if account.onboarding_mode = 'workbook_migration' and p_device_type = 'android' then
        raise exception 'workbook migration required before Android sign-in';
    end if;
    if length(trim(p_platform_label)) = 0 then
        raise exception 'platform label is required';
    end if;

    update public.accounts
    set status = 'active',
        display_name = coalesce(nullif(trim(p_display_name), ''), display_name)
    where account_id = auth.uid()
      and status = 'invited';

    insert into public.devices (
        account_id,
        device_type,
        platform_label,
        public_signing_key,
        signing_key_fingerprint,
        last_seen_at,
        status
    )
    values (
        auth.uid(),
        p_device_type,
        trim(p_platform_label),
        nullif(trim(p_public_signing_key), ''),
        nullif(trim(p_signing_key_fingerprint), ''),
        now(),
        'active'
    )
    returning * into saved_device;

    insert into public.security_events (
        account_id,
        device_id,
        event_type,
        severity,
        actor_account_id,
        redacted_details
    )
    values (
        auth.uid(),
        saved_device.device_id,
        case when account.status = 'invited' then 'invitation_accepted' else 'device_registered' end,
        'info',
        auth.uid(),
        jsonb_build_object(
            'device_type', p_device_type,
            'platform_label', trim(p_platform_label),
            'onboarding_mode', account.onboarding_mode
        )
    );

    return saved_device;
end;
$$;

revoke all on function public.accept_hosted_invitation(
    text, public.elb_device_type, text, text, text
) from public, anon, authenticated;
grant execute on function public.accept_hosted_invitation(
    text, public.elb_device_type, text, text, text
) to authenticated;

revoke all on function public.begin_workbook_migration(text, text, text, text, text)
    from public, anon, authenticated;
grant execute on function public.begin_workbook_migration(text, text, text, text, text)
    to authenticated;

notify pgrst, 'reload schema';
