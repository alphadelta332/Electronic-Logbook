-- Public reference data only. No client INSERT/UPDATE/DELETE policy is granted.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('aircraft-catalog', 'aircraft-catalog', true, 1048576, array['application/json', 'application/gzip'])
on conflict (id) do nothing;
