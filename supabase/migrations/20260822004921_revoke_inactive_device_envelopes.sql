-- Device-specific recovery envelopes are usable only while their recipient device
-- remains active. Enforce that relationship for every administrative status path,
-- including direct revocation and replacement-device supersession, and repair any
-- inactive rows left behind before this trigger existed.

create or replace function public.elb_revoke_inactive_device_envelopes()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
    if new.status <> 'active'
       and new.status is distinct from old.status then
        update public.key_envelopes
        set revoked_at = now()
        where recipient_device_id = new.device_id
          and revoked_at is null;
    end if;

    return new;
end;
$$;

drop trigger if exists devices_revoke_inactive_envelopes on public.devices;
create trigger devices_revoke_inactive_envelopes
after update of status on public.devices
for each row
execute function public.elb_revoke_inactive_device_envelopes();

update public.key_envelopes e
set revoked_at = now()
from public.devices d
where e.recipient_device_id = d.device_id
  and d.status <> 'active'
  and e.revoked_at is null;

revoke all on function public.elb_revoke_inactive_device_envelopes()
    from public, anon, authenticated;
