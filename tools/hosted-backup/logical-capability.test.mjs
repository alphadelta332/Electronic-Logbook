import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, sep } from 'node:path';
import { createServer } from 'node:net';
import { logicalCapabilityRpcSql, logicalShapeSql, revokeLogicalCapabilitySql } from './logical-rpc.mjs';

const hash = value => createHash('sha256').update(value).digest('hex');
const expiry = minutes => new Date(Date.now() + minutes * 60000).toISOString();
const contract = JSON.parse(readFileSync(new URL('./preview-logical-contract.json', import.meta.url)));

test('credential lifetime and lifecycle arguments fail closed before SQL execution', () => {
  const token = hash('synthetic');
  assert.throws(() => logicalCapabilityRpcSql(contract, token, expiry(31)));
  assert.throws(() => logicalCapabilityRpcSql(contract, token, expiry(-1)));
  assert.throws(() => logicalCapabilityRpcSql(contract, token, expiry(32*24*60), 'postgres', {daily:true}));
  assert.throws(() => logicalCapabilityRpcSql(contract, token, expiry(1), 'postgres', {expectedDefinitionHash:token}));
  assert.throws(() => logicalCapabilityRpcSql(contract, token, expiry(1), 'postgres', {daily:true,expectedDefinitionHash:'bad'}));
  assert.throws(() => revokeLogicalCapabilitySql(token, 'other_database'));
  assert.throws(() => revokeLogicalCapabilitySql(''));
  assert.throws(() => logicalCapabilityRpcSql(contract, 'not-a-digest', expiry(1)));
});

test('isolated PostgreSQL proves daily expiry, guarded rotation/revocation, API audit and unchanged data', async () => {
  const directory = mkdtempSync(join(tmpdir(), 'elb-capability-'));
  const data = join(directory, 'data');
  const pgBin = 'C:/Program Files/PostgreSQL/17/bin';
  const server = createServer();
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const port = server.address().port;
  await new Promise(resolve => server.close(resolve));
  const database = 'elb_backup_fixture_' + randomUUID().replaceAll('-', '');
  const env = {SystemRoot:process.env.SystemRoot, PATH:process.env.PATH, TEMP:process.env.TEMP, TMP:process.env.TMP,
    PGHOST:'127.0.0.1', PGPORT:String(port), PGUSER:'postgres', PGSSLMODE:'disable', PGCLIENTENCODING:'UTF8'};
  const command = (exe, args, input) => spawnSync(join(pgBin, exe + (process.platform==='win32'?'.exe':'')), args,
    {input,env,encoding:'utf8',windowsHide:true,timeout:30000,maxBuffer:8*1024*1024});
  const sql = (input, target=database) => {
    const result = command('psql', ['-X','-w','-qAt','-v','ON_ERROR_STOP=1','-d',target], input);
    assert.equal(result.status, 0, 'Synthetic fixture SQL failed: ' + result.stderr);
    return result.stdout.trim();
  };
  let initialized = false;
  try {
    const init = command('initdb', ['-D',data,'-U','postgres','-A','trust','--no-locale','--encoding=UTF8']);
    assert.equal(init.status,0,init.stderr);
    initialized = true;
    const start = command('pg_ctl', ['-D',data,'-l',join(directory,'server.log'),'-o',`-h 127.0.0.1 -p ${port}`,'-w','start']);
    assert.equal(start.status,0,start.stderr);
    sql(`create database ${database};`, 'postgres');
    sql(`create role anon nologin; create role authenticated nologin; create role service_role nologin;
      create schema auth; create schema storage; create schema supabase_migrations;
      create table auth.schema_migrations(version text); create table storage.migrations(id int,name text,hash text);
      create table supabase_migrations.schema_migrations(version text);
      create sequence auth.refresh_tokens_id_seq;
      create table public.synthetic(id int primary key,note text); insert into public.synthetic values(1,'preserve me');
      create type public.synthetic_type as enum ('value');
      create function public.typed(value public.synthetic_type) returns int language sql as 'select 1';`);
    const shape = JSON.parse(sql(logicalShapeSql));
    const fixture = {shape,generatedExpressions:null,ledgers:{auth:null,storage:null,application:null},apiAnonymousFunctions:[
      {schema:'public',name:'typed',args:'value public.synthetic_type',definer:false,bodyHash:hash('select 1'),extension:null}]};
    const first = randomBytes(32).toString('hex'), second = randomBytes(32).toString('hex');
    const install = (token, until, expectedDefinitionHash) => sql(logicalCapabilityRpcSql(fixture,hash(token),until,database,
      {daily:true,...(expectedDefinitionHash===undefined?{}:{expectedDefinitionHash})}));
    const fingerprint = () => sql("select encode(sha256(convert_to(pg_get_functiondef('public.elb_backup_snapshot()'::regprocedure),'UTF8')),'hex');");
    const request = (token, method='GET', role='anon', readOnly=true) => JSON.parse(sql(`begin ${readOnly?'read only':''};
      set local role ${role}; set local "request.method"='${method}';
      set local "request.headers"='${JSON.stringify({'x-flightlogx-backup-token':token})}';
      select public.elb_backup_snapshot(); rollback;`));
    const before = sql('select row_to_json(t)::text from public.synthetic t;');
    install(first,expiry(30*24*60));
    const originalHash = fingerprint();
    assert.equal(request(first).datasets.find(d=>d.table==='public.synthetic').rows[0], before);
    assert.throws(()=>install(second,expiry(1)),/Existing endpoint preserved/);
    assert.throws(()=>install(second,expiry(1),'0'.repeat(64)),/Endpoint changed/);
    assert.throws(()=>sql(revokeLogicalCapabilitySql('0'.repeat(64),database)),/Endpoint changed/);
    assert.equal(fingerprint(),originalHash);
    for(const token of ['', 'malformed', second])assert.throws(()=>request(token),/Export capability denied/);
    assert.throws(()=>request(first,'POST'),/Export capability denied/);
    assert.throws(()=>request(first,'GET','authenticated'),/permission denied/);
    assert.throws(()=>request(first,'GET','anon',false),/Export capability denied/);
    sql('grant select on public.synthetic to anon;');
    assert.throws(()=>install(second,expiry(1),originalHash),/Export API boundary changed/);
    sql('revoke select on public.synthetic from anon;');
    sql('grant select(note) on public.synthetic to anon;');
    assert.throws(()=>install(second,expiry(1),originalHash),/Export API boundary changed/);
    sql('revoke select(note) on public.synthetic from anon;');
    sql('grant authenticated to anon;');
    assert.throws(()=>install(second,expiry(1),originalHash),/Export API boundary changed/);
    sql('revoke authenticated from anon;');
    sql('alter role anon login;');
    assert.throws(()=>install(second,expiry(1),originalHash),/Export API boundary changed/);
    sql('alter role anon nologin;');
    sql("create function public.unexpected() returns int language sql as 'select 1';");
    assert.throws(()=>install(second,expiry(1),originalHash),/Export API boundary changed/);
    sql('drop function public.unexpected();');
    install(second,expiry(1),originalHash);
    const rotatedHash = fingerprint();
    assert.notEqual(rotatedHash,originalHash);
    assert.throws(()=>request(first),/Export capability denied/);
    assert.equal(request(second).datasets.find(d=>d.table==='public.synthetic').rows[0], before);
    sql("comment on function public.elb_backup_snapshot() is 'unrelated-owner-function';");
    assert.throws(()=>sql(revokeLogicalCapabilitySql(rotatedHash,database)),/Endpoint changed/);
    sql("comment on function public.elb_backup_snapshot() is 'flightlogx-daily-logical-v1';");
    install(first,new Date(Date.now()+1000).toISOString(),rotatedHash);
    await new Promise(resolve=>setTimeout(resolve,1200));
    assert.throws(()=>request(first),/Export capability denied/);
    sql(revokeLogicalCapabilitySql(fingerprint(),database));
    assert.equal(sql("select to_regprocedure('public.elb_backup_snapshot()') is null;"),'t');
    assert.equal(sql('select row_to_json(t)::text from public.synthetic t;'),before);
    // The supervised call retains its short lifetime, marker and no-overwrite contract.
    sql(logicalCapabilityRpcSql(fixture,hash(first),expiry(1),database));
    assert.equal(request(first).datasets.length,shape.length);
    assert.throws(()=>install(second,expiry(1),fingerprint()),/Endpoint changed/);
    assert.equal(sql("select obj_description('public.elb_backup_snapshot()'::regprocedure,'pg_proc');"),'flightlogx-supervised-logical-v1');
  } finally {
    if(initialized && command('pg_ctl',['-D',data,'status']).status===0)
      assert.equal(command('pg_ctl',['-D',data,'-w','stop','-m','fast']).status,0,'Isolated fixture must stop before deletion.');
    assert.ok(resolve(directory).startsWith(resolve(tmpdir())+sep) && directory.includes('elb-capability-'));
    rmSync(directory,{recursive:true,force:true});
  }
});
