-- Add the resumable workbook-migration lifecycle and keep workbook-led pilot
-- invitations from initializing an empty Android logbook.

do $$
begin
    create type public.elb_onboarding_mode as enum ('app_only', 'workbook_migration');
exception
    when duplicate_object then null;
end $$;

alter table public.accounts
    add column if not exists onboarding_mode public.elb_onboarding_mode not null default 'app_only';

do $$
begin
    create type public.elb_workbook_migration_status as enum ('pending', 'completed', 'failed');
exception
    when duplicate_object then null;
end $$;

create table if not exists public.workbook_migrations (
    migration_id uuid primary key default gen_random_uuid(),
    account_id uuid not null unique references public.accounts (account_id) on delete cascade,
    logbook_id uuid not null unique references public.logbooks (logbook_id) on delete cascade,
    device_id uuid not null unique references public.devices (device_id) on delete restrict,
    source_fingerprint text not null,
    status public.elb_workbook_migration_status not null default 'pending',
    attempt_count integer not null default 1,
    expected_operation_count integer,
    verified_operation_count integer,
    verification_receipt_hash text,
    failure_code text,
    started_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    completed_at timestamptz,
    failed_at timestamptz,
    constraint workbook_migrations_source_fingerprint_sha256 check (
        source_fingerprint ~ '^[0-9a-f]{64}$'
    ),
    constraint workbook_migrations_attempt_count_positive check (attempt_count > 0),
    constraint workbook_migrations_operation_counts_nonnegative check (
        (expected_operation_count is null or expected_operation_count >= 0)
        and (verified_operation_count is null or verified_operation_count >= 0)
    ),
    constraint workbook_migrations_state_fields_match check (
        (
            status = 'pending'
            and completed_at is null
            and failed_at is null
            and failure_code is null
            and expected_operation_count is null
            and verified_operation_count is null
            and verification_receipt_hash is null
        )
        or (
            status = 'completed'
            and completed_at is not null
            and failed_at is null
            and failure_code is null
            and expected_operation_count is not null
            and verified_operation_count = expected_operation_count
            and verification_receipt_hash ~ '^[0-9a-f]{64}$'
        )
        or (
            status = 'failed'
            and completed_at is null
            and failed_at is not null
            and failure_code ~ '^[A-Z0-9_]{3,64}$'
            and expected_operation_count is null
            and verified_operation_count is null
            and verification_receipt_hash is null
        )
    )
);

alter table public.workbook_migrations enable row level security;
revoke all on public.workbook_migrations from public, anon, authenticated;

create or replace function public.elb_reject_authenticated_invitation_change()
returns trigger
language plpgsql
set search_path = public
as $$
begin
    if auth.uid() is not null
       and (
           new.invited_email is distinct from old.invited_email
           or new.onboarding_mode is distinct from old.onboarding_mode
       ) then
        raise exception 'invitation identity and onboarding mode are owner-managed';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_accounts_authenticated_invitation_guard on public.accounts;
create trigger trg_accounts_authenticated_invitation_guard
    before update on public.accounts
    for each row execute function public.elb_reject_authenticated_invitation_change();

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

create or replace function public.get_workbook_migration_status()
returns setof public.workbook_migrations
language sql
stable
security definer
set search_path = public
as $$
    select m.*
    from public.workbook_migrations m
    where m.account_id = auth.uid()
$$;

create or replace function public.fail_workbook_migration(
    p_migration_id uuid,
    p_failure_code text
)
returns public.workbook_migrations
language plpgsql
security definer
set search_path = public
as $$
declare
    existing public.workbook_migrations%rowtype;
begin
    if coalesce(p_failure_code, '') !~ '^[A-Z0-9_]{3,64}$' then
        raise exception 'workbook migration failure code is invalid';
    end if;

    select * into existing
    from public.workbook_migrations
    where migration_id = p_migration_id and account_id = auth.uid()
    for update;
    if not found then
        raise exception 'workbook migration not found';
    end if;
    if existing.status = 'completed' then
        raise exception 'completed workbook migration cannot be failed';
    end if;
    if existing.status = 'failed' then
        if existing.failure_code <> p_failure_code then
            raise exception 'workbook migration already failed for a different reason';
        end if;
        return existing;
    end if;

    update public.workbook_migrations
    set status = 'failed', failure_code = p_failure_code,
        failed_at = now(), updated_at = now()
    where migration_id = p_migration_id
    returning * into existing;

    insert into public.security_events (
        account_id, logbook_id, device_id, event_type, severity,
        actor_account_id, redacted_details
    ) values (
        auth.uid(), existing.logbook_id, existing.device_id,
        'workbook_migration_failed', 'warning', auth.uid(),
        jsonb_build_object('failure_code', p_failure_code)
    );
    return existing;
end;
$$;

create or replace function public.complete_workbook_migration(
    p_migration_id uuid,
    p_expected_operation_count integer,
    p_verification_receipt_hash text
)
returns public.workbook_migrations
language plpgsql
security definer
set search_path = public
as $$
declare
    existing public.workbook_migrations%rowtype;
    hosted_operation_count integer;
begin
    if p_expected_operation_count is null or p_expected_operation_count < 0 then
        raise exception 'expected operation count is invalid';
    end if;
    if coalesce(p_verification_receipt_hash, '') !~ '^[0-9a-f]{64}$' then
        raise exception 'workbook migration verification receipt is invalid';
    end if;

    select * into existing
    from public.workbook_migrations
    where migration_id = p_migration_id and account_id = auth.uid()
    for update;
    if not found then
        raise exception 'workbook migration not found';
    end if;
    if existing.status = 'completed' then
        if existing.expected_operation_count <> p_expected_operation_count
           or existing.verification_receipt_hash <> p_verification_receipt_hash then
            raise exception 'completed workbook migration verification does not match';
        end if;
        return existing;
    end if;
    if existing.status <> 'pending' then
        raise exception 'failed workbook migration must be resumed before completion';
    end if;

    select count(*)::integer into hosted_operation_count
    from public.operations
    where logbook_id = existing.logbook_id;
    if hosted_operation_count <> p_expected_operation_count then
        raise exception 'hosted operation count does not match verified migration';
    end if;

    update public.workbook_migrations
    set status = 'completed',
        expected_operation_count = p_expected_operation_count,
        verified_operation_count = hosted_operation_count,
        verification_receipt_hash = p_verification_receipt_hash,
        completed_at = now(), updated_at = now()
    where migration_id = p_migration_id
    returning * into existing;

    insert into public.security_events (
        account_id, logbook_id, device_id, event_type, actor_account_id,
        redacted_details
    ) values (
        auth.uid(), existing.logbook_id, existing.device_id,
        'workbook_migration_completed', auth.uid(),
        jsonb_build_object('verified_operation_count', hosted_operation_count)
    );
    return existing;
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
revoke all on function public.get_workbook_migration_status()
    from public, anon, authenticated;
revoke all on function public.fail_workbook_migration(uuid, text)
    from public, anon, authenticated;
revoke all on function public.complete_workbook_migration(uuid, integer, text)
    from public, anon, authenticated;
grant execute on function public.begin_workbook_migration(text, text, text, text, text)
    to authenticated;
grant execute on function public.get_workbook_migration_status()
    to authenticated;
grant execute on function public.fail_workbook_migration(uuid, text)
    to authenticated;
grant execute on function public.complete_workbook_migration(uuid, integer, text)
    to authenticated;

notify pgrst, 'reload schema';
