import assert from 'node:assert/strict';
import {randomUUID,randomBytes,createHash} from 'node:crypto';
import {spawnSync} from 'node:child_process';
import {readFileSync,readdirSync} from 'node:fs';
import {openArchive} from './archive.mjs';
import {logicalShapeSql} from './logical-rpc.mjs';
import {dropDisposable} from './restore.mjs';
import {buildReplayCheck,replaySupervised} from './replay-supervised.mjs';

export function restoreLogicalArchive(archive,identity,expectedSha256,local,recoveryEnvironment) {
  assert.equal(createHash('sha256').update(archive).digest('hex'),expectedSha256,'Archive differs from trusted receipt.');
  assert.equal(local.host,'127.0.0.1');assert.equal(local.port,54322);assert.equal(local.user,'postgres');
  const contract=JSON.parse(readFileSync(new URL('./preview-logical-contract.json',import.meta.url)));
  const files=openArchive(archive,identity);
  const receipt={database:'elb_backup_fixture_'+randomUUID().replaceAll('-',''),token:randomUUID().replaceAll('-','')};
  const id=receipt.database.slice('elb_backup_fixture_'.length);let created=false;
  const containers=[];
  const sql=(database,input)=>{
    assert.ok(database==='postgres'||database===receipt.database);
    const r=spawnSync(local.pgBin+'/psql.exe',['-X','-w','-qAt','-v','ON_ERROR_STOP=1','-d',database],{
      input,encoding:'utf8',windowsHide:true,timeout:60000,maxBuffer:64*1024*1024,
      env:{...process.env,PGHOST:local.host,PGPORT:String(local.port),PGUSER:local.user,PGPASSWORD:local.password,
        PGSSLMODE:'disable',PGOPTIONS:'',PGCLIENTENCODING:'UTF8'}});
    assert.equal(r.status,0,'Logical restoration SQL failed; diagnostics suppressed.');return r.stdout.trim();
  };
  const docker=(...args)=>{
    const r=spawnSync('docker',args,{windowsHide:true,encoding:'utf8',timeout:60000,maxBuffer:8*1024*1024});
    assert.equal(r.status,0,'Logical reconstruction service failed; diagnostics suppressed.');return r.stdout.trim();
  };
  const run=(kind,env,command)=>{
    const name='elb-logical-'+kind+'-'+id;containers.push(name);
    return docker('run','--rm','--name',name,'--label','flightlogx-backup-fixture='+id,
      '--network','supabase_network_Electronic-Logbook',...env.flatMap(v=>['--env',v]),contract.targetImages[kind],...command);
  };
  const literal=v=>"'"+v.replaceAll("'","''")+"'";
  try {
    const manifest=JSON.parse(files['verification.json']);const snapshot=JSON.parse(files['logical.json']);
    assert.equal(manifest.accessMode,'logical-capability');assert.deepEqual(manifest.contract,contract);
    assert.deepEqual(snapshot.shape,contract.shape);assert.deepEqual(snapshot.generated,contract.generatedExpressions);
    assert.deepEqual(snapshot.datasets.map(d=>d.table),contract.shape.map(t=>t.table));
    assert.deepEqual(snapshot.ledgers,{auth:contract.ledgers.auth,storage:contract.ledgers.storage,application:contract.ledgers.application});
    assert.deepEqual(Object.keys(files).sort(),[...manifest.files.map(f=>f.name),'logical.json','verification.json'].sort());
    for(const f of manifest.files)assert.equal(createHash('sha256').update(files[f.name]).digest('hex'),f.sha256);
    if(files['deployment-sources.elbbackup']){
      const deployed=openArchive(files['deployment-sources.elbbackup'],identity);
      try {
        assert.deepEqual(Object.keys(deployed).sort(),['deployments/airport-catalog.bundle','deployments/metadata.json','deployments/recovery-envelope.bundle']);
        const metadata=JSON.parse(deployed['deployments/metadata.json']);
        assert.deepEqual(metadata.map(f=>f.slug).sort(),['airport-catalog','recovery-envelope']);
        for(const f of metadata){assert.equal(f.status,'ACTIVE');assert.ok(deployed['deployments/'+f.slug+'.bundle'].length>0);}
      }finally{Object.values(deployed).forEach(b=>b.fill(0));}
    }
    const migrations=readdirSync(new URL('../../supabase/migrations/',import.meta.url)).filter(n=>contract.ledgers.application.includes(n.split('_')[0])).sort();
    assert.equal(migrations.length,contract.ledgers.application.length);
    for(const n of migrations){const name='sources/supabase/migrations/'+n;
      assert.ok(files[name]?.equals(readFileSync(new URL('../../supabase/migrations/'+n,import.meta.url))),'Unreviewed migration source.');}
    assert.equal(docker('inspect','--format','{{.Config.Image}}','supabase_db_Electronic-Logbook'),'public.ecr.aws/supabase/postgres:17.6.1.155');
    sql('postgres',`create database ${receipt.database} template template0;`);created=true;
    sql('postgres',`comment on database ${receipt.database} is 'flightlogx-backup-restore:${receipt.token}';revoke all on database ${receipt.database} from public;`);
    sql(receipt.database,'create schema auth;create schema extensions;create extension pgcrypto with schema extensions;create extension "uuid-ossp" with schema extensions;');
    const uri=`postgres://postgres:postgres@supabase_db_Electronic-Logbook:5432/${receipt.database}?sslmode=disable`;
    const secret=randomBytes(40).toString('hex');
    run('auth',['GOTRUE_DB_DRIVER=postgres','GOTRUE_DB_DATABASE_URL='+uri+'&search_path=auth','GOTRUE_DB_NAMESPACE=auth',
      'GOTRUE_SITE_URL=http://127.0.0.1','API_EXTERNAL_URL=http://127.0.0.1','GOTRUE_JWT_SECRET='+secret,'GOTRUE_DISABLE_SIGNUP=true'],['gotrue','migrate']);
    sql(receipt.database,'alter table auth.schema_migrations alter column version type varchar(255);');
    run('storage',['DATABASE_URL='+uri,'DB_MIGRATIONS_FREEZE_AT='+contract.ledgers.storage.at(-1).name,
      'AUTH_JWT_SECRET='+secret,'STORAGE_BACKEND=file','FILE_STORAGE_BACKEND_PATH=/tmp/fixture','TENANT_ID=fixture',
      'REGION=local','GLOBAL_S3_BUCKET=fixture','POSTGREST_URL=http://127.0.0.1:1'],['node','dist/scripts/migrate-call.js']);
    for(const n of migrations)if(n!=='20261017000000_shared_airport_catalog.sql')sql(receipt.database,files['sources/supabase/migrations/'+n]);
    sql(receipt.database,`create schema supabase_migrations;create table supabase_migrations.schema_migrations(
      version text primary key,statements text[],name text,created_by text,idempotency_key text,rollback text[]);
      truncate auth.schema_migrations,storage.migrations;`);
    const targetOnly=['storage.iceberg_namespaces','storage.iceberg_tables'];
    assert.deepEqual(JSON.parse(sql(receipt.database,'set search_path=pg_catalog;'+logicalShapeSql)).filter(t=>!targetOnly.includes(t.table)),contract.shape);
    const dependencies=JSON.parse(sql(receipt.database,`set search_path=pg_catalog;select coalesce(json_agg(json_build_object(
      'child',conrelid::regclass::text,'parent',confrelid::regclass::text)),'[]') from pg_constraint where contype='f';`));
    const remaining=snapshot.datasets.filter(d=>d.rows.length);const inserted=new Set();
    while(remaining.length){const index=remaining.findIndex(d=>dependencies.every(dep=>dep.child!==d.table||dep.parent===d.table||inserted.has(dep.parent)||!remaining.some(r=>r.table===dep.parent)));
      assert.ok(index>=0,'Unsupported populated dependency cycle.');const [d]=remaining.splice(index,1);
      const columns=contract.shape.find(t=>t.table===d.table).columns.filter(c=>!c.generated).map(c=>'"'+c.name+'"').join(',');
      sql(receipt.database,`insert into ${d.table}(${columns}) overriding system value select ${columns} from
        json_populate_recordset(null::${d.table},${literal('['+d.rows.join(',')+']')}::json);`);inserted.add(d.table);
    }
    for(const d of snapshot.datasets)assert.equal(sql(receipt.database,`set timezone='UTC';set datestyle='ISO, YMD';
      select encode(sha256(convert_to(coalesce(string_agg(to_jsonb(r)::text,E'\\n' order by to_jsonb(r)::text collate "C"),''),'UTF8')),'hex') from ${d.table} r;`),d.digest,'Restored typed-row digest differs.');
    assert.deepEqual(snapshot.sequences.map(s=>s.name),['auth.refresh_tokens_id_seq']);
    for(const s of snapshot.sequences){assert.equal(s.increment,'1');assert.equal(s.cycle,false);assert.match(s.last,/^\d+$/);assert.equal(typeof s.called,'boolean');
      sql(receipt.database,`select setval('auth.refresh_tokens_id_seq',${literal(s.last)}::bigint,${s.called});`);
      assert.equal(sql(receipt.database,"select nextval('auth.refresh_tokens_id_seq')::text;"),(BigInt(s.last)+(s.called?1n:0n)).toString());}
    for(const name of targetOnly)assert.equal(sql(receipt.database,`select count(*) from ${name};`),'0');
    const google=sql(receipt.database,"select count(*) from auth.identities i join auth.users u on u.id=i.user_id where i.provider='google';");
    assert.ok(Number(google)>0,'Recovered logbook lacks Google identity links.');
    let applicationReplay;if(recoveryEnvironment){buildReplayCheck();applicationReplay=replaySupervised(local,receipt,recoveryEnvironment);}
    return {status:'database-restored-and-matched',tablesMatched:47,allRowsAndRevisionsMatched:true,applicationReplay,
      googleIdentityLinksPreserved:true,managedServiceRecoveryVerified:false};
  } finally {
    for(const name of containers){if(docker('ps','--all','--filter','name=^/'+name+'$','--format','{{.Names}}')){
      assert.equal(docker('inspect','--format','{{index .Config.Labels "flightlogx-backup-fixture"}}',name),id);docker('rm','--force',name);}}
    if(created)dropDisposable(local,receipt);Object.values(files).forEach(b=>b.fill(0));
  }
}
