import assert from 'node:assert/strict';
const literal = value => "'" + value.replaceAll("'", "''") + "'";
const identifier = value => {
  if (!/^[a-z][a-z0-9_]*$/.test(value)) throw new Error('Invalid fixture identifier.');
  return '"' + value + '"';
};
export const logicalShapeSql = `select coalesce(jsonb_agg(jsonb_build_object('table',n.nspname||'.'||c.relname,'columns',
  (select jsonb_agg(jsonb_build_object('name',a.attname,'type',format_type(a.atttypid,a.atttypmod),
    'generated',a.attgenerated,'identity',a.attidentity) order by a.attnum) from pg_attribute a
    where a.attrelid=c.oid and a.attnum>0 and not a.attisdropped)) order by n.nspname,c.relname),'[]'::jsonb)
  from pg_class c join pg_namespace n on n.oid=c.relnamespace
  where n.nspname in ('public','auth','storage','supabase_migrations') and c.relkind in ('r','p');`;


export function logicalPayloadExpression(contract,sequences) {
  const qualified = value => {
    const parts = value.split('.');
    if (parts.length !== 2 || !['public','auth','storage','supabase_migrations'].includes(parts[0]))
      throw new Error('Unexpected fixture relation.');
    return parts.map(identifier).join('.');
  };
  const datasets = contract.map(t => `(select jsonb_build_object('table',${literal(t.table)},'rows',
    coalesce(jsonb_agg(row_to_json(r)::text order by row_to_json(r)::text collate "C"),'[]'::jsonb),
    'digest',encode(sha256(convert_to(coalesce(string_agg(to_jsonb(r)::text,E'\\n' order by to_jsonb(r)::text collate "C"),''),'UTF8')),'hex'))
    from ${qualified(t.table)} r)`).join(',');
  const states = sequences.map(name => `(select jsonb_build_object('name',${literal(name)},'last',last_value::text,
    'called',is_called,'increment',s.seqincrement::text,'cycle',s.seqcycle) from ${qualified(name)},pg_sequence s
    where s.seqrelid=${literal(name)}::regclass)`).join(',');
  return `jsonb_build_object('version',1,'shape',(${logicalShapeSql.replace(/;\s*$/, '')}),
    'datasets',jsonb_build_array(${datasets}),'sequences',jsonb_build_array(${states}))`;
}

const dailyMarker = 'flightlogx-daily-logical-v1';
const definitionHashSql = "encode(sha256(convert_to(pg_get_functiondef('public.elb_backup_snapshot()'::regprocedure),'UTF8')),'hex')";

function lifecycleGuard(database, expectedDefinitionHash) {
  assert.ok(database==='postgres'||/^elb_backup_fixture_[a-f0-9]{32}$/.test(database));
  if(expectedDefinitionHash!==undefined)assert.match(expectedDefinitionHash,/^[a-f0-9]{64}$/);
  return `select pg_advisory_xact_lock(hashtextextended('flightlogx-logical-capability',0));
    do $guard$ begin if current_database()<>${literal(database)} or current_user<>'postgres'
      then raise exception 'Unexpected installation context'; end if;
      ${expectedDefinitionHash===undefined ? `if to_regprocedure('public.elb_backup_snapshot()') is not null
        then raise exception 'Existing endpoint preserved'; end if;` : `if to_regprocedure('public.elb_backup_snapshot()') is null
        then raise exception 'Expected endpoint absent'; end if;
        if obj_description('public.elb_backup_snapshot()'::regprocedure,'pg_proc') is distinct from ${literal(dailyMarker)}
          or ${definitionHashSql}<>${literal(expectedDefinitionHash)}
          then raise exception 'Endpoint changed; operation refused'; end if;`}
    end $guard$;`;
}

export function revokeLogicalCapabilitySql(expectedDefinitionHash,database='postgres') {
  assert.match(expectedDefinitionHash,/^[a-f0-9]{64}$/);
  return `begin; set local transaction_read_only=off; set local lock_timeout='2s';
    ${lifecycleGuard(database,expectedDefinitionHash)}
    drop function public.elb_backup_snapshot(); notify pgrst,'reload schema'; commit;`;
}

export function logicalCapabilityRpcSql(contract,tokenHash,expiresAt,database='postgres',{daily=false,expectedDefinitionHash}={}) {
  assert.ok(database==='postgres'||/^elb_backup_fixture_[a-f0-9]{32}$/.test(database));
  if(database==='postgres')assert.equal(contract.shape.length,47);
  assert.match(tokenHash,/^[a-f0-9]{64}$/);
  assert.equal(typeof daily,'boolean');assert.ok(daily||expectedDefinitionHash===undefined);
  assert.ok(Number.isFinite(Date.parse(expiresAt))&&Date.parse(expiresAt)>Date.now()&&
    Date.parse(expiresAt)<=Date.now()+(daily?31*24*60:30)*60*1000);
  const tables=contract.shape.map(t=>{assert.match(t.table,/^(auth|public|storage|supabase_migrations)\.[a-z][a-z0-9_]*$/);return t.table;});
  return `begin; set local transaction_read_only=off; set local lock_timeout='2s';
    ${lifecycleGuard(database,expectedDefinitionHash)}
    ${daily ? `do $audit$ declare boundary jsonb; begin
      select jsonb_build_object(
        'login',(select rolcanlogin or rolsuper or rolbypassrls or rolcreaterole or rolcreatedb from pg_roles where rolname='anon'),
        'memberships',(select count(*) from pg_auth_members where member='anon'::regrole),
        'accessibleRelations',(select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace
          where n.nspname in ('public','graphql_public') and c.relkind in ('r','p','v','m','f')
          and (has_table_privilege('anon',c.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
          or exists(select 1 from pg_attribute a where a.attrelid=c.oid and a.attnum>0 and not a.attisdropped
            and has_column_privilege('anon',c.oid,a.attnum,'SELECT,INSERT,UPDATE,REFERENCES')))),
        'functions',(select coalesce(jsonb_agg(jsonb_build_object('schema',n.nspname,'name',p.proname,
          'args',pg_get_function_identity_arguments(p.oid),'definer',p.prosecdef,
          'bodyHash',encode(sha256(convert_to(p.prosrc,'UTF8')),'hex'),
          'extension',(select e.extname from pg_depend d join pg_extension e on e.oid=d.refobjid
            where d.classid='pg_proc'::regclass and d.objid=p.oid and d.deptype='e'))
          order by n.nspname,p.proname,pg_get_function_identity_arguments(p.oid)),'[]'::jsonb)
          from pg_proc p join pg_namespace n on n.oid=p.pronamespace
          where n.nspname in ('public','graphql_public') and has_schema_privilege('anon',n.oid,'USAGE')
          and has_function_privilege('anon',p.oid,'EXECUTE')
          and p.oid is distinct from to_regprocedure('public.elb_backup_snapshot()'))) into boundary;
      if boundary is distinct from ${literal(JSON.stringify({login:false,memberships:0,accessibleRelations:0,functions:contract.apiAnonymousFunctions}))}::jsonb
        or not exists(select 1 from pg_roles where rolname=current_user and (rolbypassrls or rolsuper))
        then raise exception 'Export API boundary changed'; end if;
    end $audit$;` : ''}
    create ${expectedDefinitionHash===undefined?'':'or replace '}function public.elb_backup_snapshot() returns jsonb language plpgsql volatile security definer
    set search_path=pg_catalog set lock_timeout='250ms' set timezone='UTC' set datestyle='ISO, YMD' as $export$
    declare result jsonb; token text; begin
      token:=coalesce(current_setting('request.headers',true),'{}')::jsonb->>'x-flightlogx-backup-token';
      if current_setting('role',true) is distinct from 'anon' or current_setting('request.method',true) is distinct from 'GET'
        or not exists(select 1 from pg_roles where rolname=current_user and (rolbypassrls or rolsuper))
        or current_setting('transaction_read_only')<>'on' or current_setting('transaction_isolation')<>'read committed'
        or clock_timestamp()>=${literal(expiresAt)}::timestamptz or token is null or token!~'^[a-f0-9]{64}$'
        or encode(sha256(convert_to(token,'UTF8')),'hex')<>${literal(tokenHash)} then raise exception 'Export capability denied'; end if;
      lock table ${tables.join(',')} in access share mode;
      select ${logicalPayloadExpression(contract.shape,['auth.refresh_tokens_id_seq'])} || jsonb_build_object(
        'context',jsonb_build_object('method',current_setting('request.method'),'readOnly',current_setting('transaction_read_only'),
          'isolation',current_setting('transaction_isolation'),'capturedAt',statement_timestamp()),
        'generated',(select jsonb_agg(jsonb_build_object('table',n.nspname||'.'||c.relname,'column',a.attname,
          'expression',pg_get_expr(d.adbin,d.adrelid)) order by n.nspname,c.relname,a.attnum)
          from pg_attribute a join pg_class c on c.oid=a.attrelid join pg_namespace n on n.oid=c.relnamespace
          join pg_attrdef d on d.adrelid=c.oid and d.adnum=a.attnum where a.attgenerated<>'' and n.nspname in ('public','auth','storage','supabase_migrations')),
        'ledgers',jsonb_build_object('auth',(select jsonb_agg(version order by version) from auth.schema_migrations),
          'storage',(select jsonb_agg(jsonb_build_object('id',id,'name',name,'hash',hash) order by id) from storage.migrations),
          'application',(select jsonb_agg(version order by version) from supabase_migrations.schema_migrations))) into result;
      if result->'shape'<>${literal(JSON.stringify(contract.shape))}::jsonb
        or result->'generated'<>${literal(JSON.stringify(contract.generatedExpressions))}::jsonb
        or result->'ledgers'<>${literal(JSON.stringify({auth:contract.ledgers.auth,storage:contract.ledgers.storage,application:contract.ledgers.application}))}::jsonb
        or octet_length(result::text)>32*1024*1024 then raise exception 'Unsupported or oversized export'; end if;
      return result;
    end $export$;
    comment on function public.elb_backup_snapshot() is ${literal(daily?dailyMarker:'flightlogx-supervised-logical-v1')};
    revoke all on function public.elb_backup_snapshot() from public,anon,authenticated,service_role;
    grant execute on function public.elb_backup_snapshot() to anon;
    notify pgrst,'reload schema'; commit;`;
}
