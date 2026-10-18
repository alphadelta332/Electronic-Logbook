import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID, randomBytes, createHash, createHmac, generateKeyPairSync } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { logicalShapeSql, logicalRpcFixtureSql } from './logical-rpc-fixture.mjs';
import { sealArchive, openArchive } from './archive.mjs';
import { dropDisposable } from './restore.mjs';
import { buildReplayCheck, replaySupervised } from './replay-supervised.mjs';

export async function proveLogicalRecovery({ managedDatabase = 'postgres', previewContract, targetOnlyTables = [], seedExtra, checkCapability, afterRestore } = {}) {
  const local = { host: '127.0.0.1', port: 54322, user: 'postgres', password: 'postgres', pgBin: 'C:/Program Files/PostgreSQL/17/bin' };
  const id = randomUUID().replaceAll('-', '');
  const source = { database: 'elb_backup_fixture_' + id, token: randomUUID().replaceAll('-', '') };
  const role = 'elb_rpc_' + id, authenticator = 'elb_auth_' + id;
  const password = randomBytes(32).toString('hex'), signingSecret = randomBytes(40).toString('hex');
  const container = 'elb-rpc-' + id;
  const literal = value => "'" + String(value).replaceAll("'", "''") + "'";
  const hash = value => createHash('sha256').update(value).digest('hex');
  const args = ['-X','-w','-qAt','-v','ON_ERROR_STOP=1'];
  const sql = (database, input) => {
    assert.match(database, /^(postgres|elb_backup_fixture_[a-f0-9]{32})$/);
    const result = spawnSync(local.pgBin + '/psql.exe', [...args,'-d',database], { input, encoding: 'utf8', windowsHide: true,
      timeout: 30000, maxBuffer: 64 * 1024 * 1024, env: { ...process.env, PGHOST:local.host,PGPORT:'54322',PGUSER:'postgres',
        PGPASSWORD:'postgres',PGSSLMODE:'disable',PGOPTIONS:'',PGCLIENTENCODING:'UTF8' } });
    assert.equal(result.status, 0, 'Disposable SQL failed; diagnostics suppressed: ' +
      (result.stderr.match(/ERROR:\s+[^\r\n]{1,180}/)?.[0] ?? 'no safe hint'));
    return result.stdout.trim();
  };
  const docker = (...command) => {
    const result = spawnSync('docker', command, { windowsHide:true, timeout:30000, maxBuffer:64*1024*1024 });
    assert.equal(result.status,0,'Disposable container operation failed; diagnostics suppressed.'); return result.stdout.toString().trim();
  };
  const shape = database => JSON.parse(sql(database, 'set search_path=pg_catalog;'+logicalShapeSql));
  const receipt = () => ({ database:'elb_backup_fixture_'+randomUUID().replaceAll('-',''),token:randomUUID().replaceAll('-','') });
  let sourceCreated=false,rolesCreated=false,containerId,environment;
  const buffers=[];
  assert.match(managedDatabase,/^(postgres|elb_backup_fixture_[a-f0-9]{32})$/);
  assert.ok(targetOnlyTables.every(t=>['storage.iceberg_tables','storage.iceberg_namespaces'].includes(t)));
  const migrations = readdirSync(new URL('../../supabase/migrations/',import.meta.url)).filter(n=>n.endsWith('.sql') &&
    (previewContract ? previewContract.ledgers.application.includes(n.split('_')[0]) : !n.startsWith('20261017'))).sort();
  const managed = Buffer.from(docker('exec','supabase_db_Electronic-Logbook','pg_dump','-U','postgres','-d',managedDatabase,
    '--schema-only','--no-owner','--no-privileges','--schema=auth','--schema=storage'));
  // Definitions come from the installed fresh-service runtime, never from participant data.
  const rebuild = target => {
    sql('postgres',`create database ${target.database} template template0;`);
    sql('postgres',`comment on database ${target.database} is 'flightlogx-backup-restore:${target.token}'; revoke all on database ${target.database} from public;`);
    if(target===source)sourceCreated=true;
    sql(target.database,`create schema extensions; create extension pgcrypto with schema extensions; create extension "uuid-ossp" with schema extensions;`);
    sql(target.database,managed.toString());
    if(target===source && targetOnlyTables.length)sql(target.database,`drop table ${targetOnlyTables.join(',')};`);
    for(const name of migrations) {
      const content=readFileSync(new URL('../../supabase/migrations/'+name,import.meta.url),'utf8');
      // The airport dataset is restored, while its outbound timer requires fresh target credentials.
      if(name==='20261017000000_shared_airport_catalog.sql' && target!==source)continue;
      sql(target.database,name==='20261017000000_shared_airport_catalog.sql' ? content.split('create extension')[0] : content);
    }
    sql(target.database,`create schema supabase_migrations; create table supabase_migrations.schema_migrations(version text primary key
      ${previewContract ? ',statements text[],name text,created_by text,idempotency_key text,rollback text[]' : ''});
      create table public.logical_typed(id bigint generated always as identity(start 9007199254740993 increment 7) primary key,
        note text,amount numeric(35,12),raw bytea,tags text[],document jsonb,happened timestamptz,
        doubled numeric generated always as(amount*2) stored);`);
  };
  try {
    // Explicitly require the existing local stack, not an arbitrary server at this port.
    assert.equal(docker('inspect','--format','{{.Config.Image}}','supabase_db_Electronic-Logbook'),'public.ecr.aws/supabase/postgres:17.6.1.155');
    assert.equal(sql('postgres',"select current_setting('server_version_num');"),'170006');
    rebuild(source); sourceCreated=true;
    sql(source.database,`create role ${role} nologin noinherit nosuperuser nocreatedb nocreaterole nobypassrls;
      create role ${authenticator} login noinherit password '${password}'; grant ${role} to ${authenticator};
      grant connect on database ${source.database} to ${authenticator};`); rolesCreated=true;
    if (previewContract) assert.deepEqual(shape(source.database).filter(t=>t.table!=='public.logical_typed'),previewContract.shape);
    buildReplayCheck();
    const seed=spawnSync('dotnet',[fileURLToPath(new URL('./ReplayCheck/bin/Debug/net10.0/ReplayCheck.dll',import.meta.url)),'seed'],{encoding:'utf8',windowsHide:true,timeout:30000});
    assert.equal(seed.status,0);const fixture=JSON.parse(seed.stdout),guid=fixture.logbookGuid,device=fixture.operations[0].deviceId.value.slice(4);
    environment=Buffer.from(`RECOVERY_KEK_BASE64=${fixture.kek}\nRECOVERY_KEY_VERSION_ID=backup-fixture-v1\n`);
    sql(source.database,`insert into auth.users(id,instance_id,aud,role,email,raw_app_meta_data) values('${guid}',
      '00000000-0000-0000-0000-000000000000','authenticated','authenticated','owner@example.invalid','{"provider":"google","providers":["google"]}');
      insert into auth.identities(user_id,provider_id,provider,identity_data) values('${guid}','synthetic-google-sub','google','{"sub":"synthetic-google-sub","email":"owner@example.invalid"}');
      insert into public.accounts(account_id,invited_email,status) values('${guid}','owner@example.invalid','active');
      insert into public.logbooks(logbook_id,owner_account_id,display_name) values('${guid}','${guid}','Synthetic recovered logbook');
      insert into public.logbook_memberships(logbook_id,account_id,role,granted_by_account_id,accepted_at) values('${guid}','${guid}','owner','${guid}',now());
      insert into public.devices(device_id,account_id,device_type,platform_label) values('${device}','${guid}','android','Synthetic device');
      insert into public.key_envelopes(logbook_id,recovery_method,wrapping_algorithm,key_version_id,ciphertext,nonce)
        values('${guid}','managed-service-v1','AES-256-GCM','backup-fixture-v1',${literal(fixture.keyEnvelope.ciphertext)},${literal(fixture.keyEnvelope.nonce)});
      insert into public.logical_typed(note,amount,raw,tags,document,happened) values
        (${literal('tabs\tand\nUnicode café 🛩')},900719925474099312345.123456789012,decode('00ff0a','hex'),ARRAY['',null,'comma,'],'{"large":900719925474099312345}','infinity'),
        (null,null,null,ARRAY[]::text[],'null','-infinity');
      insert into storage.buckets(id,name,public) values('airport-catalog','airport-catalog',true) on conflict(id) do nothing;
      ${migrations.map(n=>`insert into supabase_migrations.schema_migrations values(${literal(n.split('_')[0])});`).join('\n')}`);
    if(previewContract) {
      sql(source.database,`insert into auth.schema_migrations(version) values ${previewContract.ledgers.auth.map(v=>'('+literal(v)+')').join(',')};
        insert into storage.migrations(id,name,hash) values ${previewContract.ledgers.storage.map(v=>`(${v.id},${literal(v.name)},${literal(v.hash)})`).join(',')};
        update supabase_migrations.schema_migrations set statements=ARRAY['select 1;'],rollback=ARRAY['select 2;'],
          name='synthetic',created_by='fixture',idempotency_key='fixture-'||version;`);
    }
    if(seedExtra)seedExtra(source,sql,guid);
    const common=e=>[literal(guid),e.hostedRevision,literal(randomUUID()),literal(e.revisionId.value),literal(device),e.schemaVersion-1,
      literal(e.payloadCiphertext),literal(e.payloadNonce),literal(e.payloadTag),literal(e.payloadHash),literal(e.createdAt)].join(',');
    for(const e of fixture.operations) sql(source.database,`insert into public.operations(logbook_id,revision,operation_id,portable_revision_id,
      author_device_id,operation_format_version,payload_ciphertext,payload_nonce,payload_tag,payload_hash,client_created_at,entry_id,parent_revision_ids,operation_type)
      values(${common(e)},${literal(e.entryId.value)},${literal(JSON.stringify(e.parentRevisionIds.map(v=>v.value)))}::jsonb,'fixture');`);
    sql(source.database,`insert into public.configuration_revisions(logbook_id,revision,configuration_id,portable_revision_id,
      author_device_id,configuration_format_version,payload_ciphertext,payload_nonce,payload_tag,payload_hash,client_created_at) values(${common(fixture.configuration)});`);
    const contract=shape(source.database);
    assert.ok(contract.length>=45);assert.equal(contract.filter(t=>t.table.startsWith('public.')&&t.table!=='public.logical_typed').length,11);
    const sequences=JSON.parse(sql(source.database,"select json_agg(n.nspname||'.'||c.relname order by n.nspname,c.relname) from pg_class c join pg_namespace n on n.oid=c.relnamespace where c.relkind='S' and n.nspname in ('public','auth','storage','supabase_migrations');"));
    const install=(before='',pause='')=>sql(source.database,logicalRpcFixtureSql(source.database,role,contract,sequences,before,pause));
    assert.throws(()=>sql('postgres',logicalRpcFixtureSql(source.database,role,contract,sequences)),/Disposable SQL failed/);
    assert.equal(sql('postgres',"select to_regprocedure('public.elb_backup_snapshot_fixture()') is null;"),'t');
    install();
    assert.equal(sql(source.database,`select rolcanlogin or rolsuper or rolbypassrls or rolcreatedb or rolcreaterole from pg_roles where rolname='${role}';`),'f');
    const jwt=(claims={},secret=signingSecret)=>{
      const encode=x=>Buffer.from(JSON.stringify(x)).toString('base64url');const head=encode({alg:'HS256',typ:'JWT'});
      const body=encode({role,exp:Math.floor(Date.now()/1000)+600,...claims});const text=head+'.'+body;
      return text+'.'+createHmac('sha256',secret).update(text).digest('base64url');
    };
    containerId=docker('run','--detach','--name',container,'--label','flightlogx-backup-fixture='+id,
      '--network','supabase_network_Electronic-Logbook','--publish','127.0.0.1::3000',
      '--env',`PGRST_DB_URI=postgres://${authenticator}:${password}@supabase_db_Electronic-Logbook:5432/${source.database}`,
      '--env','PGRST_DB_SCHEMAS=public','--env','PGRST_DB_ANON_ROLE=anon','--env',`PGRST_JWT_SECRET=${signingSecret}`,
      'public.ecr.aws/supabase/postgrest:v14.15');
    assert.match(containerId,/^[a-f0-9]{64}$/);
    const binding=JSON.parse(docker('inspect','--format','{{json .NetworkSettings.Ports}}',container))['3000/tcp'][0];
    assert.equal(binding.HostIp,'127.0.0.1');const origin='http://127.0.0.1:'+binding.HostPort;
    const request=async(path='/rpc/elb_backup_snapshot_fixture',method='GET',token=jwt(),headers={})=>fetch(origin+path,
      {method,headers:{Authorization:'Bearer '+token,...(method==='POST'?{'Content-Type':'application/json'}:{}),...headers},
        ...(method==='POST'?{body:'{}'}:{}),signal:AbortSignal.timeout(15000)});
    let ready=false,lastError='no response';for(let i=0;i<60;i++){try{const r=await request();if(r.ok){ready=true;break;}const e=await r.json();lastError=`HTTP ${r.status}, ${e.code}: ${e.message}`;}catch{}await new Promise(r=>setTimeout(r,250));}assert.ok(ready,'Disposable API did not become ready: '+lastError);
    const capture=async()=>{const response=await request();assert.equal(response.status,200);const bytes=Buffer.from(await response.arrayBuffer());
      assert.ok(bytes.length<32*1024*1024);const snapshot=JSON.parse(bytes);assert.deepEqual(snapshot.shape,contract);assert.equal(snapshot.datasets.length,contract.length);
      for(const s of snapshot.sequences){assert.ok(BigInt(s.increment)>0n&&!s.cycle);assert.match(s.last,/^\d+$/);}return {bytes,snapshot};};
    const original=await capture();buffers.push(original.bytes);
    if(checkCapability)await checkCapability({source,execute:sql,origin,authenticator,anonJwt:jwt({role:'anon'}),snapshot:original.snapshot,recoveryEnvironment:environment});
    for(const [path,method,token,headers,code] of [
      ['/accounts','GET',jwt(),{},'42501'],['/accounts','POST',jwt(),{},'42501'],['/rpc/elb_backup_snapshot_fixture','POST',jwt(),{},'P0001'],
      ['/rpc/elb_backup_snapshot_fixture','GET',jwt({role:'anon'}),{},'42501'],['/rpc/elb_backup_snapshot_fixture','GET',jwt({exp:1}),{},'PGRST303'],
      ['/rpc/elb_backup_snapshot_fixture','GET',jwt({},'wrong-secret'),{},'PGRST301'],['/rpc/pg_sleep','POST',jwt(),{},'PGRST202'],
      ['/http_request_queue','GET',jwt(),{'Accept-Profile':'net'},'PGRST106'],['/decrypted_secrets','GET',jwt(),{'Accept-Profile':'vault'},'PGRST106']]){
      const response=await request(path,method,token,headers);assert.ok(!response.ok,'Forbidden API request succeeded.');
      assert.equal((await response.json()).code,code,'Denial must prove the expected boundary.');
    }
    assert.deepEqual((await capture()).snapshot.datasets,original.snapshot.datasets,'Denied requests changed source rows.');
    const waiting=async()=>{for(let i=0;i<80;i++){if(sql('postgres',`select count(*) from pg_stat_activity where datname='${source.database}' and wait_event='PgSleep';`)!=='0')return;await new Promise(r=>setTimeout(r,20));}throw new Error('Export concurrency barrier not reached.');};
    // A fresh inner snapshot must follow the locks, even when the outer GET snapshot is older.
    install('perform pg_sleep(1);');const late=capture();await waiting();
    sql(source.database,"truncate public.logical_typed; insert into public.logical_typed(note) values('after-truncate');");
    assert.equal((await late).snapshot.datasets.find(d=>d.table==='public.logical_typed').rows.length,1);
    install('','select pg_sleep(1)');const held=capture();await waiting();
    assert.throws(()=>sql(source.database,"set lock_timeout='100ms'; truncate public.logical_typed;"),/Disposable SQL failed/);
    sql(source.database,"insert into public.logical_typed(note) values('after-snapshot');");
    assert.equal((await held).snapshot.datasets.find(d=>d.table==='public.logical_typed').rows.length,1,'Later INSERT must stay outside one statement snapshot.');
    install();
    const keys=generateKeyPairSync('rsa',{modulusLength:3072,publicKeyEncoding:{type:'spki',format:'pem'},privateKeyEncoding:{type:'pkcs8',format:'pem'}});
    const reference=Buffer.from('Synthetic public reference bytes.');
    const archive=sealArchive({'logical.json':original.bytes,'reference/airport.json':reference},keys.publicKey);buffers.push(archive);
    const dependencies=JSON.parse(sql(source.database,`set search_path=pg_catalog;select coalesce(json_agg(json_build_object('child',conrelid::regclass::text,'parent',confrelid::regclass::text)),'[]') from pg_constraint where contype='f';`));
    let restored=0,rejected=0;
    const restore=async(bytes=archive,expected=hash(archive),mutate=()=>{})=>{
      assert.equal(hash(bytes),expected,'Archive differs from trusted digest.');const files=openArchive(bytes,keys.privateKey);let target;
      try{
        const snapshot=JSON.parse(files['logical.json']);mutate(snapshot);assert.deepEqual(snapshot.shape,contract);
        assert.deepEqual(snapshot.datasets.map(d=>d.table),contract.map(t=>t.table));assert.ok(files['reference/airport.json'].equals(reference));
        target=receipt();rebuild(target);assert.deepEqual(shape(target.database).filter(t=>!targetOnlyTables.includes(t.table)),contract);
        const remaining=snapshot.datasets.filter(d=>d.rows.length);const inserted=new Set();
        while(remaining.length){const index=remaining.findIndex(d=>dependencies.every(dep=>dep.child!==d.table||dep.parent===d.table||inserted.has(dep.parent)||!remaining.some(r=>r.table===dep.parent)));
          assert.ok(index>=0,'Unsupported populated dependency cycle.');const [d]=remaining.splice(index,1);const table=contract.find(t=>t.table===d.table);
          const columns=table.columns.filter(c=>!c.generated).map(c=>'"'+c.name+'"').join(',');
          sql(target.database,`insert into ${d.table}(${columns}) overriding system value select ${columns} from json_populate_recordset(null::${d.table},${literal('['+d.rows.join(',')+']')}::json);`);inserted.add(d.table);
        }
        for(const d of snapshot.datasets){const actual=sql(target.database,`select encode(sha256(convert_to(coalesce(string_agg(to_jsonb(r)::text,E'\\n' order by to_jsonb(r)::text collate "C"),''),'UTF8')),'hex') from ${d.table} r;`);assert.equal(actual,d.digest,'Restored typed rows/generated values differ.');}
        assert.deepEqual(snapshot.sequences.map(s=>s.name),sequences);
        for(const s of snapshot.sequences){assert.ok(BigInt(s.increment)>0n&&!s.cycle);sql(target.database,`select setval(${literal(s.name)},${literal(s.last)}::bigint,${s.called});`);
          const expected=s.called?BigInt(s.last)+BigInt(s.increment):BigInt(s.last);
          assert.equal(sql(target.database,`select nextval(${literal(s.name)})::text;`),expected.toString());}
        assert.deepEqual(replaySupervised(local,target,environment),{status:'application-replayed',logbooks:[{operations:6,configurations:1,activeEntries:1,deletedEntries:1,conflicts:0,customFields:1}]});
        assert.equal(sql(target.database,"select count(*) from auth.identities where provider='google' and provider_id='synthetic-google-sub';"),'1');
        for(const name of targetOnlyTables)assert.equal(sql(target.database,`select count(*) from ${name};`),'0');
        if(afterRestore)await afterRestore(target,sql,guid);
        restored++;
      }finally{if(target)dropDisposable(local,target);Object.values(files).forEach(b=>b.fill(0));}
    };
    await restore();
    for(const mutation of [s=>s.datasets.pop(),s=>s.datasets.find(d=>d.table==='public.operations').rows.pop(),
      s=>s.datasets.find(d=>d.table==='public.configuration_revisions').rows.splice(0),s=>s.shape.pop(),s=>s.sequences.pop(),
      s=>{s.sequences[0].cycle=true;}]){
      await assert.rejects(()=>restore(archive,hash(archive),mutation));rejected++;
    }
    const corrupt=Buffer.from(archive);corrupt[corrupt.length-1]^=1;await assert.rejects(()=>restore(corrupt));rejected++;
    sql(source.database,'alter table public.logical_typed add column unsupported text;');
    assert.ok(!(await request()).ok,'Schema drift must reject export.');
    console.log(JSON.stringify({scope:'disposable logical HTTP recovery only',tables:contract.length,restored,rejected,
      httpDenials:9,concurrentTruncateBlocked:true,postLockSnapshotFresh:true,laterInsertExcluded:true,
      originalReferenceBytesRestored:true,googleIdentityBindingRestored:true,realGoogleSignInVerified:false,hostedChanges:false}));
  } finally {
    environment?.fill(0);buffers.forEach(b=>b.fill(0));
    if(containerId){assert.equal(docker('inspect','--format','{{index .Config.Labels "flightlogx-backup-fixture"}}',containerId),id);docker('rm','--force',containerId);}
    if(sourceCreated)dropDisposable(local,source);
    if(rolesCreated)sql('postgres',`drop role ${authenticator}; drop role ${role};`);
  }
}

test('restricted real HTTP export encrypts and restores complete local managed/application rows into fresh schemas', () => proveLogicalRecovery());
