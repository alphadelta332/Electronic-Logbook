-- External Free-plan activity probe: no participant data or elevated privileges.
begin;

create or replace function public.preview_activity_probe()
returns jsonb
language sql
stable
security invoker
set search_path = ''
as $$
    select pg_catalog.jsonb_build_object(
        'status', 'ok',
        'database_time', pg_catalog.now()
    );
$$;

revoke all on function public.preview_activity_probe() from public, anon, authenticated;
grant usage on schema public to anon;
grant execute on function public.preview_activity_probe() to anon;

-- Older routines inherited PUBLIC execute. Preserve application/service access
-- explicitly before removing that anonymous route to participant data.
grant execute on function
    public.elb_current_account_id(),
    public.elb_is_active_account(uuid),
    public.elb_has_logbook_role(uuid, public.elb_logbook_role),
    public.elb_device_belongs_to_current_account(uuid),
    public.elb_reject_operation_mutation(),
    public.elb_reject_authenticated_device_status_change(),
    public.elb_reject_authenticated_invitation_change(),
    public.elb_reject_configuration_revision_mutation(),
    public.read_missing_operations(uuid, bigint, integer),
    public.record_operation_ack(uuid, uuid, bigint, bigint, bigint, text),
    public.append_hosted_configuration_revision(uuid, uuid, uuid, text, integer, text, text, text, text, timestamptz),
    public.read_hosted_configuration_revisions(uuid, bigint, integer)
to authenticated, service_role;

revoke execute on function
    public.elb_current_account_id(),
    public.elb_is_active_account(uuid),
    public.elb_has_logbook_role(uuid, public.elb_logbook_role),
    public.elb_device_belongs_to_current_account(uuid),
    public.elb_reject_operation_mutation(),
    public.elb_reject_authenticated_device_status_change(),
    public.elb_reject_authenticated_invitation_change(),
    public.elb_reject_configuration_revision_mutation(),
    public.read_missing_operations(uuid, bigint, integer),
    public.record_operation_ack(uuid, uuid, bigint, bigint, bigint, text),
    public.append_hosted_configuration_revision(uuid, uuid, uuid, text, integer, text, text, text, text, timestamptz),
    public.read_hosted_configuration_revisions(uuid, bigint, integer)
from public, anon;

commit;
