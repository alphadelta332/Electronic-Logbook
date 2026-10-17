import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { spawnSync } from 'node:child_process';

test('airport cutover refuses dependencies/queued work and removes only the obsolete scheduler and extension', () => {
  const database='elb_backup_fixture_'+randomUUID().replaceAll('-','');
  const executable=process.platform==='win32'?'C:/Program Files/PostgreSQL/17/bin/psql.exe':'psql';
  const sql=(target,input)=>spawnSync(executable,['-X','-w','-qAt','-v','ON_ERROR_STOP=1','-d',target],{
    env:{...process.env,PGHOST:'127.0.0.1',PGPORT:'54322',PGUSER:'postgres',PGPASSWORD:'postgres',PGSSLMODE:'disable',PGOPTIONS:''},
    input,encoding:'utf8',windowsHide:true,timeout:20000});
  const admin=input=>{const result=sql(database,input);assert.equal(result.status,0,'Disposable fixture SQL failed: '+result.stderr);return result.stdout.trim();};
  const migration=readFileSync(new URL('../migrations/20261017010000_external_airport_scheduler.sql',import.meta.url),'utf8');
  let created=false;
  try {
    assert.equal(sql('postgres',`create database ${database};`).status,0);created=true;
    admin(`create schema extensions;create extension pg_net with schema extensions;
      create schema cron;create table cron.job(jobid bigint,jobname text,command text);
      insert into cron.job values(1,'flightlogx-airport-catalog','select net.http_post()'),(2,'unrelated','select 1');
      create function cron.unschedule(bigint) returns boolean language sql as 'delete from cron.job where jobid=$1 returning true';
      create schema vault;create table vault.secrets(name text);
      insert into vault.secrets values('airport_catalog_url'),('airport_catalog_service_role'),('unrelated');
      create schema supabase_functions;`);
    const refused=()=>{
      assert.notEqual(sql(database,migration).status,0,'Unsafe cutover unexpectedly accepted');
      assert.equal(admin("select count(*) from cron.job where jobname='flightlogx-airport-catalog';"),'1');
      assert.equal(admin("select count(*) from pg_extension where extname='pg_net';"),'1');
      assert.equal(admin('select count(*) from vault.secrets;'),'3');
    };
    admin("insert into cron.job values(3,'another-http-job','select net.http_get()');");refused();
    admin('delete from cron.job where jobid=3;');
    admin("insert into net.http_request_queue(method,url,headers,body,timeout_milliseconds) values('GET','https://example.invalid','{}',null,1000);");refused();
    admin('delete from net.http_request_queue;');
    admin("create function public.other_http() returns text language sql as $$select 'net.http_post'::text$$;");refused();
    admin('drop function public.other_http();');
    admin(`create function supabase_functions.webhook() returns trigger language plpgsql as 'begin return new;end';
      create table public.fixture(id integer);create trigger fixture_webhook after insert on public.fixture
      for each row execute function supabase_functions.webhook();`);refused();
    admin('drop trigger fixture_webhook on public.fixture;');
    admin('create view public.unexpected_dependency as select id from net.http_request_queue;');refused();
    admin('drop view public.unexpected_dependency;');
    admin(migration);
    assert.equal(admin("select count(*) from pg_extension where extname='pg_net';"),'0');
    assert.equal(admin('select jobname from cron.job;'),'unrelated');
    assert.equal(admin('select name from vault.secrets;'),'unrelated');
    admin(migration);
  } finally {if(created)assert.equal(sql('postgres',`drop database ${database};`).status,0);}
});
