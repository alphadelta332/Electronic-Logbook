-- Public reference data only; clients have no write policy.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('airport-catalog', 'airport-catalog', true, 4194304, array['application/json', 'application/gzip'])
on conflict (id) do nothing;

create extension if not exists pg_cron;
create extension if not exists pg_net with schema extensions;
create extension if not exists supabase_vault with schema vault;

-- The publisher exits without downloading OurAirports when this AIRAC cycle is current.
-- Hourly invocation also retries failures without needing an owner's PC or phone.
select cron.schedule('flightlogx-airport-catalog', '7 * * * *', $job$
    select net.http_post(
        url := (select decrypted_secret from vault.decrypted_secrets where name = 'airport_catalog_url') || '/functions/v1/airport-catalog',
        headers := jsonb_build_object('Content-Type', 'application/json',
            'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'airport_catalog_service_role')),
        body := '{}'::jsonb,
        timeout_milliseconds := 120000
    );
$job$);
