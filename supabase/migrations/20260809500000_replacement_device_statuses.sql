-- Enum additions must commit before a later migration can use the new values.
alter type public.elb_device_status add value if not exists 'pending';
alter type public.elb_device_status add value if not exists 'superseded';
