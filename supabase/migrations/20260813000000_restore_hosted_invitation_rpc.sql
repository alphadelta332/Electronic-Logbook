-- Restore the invitation RPC on projects whose migration ledger records the hosted
-- foundation even though this function is absent from pg_proc.

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
        jsonb_build_object('device_type', p_device_type, 'platform_label', trim(p_platform_label))
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
