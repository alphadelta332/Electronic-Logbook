-- Apply only after Cloudflare's authenticated scheduled publisher has succeeded.
begin;
do $$
begin
    if exists(select 1 from cron.job where jobname <> 'flightlogx-airport-catalog'
        and command ilike '%net.%') then
        raise exception 'Another scheduled job still uses pg_net';
    end if;
    if exists(select 1 from pg_trigger t join pg_proc p on p.oid=t.tgfoid
        join pg_namespace n on n.oid=p.pronamespace
        where not t.tgisinternal and n.nspname='supabase_functions') then
        raise exception 'Database webhooks still require pg_net';
    end if;
    if exists(select 1 from pg_proc p join pg_namespace n on n.oid=p.pronamespace
        where n.nspname not in ('pg_catalog','information_schema','net')
        and p.proname <> 'grant_pg_net_access' and p.prosrc ilike '%net.%') then
        raise exception 'An application routine still references pg_net';
    end if;
    if exists(select 1 from pg_extension where extname='pg_net') then
        if exists(select 1 from net.http_request_queue) then
            raise exception 'Unsent HTTP requests remain; pg_net removal refused';
        end if;
    end if;
end $$;
select cron.unschedule(jobid) from cron.job where jobname='flightlogx-airport-catalog';
-- No CASCADE: an unexpected dependency must stop the cutover rather than be deleted.
drop extension if exists pg_net;
delete from vault.secrets where name in ('airport_catalog_url','airport_catalog_service_role');
commit;
