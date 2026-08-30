-- Restore the foundation device-status guard on projects whose migration ledger
-- records the trigger even though it is absent from pg_trigger.

begin;

drop trigger if exists trg_devices_authenticated_status_guard on public.devices;
create trigger trg_devices_authenticated_status_guard
    before update on public.devices
    for each row execute function public.elb_reject_authenticated_device_status_change();

commit;
