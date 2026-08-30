-- Restore foundation reporting routines on projects whose migration ledger records
-- the hosted foundation even though these routines are absent from pg_proc.

begin;

create or replace function public.get_hosted_pilot_health()
returns table (
    active_account_count bigint,
    active_device_count bigint,
    stored_operation_count bigint,
    estimated_database_bytes bigint,
    database_size_status text,
    paid_plan_upgrade_triggers jsonb
)
language sql
stable
security definer
set search_path = public
as $$
    with measured as (
        select
            (select count(*) from public.accounts where status = 'active') as active_account_count,
            (select count(*) from public.devices where status = 'active') as active_device_count,
            (select count(*) from public.operations) as stored_operation_count,
            (
                pg_total_relation_size('public.accounts'::regclass)
                + pg_total_relation_size('public.logbooks'::regclass)
                + pg_total_relation_size('public.logbook_memberships'::regclass)
                + pg_total_relation_size('public.devices'::regclass)
                + pg_total_relation_size('public.operations'::regclass)
                + pg_total_relation_size('public.operation_acks'::regclass)
                + pg_total_relation_size('public.security_events'::regclass)
            ) as estimated_database_bytes
    )
    select
        active_account_count,
        active_device_count,
        stored_operation_count,
        estimated_database_bytes,
        case
            when estimated_database_bytes >= 450000000 then 'upgrade_required'
            when estimated_database_bytes >= 400000000 then 'near_limit'
            else 'ok'
        end as database_size_status,
        to_jsonb(array_remove(array[
            case when estimated_database_bytes >= 450000000 then 'database storage reached private-pilot upgrade trigger' end,
            case when active_account_count >= 45 then 'active pilot accounts nearing configured free-tier ceiling' end,
            case when stored_operation_count >= 50000 then 'operation count requires restore rehearsal and paid-plan review' end
        ], null)) as paid_plan_upgrade_triggers
    from measured;
$$;

create or replace function public.create_redacted_hosted_diagnostics(
    p_logbook_id uuid default null
)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
    with health as (
        select *
        from public.get_hosted_pilot_health()
    ),
    events as (
        select coalesce(jsonb_agg(jsonb_build_object(
            'created_at', e.created_at,
            'event_type', e.event_type,
            'severity', e.severity,
            'details', e.redacted_details
        ) order by e.created_at desc), '[]'::jsonb) as rows
        from public.security_events e
        where (p_logbook_id is null or e.logbook_id = p_logbook_id)
          and (
              e.account_id = auth.uid()
              or e.logbook_id is null
              or public.elb_has_logbook_role(e.logbook_id, 'owner')
          )
        limit 100
    )
    select jsonb_build_object(
        'created_at', now(),
        'supabase_url', '[redacted]',
        'anon_key', '[redacted]',
        'account_id', case when auth.uid() is null then '[anonymous]' else '[redacted]' end,
        'logbook_id', case when p_logbook_id is null then '[none]' else '[redacted]' end,
        'contains_ciphertext_payloads', false,
        'health', to_jsonb(health),
        'security_events', events.rows
    )
    from health, events;
$$;

create or replace function public.create_hosted_logical_export_manifest(
    p_logbook_id uuid
)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
    select jsonb_build_object(
        'logbook_id', p_logbook_id,
        'exported_at', now(),
        'contains_ciphertext_payloads', true,
        'account_count', (
            select count(*)
            from public.logbook_memberships m
            where m.logbook_id = p_logbook_id
              and m.revoked_at is null
        ),
        'device_count', (
            select count(*)
            from public.devices d
            join public.logbook_memberships m on m.account_id = d.account_id
            where m.logbook_id = p_logbook_id
              and m.revoked_at is null
        ),
        'operation_count', (
            select count(*)
            from public.operations o
            where o.logbook_id = p_logbook_id
        ),
        'highest_revision', (
            select coalesce(max(o.revision), 0)
            from public.operations o
            where o.logbook_id = p_logbook_id
        ),
        'restore_target', 'separate Sydney Supabase project or disposable local database'
    )
    where public.elb_has_logbook_role(p_logbook_id, 'owner');
$$;

-- SECURITY DEFINER routines must not retain PostgreSQL's default PUBLIC execute grant.
revoke all on function public.get_hosted_pilot_health()
    from public, anon, authenticated;
revoke all on function public.create_redacted_hosted_diagnostics(uuid)
    from public, anon, authenticated;
revoke all on function public.create_hosted_logical_export_manifest(uuid)
    from public, anon, authenticated;

grant execute on function public.get_hosted_pilot_health() to authenticated;
grant execute on function public.create_redacted_hosted_diagnostics(uuid) to authenticated;
grant execute on function public.create_hosted_logical_export_manifest(uuid) to authenticated;

commit;

notify pgrst, 'reload schema';
