// One supervised run. Owner credentials stay in this controller, never the export client.
import assert from 'node:assert/strict';
import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import {resolve,join,isAbsolute} from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes,createHash,createPublicKey} from 'node:crypto';
import {spawnSync} from 'node:child_process';
import {postgres} from './postgres.mjs';
import {logicalCapabilityRpcSql,logicalShapeSql} from './logical-rpc.mjs';
import {reconstructionSources} from './orchestrate.mjs';
import {sealArchive} from './archive.mjs';

const hash=b=>createHash('sha256').update(b).digest('hex');
let installed=false,connection,definitionHash,stage='usage/output validation';
try {
  const args=process.argv.slice(2);assert.equal(args.length,4);assert.equal(args[0],'--usage');assert.equal(args[2],'--output');
  assert.ok(isAbsolute(args[1])&&isAbsolute(args[3]));const output=args[3];
  assert.ok(!existsSync(output)&&!existsSync(output+'.receipt.json'),'Existing output is preserved.');
  const usage=JSON.parse(readFileSync(args[1]));
  assert.equal(usage.plan,'free');assert.equal(usage.scope,'all-projects-current-billing-period');
  assert.ok(Date.now()-Date.parse(usage.checkedAt)>=0&&Date.now()-Date.parse(usage.checkedAt)<2*60*60*1000,'Usage evidence is stale.');
  for(const name of ['egress','cachedEgress']){
    assert.ok(Number.isFinite(usage[name].usedBytes)&&usage[name].usedBytes>=0&&Number.isFinite(usage[name].includedBytes));
    assert.ok(usage[name].includedBytes-usage[name].usedBytes>=64*1024*1024,'Insufficient verified free headroom.');
  }
  stage='owner metadata and free-plan validation';
  const root=join(process.env.LOCALAPPDATA,'ElectronicLogbook');
  const metadata=JSON.parse(readFileSync(join(root,'Supabase/hosted-preview-projects.local.json'))).preview;
  const management=readFileSync(join(root,'Supabase/access-token.txt'),'utf8').trim();
  const get=async path=>{const r=await fetch('https://api.supabase.com/v1'+path,{headers:{Authorization:'Bearer '+management},
    signal:AbortSignal.timeout(30000)});assert.ok(r.ok,`Metadata GET failed (${r.status}); details suppressed.`);return r.json();};
  const project=await get('/projects/'+metadata.project_ref);assert.equal(project.name,'FlightLogX Preview');assert.equal(project.region,'ap-southeast-2');assert.equal(project.status,'ACTIVE_HEALTHY');
  const organization=await get('/organizations/'+project.organization_id);assert.equal(organization.plan,'free');
  const api=await get('/projects/'+metadata.project_ref+'/postgrest');assert.equal(api.db_schema,'public,graphql_public');
  connection={host:'aws-0-ap-southeast-2.pooler.supabase.com',port:5432,database:'postgres',user:`postgres.${metadata.project_ref}`,
    password:metadata.db_password,projectRef:metadata.project_ref,pgBin:'C:/Program Files/PostgreSQL/17/bin',
    sslRootCert:join(root,'HostedBackup/source-root.crt')};
  const sql=(q,write=false)=>postgres(connection,'psql',['-X','-w','-qAt','-v','ON_ERROR_STOP=1'],
    write?q:`begin read only;set local search_path=pg_catalog;${q} rollback;`,true).toString().trim();
  const contract=JSON.parse(readFileSync(new URL('./preview-logical-contract.json',import.meta.url)));
  assert.deepEqual(JSON.parse(sql(logicalShapeSql)),contract.shape);
  stage='effective API capability audit';
  const boundary=JSON.parse(sql(`select json_build_object(
    'login',(select rolcanlogin or rolsuper or rolbypassrls or rolcreaterole or rolcreatedb from pg_roles where rolname='anon'),
    'memberships',(select count(*) from pg_auth_members where member='anon'::regrole),
    'accessibleRelations',(select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname in ('public','graphql_public')
      and c.relkind in ('r','p','v','m','f') and (has_table_privilege('anon',c.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
      or exists(select 1 from pg_attribute a where a.attrelid=c.oid and a.attnum>0 and not a.attisdropped and has_column_privilege('anon',c.oid,a.attnum,'SELECT,INSERT,UPDATE,REFERENCES')))),
    'functions',(select json_agg(json_build_object('schema',n.nspname,'name',p.proname,'args',pg_get_function_identity_arguments(p.oid),
      'definer',p.prosecdef,'bodyHash',encode(sha256(convert_to(p.prosrc,'UTF8')),'hex'),
      'extension',(select e.extname from pg_depend d join pg_extension e on e.oid=d.refobjid where d.classid='pg_proc'::regclass and d.objid=p.oid and d.deptype='e'))
      order by n.nspname,p.proname,pg_get_function_identity_arguments(p.oid)) from pg_proc p join pg_namespace n on n.oid=p.pronamespace
      where n.nspname in ('public','graphql_public') and has_schema_privilege('anon',n.oid,'USAGE') and has_function_privilege('anon',p.oid,'EXECUTE')));`));
  assert.equal(boundary.login,false);assert.equal(boundary.memberships,0);assert.equal(boundary.accessibleRelations,0);
  assert.deepEqual(boundary.functions,contract.apiAnonymousFunctions,'Exposed capability boundary changed.');
  const keys=await get('/projects/'+metadata.project_ref+'/api-keys');const publicKey=keys.find(k=>k.type==='publishable')?.api_key;
  assert.ok(publicKey?.startsWith('sb_publishable_'));
  const recipient=readFileSync(join(root,'HostedBackup/recipient.pem'),'utf8');
  const recoveryReceipt=JSON.parse(readFileSync(join(root,'HostedBackup/recovery-receipt.json')));
  const recipientSha256=hash(createPublicKey(recipient).export({type:'spki',format:'der'}));
  assert.equal(recoveryReceipt.independentReadbackPassed,true);assert.equal(recipientSha256,recoveryReceipt.recipientSha256);
  stage='protected deployed-source capture';
  const functions=await get('/projects/'+metadata.project_ref+'/functions');
  const deployments=functions.filter(f=>['airport-catalog','recovery-envelope'].includes(f.slug));assert.equal(deployments.length,2);
  const deployedFiles={};
  try {
    for(const f of deployments){assert.equal(f.status,'ACTIVE');
      const r=await fetch(`https://api.supabase.com/v1/projects/${metadata.project_ref}/functions/${f.slug}/body`,{
        headers:{Authorization:'Bearer '+management},redirect:'error',signal:AbortSignal.timeout(30000)});
      assert.ok(r.ok&&r.body,'Deployed source read failed.');const chunks=[];let size=0;
      for await(const b of r.body){size+=b.length;assert.ok(size<=4*1024*1024);chunks.push(b);}
      deployedFiles['deployments/'+f.slug+'.bundle']=Buffer.concat(chunks);
    }
    deployedFiles['deployments/metadata.json']=Buffer.from(JSON.stringify(deployments));
  }catch{Object.values(deployedFiles).forEach(b=>b.fill(0));throw new Error('Deployed source capture failed.');}
  const protectedDeployments=sealArchive(deployedFiles,recipient);Object.values(deployedFiles).forEach(b=>b.fill(0));
  const sourceRoot=resolve(fileURLToPath(new URL('../../',import.meta.url)));const source=reconstructionSources(sourceRoot);
  const capability=randomBytes(32).toString('hex');const expiresAt=new Date(Date.now()+20*60*1000).toISOString();
  stage='temporary endpoint installation';
  sql(logicalCapabilityRpcSql(contract,hash(capability),expiresAt),true);installed=true;
  definitionHash=sql("select encode(sha256(convert_to(pg_get_functiondef('public.elb_backup_snapshot()'::regprocedure),'UTF8')),'hex');");
  const roleCheck=JSON.parse(sql(`select json_build_object('owner',pg_get_userbyid(proowner),'definer',prosecdef,
    'publicExecute',exists(select 1 from aclexplode(proacl) where grantee=0),
    'anonExecute',has_function_privilege('anon',oid,'EXECUTE'),'authenticatedExecute',has_function_privilege('authenticated',oid,'EXECUTE'))
    from pg_proc where oid='public.elb_backup_snapshot()'::regprocedure;`));
  assert.deepEqual(roleCheck,{owner:'postgres',definer:true,publicExecute:false,anonExecute:true,authenticatedExecute:false});
  const origin=`https://${metadata.project_ref}.supabase.co`;
  stage='hosted denial preflight';
  let ready=false;const denials=[];
  for(let i=0;i<25;i++){
    const r=await fetch(origin+'/rest/v1/rpc/elb_backup_snapshot',{headers:{apikey:publicKey},signal:AbortSignal.timeout(10000)});
    const body=await r.json();if(r.status===400&&body.code==='P0001'){ready=true;break;}
    assert.ok(r.status===404,'Unexpected hosted preflight denial.');await new Promise(r=>setTimeout(r,300));
  }assert.ok(ready,'Hosted endpoint cache did not become ready.');denials.push('missing-token');
  for(const [value,method] of [['invalid','GET'],[randomBytes(32).toString('hex'),'GET'],[capability,'POST']]){
    const r=await fetch(origin+'/rest/v1/rpc/elb_backup_snapshot',{method,headers:{apikey:publicKey,'x-flightlogx-backup-token':value,
      ...(method==='POST'?{'Content-Type':'application/json'}:{})},...(method==='POST'?{body:'{}'}:{}),signal:AbortSignal.timeout(10000)});
    assert.equal(r.status,400);assert.equal((await r.json()).code,'P0001');denials.push(method==='POST'?'post':'wrong-token');
  }
  const request={origin,publishableKey:publicKey,capability,recipient,contract,
    sources:{...Object.fromEntries(Object.entries(source.files).map(([n,b])=>[n,b.toString('base64')])),
      'deployment-sources.elbbackup':protectedDeployments.toString('base64')},
    sourceManifest:{revision:source.revision,dirty:source.dirty,files:[...Object.entries(source.files).map(([name,b])=>({name,sha256:hash(b)})),
      {name:'deployment-sources.elbbackup',sha256:hash(protectedDeployments)}]}};
  stage='restricted client capture/reference validation';
  const client=spawnSync(process.execPath,[fileURLToPath(new URL('./logical-client.mjs',import.meta.url))],{
    input:JSON.stringify(request),windowsHide:true,timeout:120000,maxBuffer:52*1024*1024,
    env:{SystemRoot:process.env.SystemRoot,PATH:process.env.PATH,TEMP:process.env.TEMP}});
  assert.equal(client.status,0,'Restricted export client failed; records and diagnostics suppressed.');
  const current=reconstructionSources(sourceRoot);
  const currentDeployments=(await get('/projects/'+metadata.project_ref+'/functions')).filter(f=>['airport-catalog','recovery-envelope'].includes(f.slug));
  assert.deepEqual(currentDeployments.map(f=>({slug:f.slug,version:f.version,updated_at:f.updated_at})).sort((a,b)=>a.slug.localeCompare(b.slug)),
    deployments.map(f=>({slug:f.slug,version:f.version,updated_at:f.updated_at})).sort((a,b)=>a.slug.localeCompare(b.slug)),'Deployment changed during export.');
  assert.deepEqual(Object.keys(source.files),Object.keys(current.files));for(const n of Object.keys(source.files))assert.ok(source.files[n].equals(current.files[n]),'Source changed during export.');
  const receipt={checkedAt:new Date().toISOString(),accessMode:'logical-capability',bytes:client.stdout.length,
    sha256:hash(client.stdout),recipientSha256,tables:47,denialChecks:denials,sourceRevision:source.revision,
    sourceDirty:source.dirty,capabilityExpiresAt:expiresAt,freeHeadroomChecked:true,managedServiceRecoveryVerified:false};
  writeFileSync(output,client.stdout,{flag:'wx'});writeFileSync(output+'.receipt.json',JSON.stringify(receipt,null,2)+'\n',{flag:'wx'});
  Object.values(source.files).forEach(b=>b.fill(0));Object.values(current.files).forEach(b=>b.fill(0));client.stdout.fill(0);protectedDeployments.fill(0);
  console.log(JSON.stringify(receipt));
}catch{console.error('Supervised logical export stopped at '+stage+'; no credentials, record values or provider diagnostics printed. Existing backups preserved.');process.exitCode=1;}
finally{
  if(installed){try{
    postgres(connection,'psql',['-X','-w','-qAt','-v','ON_ERROR_STOP=1'],`begin;set local transaction_read_only=off;
      do $cleanup$ begin if obj_description('public.elb_backup_snapshot()'::regprocedure,'pg_proc')<>'flightlogx-supervised-logical-v1'
        or encode(sha256(convert_to(pg_get_functiondef('public.elb_backup_snapshot()'::regprocedure),'UTF8')),'hex')<>'${definitionHash}'
        then raise exception 'Endpoint changed; cleanup refused';end if;end $cleanup$;
      drop function public.elb_backup_snapshot();notify pgrst,'reload schema';commit;`,true);
    console.log(JSON.stringify({temporaryHostedEndpointRemoved:true}));
  }catch{console.error('Temporary endpoint cleanup needs inspection; expiring token remains time-limited.');process.exitCode=1;}}
}
