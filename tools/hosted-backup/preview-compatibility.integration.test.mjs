import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID, randomBytes, createHmac, createHash, generateKeyPairSync } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { proveLogicalRecovery } from './logical-rpc.integration.test.mjs';
import { logicalShapeSql } from './logical-rpc-fixture.mjs';
import { logicalCapabilityRpcSql } from './logical-rpc.mjs';
import { dropDisposable } from './restore.mjs';
import {gzipSync} from 'node:zlib';
import {fileURLToPath} from 'node:url';
import {reconstructionSources} from './orchestrate.mjs';
import {captureLogicalArchive} from './logical-client.mjs';
import {restoreLogicalArchive} from './logical-restore.mjs';
import {sealArchive} from './archive.mjs';

test('Preview managed revisions reconstruct every declared table and preserve application recovery', async () => {
  const contract = JSON.parse(readFileSync(new URL('./preview-logical-contract.json',import.meta.url)));
  const hash=b=>createHash('sha256').update(b).digest('hex');
  const catalogue=gzipSync(Buffer.from(JSON.stringify(Array.from({length:5000},(_,i)=>({i:'X'+String(i).padStart(4,'0'),n:'Synthetic '+i,y:0,x:0})))));
  const cataloguePath='snapshots/'+hash(catalogue)+'.json.gz';
  const references={'manifest.json':Buffer.from(JSON.stringify({schemaVersion:1,cycle:'2026-10-01',publishedAt:'2026-10-17T00:00:00Z',
    sha256:hash(catalogue),path:cataloguePath,count:5000})),[cataloguePath]:catalogue,'seed.json.gz':catalogue};
  const id = randomUUID().replaceAll('-','');
  const target = {database:'elb_backup_fixture_'+id,token:randomUUID().replaceAll('-','')};
  const local = {host:'127.0.0.1',port:54322,user:'postgres',password:'postgres',pgBin:'C:/Program Files/PostgreSQL/17/bin'};
  const docker = (...args) => {
    const r = spawnSync('docker',args,{windowsHide:true,timeout:60000,maxBuffer:8*1024*1024,encoding:'utf8'});
    assert.equal(r.status,0,'Fixture container command failed: '+r.stderr.slice(-1500));
    return (args[0]==='logs' ? r.stdout+r.stderr : r.stdout).trim();
  };
  const sql = (database,input) => {
    assert.match(database,/^(postgres|elb_backup_fixture_[a-f0-9]{32})$/);
    const r = spawnSync(local.pgBin+'/psql.exe',['-X','-w','-qAt','-v','ON_ERROR_STOP=1','-d',database],{
      input,windowsHide:true,encoding:'utf8',timeout:30000,maxBuffer:8*1024*1024,
      env:{...process.env,PGHOST:local.host,PGPORT:'54322',PGUSER:local.user,PGPASSWORD:local.password,
        PGSSLMODE:'disable',PGOPTIONS:'',PGCLIENTENCODING:'UTF8'}});
    assert.equal(r.status,0,'Fixture SQL failed: '+r.stderr.slice(-1000)); return r.stdout.trim();
  };
  const images = contract.targetImages;
  const secret = randomBytes(40).toString('hex');
  const containerNames = [];
  let created=false;
  const run = (kind,env,command) => {
    const name='elb-compat-'+kind+'-'+id;
    containerNames.push(name);
    return docker('run','--rm','--name',name,'--label','flightlogx-backup-fixture='+id,
      '--network','supabase_network_Electronic-Logbook',...env.flatMap(e=>['--env',e]),images[kind],...command);
  };
  const uri = database => `postgres://postgres:postgres@supabase_db_Electronic-Logbook:5432/${database}?sslmode=disable`;
  const authEnv = database => [`GOTRUE_DB_DRIVER=postgres`,`GOTRUE_DB_DATABASE_URL=${uri(database)}&search_path=auth`,
    'GOTRUE_DB_NAMESPACE=auth','GOTRUE_SITE_URL=http://127.0.0.1','API_EXTERNAL_URL=http://127.0.0.1',
    `GOTRUE_JWT_SECRET=${secret}`,'GOTRUE_DISABLE_SIGNUP=true'];
  try {
    assert.equal(docker('inspect','--format','{{.Config.Image}}','supabase_db_Electronic-Logbook'),
      'public.ecr.aws/supabase/postgres:17.6.1.155');
    sql('postgres',`create database ${target.database} template template0;`);created=true;
    sql('postgres',`comment on database ${target.database} is 'flightlogx-backup-restore:${target.token}';
      revoke all on database ${target.database} from public;`);
    sql(target.database,`create schema auth; create schema extensions; create extension pgcrypto with schema extensions;
      create extension "uuid-ossp" with schema extensions;`);
    run('auth',authEnv(target.database),['gotrue','migrate']);
    // The fresh migrator creates varchar(14); Preview retains the older wider ledger column.
    sql(target.database,'alter table auth.schema_migrations alter column version type varchar(255);');
    run('storage',[`DATABASE_URL=${uri(target.database)}`,`DB_MIGRATIONS_FREEZE_AT=${contract.ledgers.storage.at(-1).name}`,
      'AUTH_JWT_SECRET='+secret,'STORAGE_BACKEND=file','FILE_STORAGE_BACKEND_PATH=/tmp/fixture',
      'TENANT_ID=fixture','REGION=local','GLOBAL_S3_BUCKET=fixture','POSTGREST_URL=http://127.0.0.1:1'],
      ['node','dist/scripts/migrate-call.js']);
    const shape=JSON.parse(sql(target.database,'set search_path=pg_catalog;'+logicalShapeSql));
    const targetOnlyTables=['storage.iceberg_tables','storage.iceberg_namespaces'];
    assert.deepEqual(shape.filter(t=>t.table.startsWith('auth.')||t.table.startsWith('storage.'))
      .filter(t=>!targetOnlyTables.includes(t.table)),contract.shape.filter(t=>t.table.startsWith('auth.')||t.table.startsWith('storage.')));
    assert.deepEqual(shape.filter(t=>!contract.shape.some(s=>s.table===t.table)).map(t=>t.table).sort(),
      targetOnlyTables.sort());
    assert.deepEqual(JSON.parse(sql(target.database,`set search_path=pg_catalog;
      select json_agg(json_build_object('table',n.nspname||'.'||c.relname,'column',a.attname,
        'expression',pg_get_expr(d.adbin,d.adrelid)) order by n.nspname,c.relname,a.attnum)
      from pg_attribute a join pg_class c on c.oid=a.attrelid join pg_namespace n on n.oid=c.relnamespace
      join pg_attrdef d on d.adrelid=c.oid and d.adnum=a.attnum where a.attgenerated<>''
      and n.nspname in ('auth','storage','public','supabase_migrations');`)),contract.generatedExpressions);
    // Current Auth folds seven historical migrations into 00; recovery still inserts all source ledger rows.
    const folded=['20171026211738','20171026211808','20171026211834','20180103212743',
      '20180108183307','20180119214651','20180125194653'];
    assert.deepEqual(JSON.parse(sql(target.database,'select json_agg(version order by version) from auth.schema_migrations;')),
      contract.ledgers.auth.filter(v=>!folded.includes(v)));
    assert.deepEqual(JSON.parse(sql(target.database,"select json_agg(json_build_object('id',id,'name',name,'hash',hash) order by id) from storage.migrations;")),contract.ledgers.storage);
    const sequence=JSON.parse(sql(target.database,`select json_build_object('name','auth.refresh_tokens_id_seq',
      'start',seqstart::text,'increment',seqincrement::text,'min',seqmin::text,'max',seqmax::text,
      'cache',seqcache::text,'cycle',seqcycle) from pg_sequence where seqrelid='auth.refresh_tokens_id_seq'::regclass;`));
    assert.deepEqual(sequence,contract.sequences.find(s=>s.name===sequence.name));
    await proveLogicalRecovery({managedDatabase:target.database,previewContract:contract,targetOnlyTables,
      checkCapability:async({source,execute,origin,authenticator,anonJwt,snapshot,recoveryEnvironment})=>{
        execute(source.database,`grant anon to ${authenticator};`);
        const token=randomBytes(32).toString('hex');
        const expected={...contract,shape:snapshot.shape,generatedExpressions:JSON.parse(execute(source.database,`set search_path=pg_catalog;
          select json_agg(json_build_object('table',n.nspname||'.'||c.relname,'column',a.attname,'expression',pg_get_expr(d.adbin,d.adrelid))
          order by n.nspname,c.relname,a.attnum) from pg_attribute a join pg_class c on c.oid=a.attrelid join pg_namespace n on n.oid=c.relnamespace
          join pg_attrdef d on d.adrelid=c.oid and d.adnum=a.attnum where a.attgenerated<>'' and n.nspname in ('auth','storage','public','supabase_migrations');`))};
        const install=expiry=>execute(source.database,logicalCapabilityRpcSql(expected,createHash('sha256').update(token).digest('hex'),expiry,source.database));
        install(new Date(Date.now()+600000).toISOString());
        const request=(value=token,method='GET')=>fetch(origin+'/rpc/elb_backup_snapshot',{method,headers:{Authorization:'Bearer '+anonJwt,
          'x-flightlogx-backup-token':value,...(method==='POST'?{'Content-Type':'application/json'}:{})},...(method==='POST'?{body:'{}'}:{}),signal:AbortSignal.timeout(10000)});
        let response;for(let i=0;i<50;i++){response=await request();if(response.status!==404)break;await new Promise(r=>setTimeout(r,100));}
        if(!response.ok)throw new Error('Capability fixture HTTP '+response.status+': '+await response.text());
        const captured=await response.json();assert.deepEqual(captured.datasets,snapshot.datasets);
        assert.deepEqual(captured.context.method,'GET');assert.equal(captured.context.readOnly,'on');assert.equal(captured.context.isolation,'read committed');
        const logical={...captured,shape:contract.shape,generated:contract.generatedExpressions,
          datasets:captured.datasets.filter(d=>d.table!=='public.logical_typed'),sequences:captured.sequences.filter(s=>s.name==='auth.refresh_tokens_id_seq')};
        const sources=reconstructionSources(fileURLToPath(new URL('../../',import.meta.url)));
        const keys=generateKeyPairSync('rsa',{modulusLength:3072,publicKeyEncoding:{type:'spki',format:'pem'},privateKeyEncoding:{type:'pkcs8',format:'pem'}});
        const job={origin:'https://'+'a'.repeat(20)+'.supabase.co',publishableKey:'sb_publishable_fixture',capability:token,recipient:keys.publicKey,contract,
          sources:Object.fromEntries(Object.entries(sources.files).map(([n,b])=>[n,b.toString('base64')])),
          sourceManifest:{revision:sources.revision,dirty:sources.dirty,files:Object.entries(sources.files).map(([name,b])=>({name,sha256:hash(b)}))}};
        const deploymentBundle=sealArchive({'deployments/airport-catalog.bundle':Buffer.from('synthetic airport deployment'),
          'deployments/recovery-envelope.bundle':Buffer.from('synthetic recovery deployment'),
          'deployments/metadata.json':Buffer.from(JSON.stringify([{slug:'airport-catalog',status:'ACTIVE'},{slug:'recovery-envelope',status:'ACTIVE'}]))},keys.publicKey);
        job.sources['deployment-sources.elbbackup']=deploymentBundle.toString('base64');
        job.sourceManifest.files.push({name:'deployment-sources.elbbackup',sha256:hash(deploymentBundle)});
        const originalFetch=globalThis.fetch;let mode='normal';
        globalThis.fetch=async url=>{
          const path=new URL(url).pathname;
          if(path==='/rest/v1/rpc/elb_backup_snapshot')return new Response(mode==='oversized'?Buffer.alloc(32*1024*1024+1):JSON.stringify(logical));
          const name=path.split('/airport-catalog/')[1];assert.ok(references[name]);return new Response(references[name]);
        };
        try {
          const archive=await captureLogicalArchive(job);
          assert.deepEqual(restoreLogicalArchive(archive,keys.privateKey,hash(archive),local,recoveryEnvironment),{
            status:'database-restored-and-matched',tablesMatched:47,allRowsAndRevisionsMatched:true,googleIdentityLinksPreserved:true,
            managedServiceRecoveryVerified:false,applicationReplay:{status:'application-replayed',logbooks:[{
              operations:6,configurations:1,activeEntries:1,deletedEntries:1,conflicts:0,customFields:1}]}});
          assert.throws(()=>restoreLogicalArchive(archive,keys.privateKey,'0'.repeat(64),local,recoveryEnvironment));
          mode='oversized';await assert.rejects(()=>captureLogicalArchive(job),/approved limit/);mode='normal';
          job.sourceManifest.files[0].sha256='0'.repeat(64);await assert.rejects(()=>captureLogicalArchive(job));
          console.log(JSON.stringify({productionLogicalClientAnd47TableRestorerVerified:true,referenceBytesBundled:true,sourceManifestAndSizeDenials:true}));
        }finally{globalThis.fetch=originalFetch;Object.values(sources.files).forEach(b=>b.fill(0));}
        for(const [value,method] of [['','GET'],['bad','GET'],[randomBytes(32).toString('hex'),'GET'],[token,'POST']]){
          const denied=await request(value,method);assert.equal(denied.status,400);assert.equal((await denied.json()).code,'P0001');
        }
        execute(source.database,'drop function public.elb_backup_snapshot();');
        install(new Date(Date.now()+1000).toISOString());await new Promise(r=>setTimeout(r,1200));
        assert.equal((await request()).status,400);
        console.log(JSON.stringify({opaqueCapabilityHttpVerified:true,deniedMissingMalformedWrongPostAndExpired:true,sourceDataUnchanged:true}));
      },
      seedExtra:(source,execute,guid)=>execute(source.database,`
        update auth.users set email_confirmed_at=now(),created_at=now(),updated_at=now(),confirmation_token='',
          recovery_token='',email_change_token_new='',email_change='',phone_change_token='',
          email_change_token_current='',reauthentication_token='' where id='${guid}';
        update auth.identities set created_at=now(),updated_at=now(),last_sign_in_at=now() where user_id='${guid}';
        insert into auth.sso_providers(id,resource_id) values('${guid}','synthetic-sso');
        insert into auth.scim_users(id,sso_provider_id,user_id,resource) values('${guid}','${guid}','${guid}',
          '{"userName":"Synthetic Café","externalId":"fixture-external","active":false}');
        ${Object.entries(references).map(([name,b])=>`insert into storage.objects(bucket_id,name,metadata) values('airport-catalog','${name}',
          '${JSON.stringify({size:b.length,eTag:createHash('md5').update(b).digest('hex')})}');`).join('\n')}`),
      afterRestore:async(restored,execute,guid)=>{
        const name='elb-compat-live-'+id; containerNames.push(name);
        const ledgerBefore=execute(restored.database,'select md5(string_agg(version,\',\' order by version)) from auth.schema_migrations;');
        docker('run','--detach','--name',name,'--label','flightlogx-backup-fixture='+id,
          '--network','supabase_network_Electronic-Logbook','--publish','127.0.0.1::9999',
          ...authEnv(restored.database).flatMap(e=>['--env',e]),'--env','GOTRUE_API_HOST=0.0.0.0',
          '--env','GOTRUE_API_PORT=9999','--env','GOTRUE_JWT_ADMIN_ROLES=service_role',images.auth,'gotrue','serve');
        try {
          const ports=JSON.parse(docker('inspect','--format','{{json .NetworkSettings.Ports}}',name))['9999/tcp'][0];
          assert.equal(ports.HostIp,'127.0.0.1');const origin='http://127.0.0.1:'+ports.HostPort;
          let ready=false;for(let i=0;i<50;i++){
            try{const r=await fetch(origin+'/health',{signal:AbortSignal.timeout(1000)});if(r.ok){
              assert.equal((await r.json()).version,contract.authVersion);ready=true;break;}}catch{}
            await new Promise(r=>setTimeout(r,100));
          }assert.ok(ready,'Restored disposable Auth service did not become healthy.');
          const encode=v=>Buffer.from(JSON.stringify(v)).toString('base64url');
          const claims=encode({alg:'HS256',typ:'JWT'})+'.'+encode({role:'service_role',exp:Math.floor(Date.now()/1000)+60});
          const jwt=claims+'.'+createHmac('sha256',secret).update(claims).digest('base64url');
          const r=await fetch(origin+'/admin/users/'+guid,{headers:{Authorization:'Bearer '+jwt},signal:AbortSignal.timeout(5000)});
          assert.equal(r.status,200,docker('logs','--tail','8',name));const user=await r.json();assert.equal(user.id,guid);
          assert.equal(user.identities.find(i=>i.provider==='google').identity_data.sub,'synthetic-google-sub');
          assert.equal(ledgerBefore,execute(restored.database,'select md5(string_agg(version,\',\' order by version)) from auth.schema_migrations;'));
        } finally {
          assert.equal(docker('inspect','--format','{{index .Config.Labels "flightlogx-backup-fixture"}}',name),id);
          docker('rm','--force',name);
        }
      }});
    console.log(JSON.stringify({scope:'Preview logical compatibility',requiredTables:47,authVersion:contract.authVersion,
      authMigrations:contract.ledgers.auth.length,storageMigrations:contract.ledgers.storage.length,
      generatedFields:contract.generatedExpressions.length,restoredAuthApiVerified:true,targetOnlyEmptyTables:2,
      images,hostedChanges:false,realGoogleSignInVerified:false}));
  } finally {
    for(const name of containerNames){
      const existing=docker('ps','--all','--filter','name=^/'+name+'$','--format','{{.Names}}');
      if(existing){assert.equal(docker('inspect','--format','{{index .Config.Labels "flightlogx-backup-fixture"}}',name),id);docker('rm','--force',name);}
    }
    if(created)dropDisposable(local,target);
  }
});
