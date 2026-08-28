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

notify pgrst, 'reload schema';
