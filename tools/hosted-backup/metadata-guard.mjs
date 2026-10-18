// Disposable experiment only: same-source metadata equality, never DDL reconstruction.
import { postgres } from './postgres.mjs';

const catalogs = `pg_aggregate pg_am pg_amop pg_amproc pg_attrdef pg_attribute pg_auth_members
pg_authid pg_cast pg_class pg_collation pg_constraint pg_conversion pg_database
pg_db_role_setting pg_default_acl pg_depend pg_description pg_enum pg_event_trigger
pg_extension pg_foreign_data_wrapper pg_foreign_server pg_foreign_table pg_index
pg_inherits pg_init_privs pg_language pg_largeobject pg_largeobject_metadata
pg_namespace pg_opclass pg_operator pg_opfamily pg_parameter_acl pg_partitioned_table
pg_policy pg_proc pg_publication pg_publication_namespace pg_publication_rel pg_range
pg_replication_origin pg_rewrite pg_seclabel pg_sequence pg_shdepend pg_shdescription
pg_shseclabel pg_statistic pg_statistic_ext pg_statistic_ext_data pg_subscription
pg_subscription_rel pg_tablespace pg_transform pg_trigger pg_ts_config
pg_ts_config_map pg_ts_dict pg_ts_parser pg_ts_template pg_type pg_user_mapping`.split(/\s+/).sort();

// Passwords/data/statistics aren't baseline metadata. Unsupported data-bearing classes
// below must be empty; role attributes come from the password-free pg_roles view.
const omitted = new Set(['pg_authid', 'pg_largeobject', 'pg_statistic', 'pg_statistic_ext_data',
  'pg_subscription', 'pg_user_mapping']);
const unsupported = ['pg_largeobject_metadata', 'pg_foreign_data_wrapper', 'pg_foreign_server',
  'pg_foreign_table', 'pg_user_mapping', 'pg_event_trigger', 'pg_partitioned_table',
  'pg_inherits', 'pg_publication', 'pg_subscription', 'pg_replication_origin'];
const ignoredFields = {
  pg_class: ['relfilenode', 'relpages', 'reltuples', 'relallvisible', 'relfrozenxid', 'relminmxid'],
  pg_database: ['datfrozenxid', 'datminmxid'],
  pg_roles: ['rolpassword']
};
const quote = value => `'${value.replaceAll("'", "''")}'`;
const digest = expression => `encode(sha256(convert_to(${expression},'UTF8')),'hex')`;

export function metadataGuardSql() {
  const fingerprints = [...catalogs.filter(name => !omitted.has(name)), 'pg_roles'].map(name => {
    const row = `(to_jsonb(t) - ARRAY[${(ignoredFields[name] ?? []).map(quote).join(',')}]::text[])::text`;
    return `select ${quote(name)} as name, count(*) as rows,
      ${digest(`coalesce(string_agg(${row}, E'\\n' order by ${row} collate "C"),'')`)} as digest
      from pg_catalog.${name} t`;
  }).join('\nunion all\n');
  return `set search_path=pg_catalog; set timezone='UTC'; set datestyle='ISO,YMD';
    select json_build_object(
      'server',current_setting('server_version_num'),
      'database',(select oid::text from pg_database where datname=current_database()),
      'catalogs',(select json_agg(relname order by relname) from pg_class
        where relnamespace='pg_catalog'::regnamespace and relkind='r'),
      'unsupported',json_build_object(${unsupported.map(name => `${quote(name)},(select count(${name === 'pg_subscription' ? 'oid' : '*'}) from ${name === 'pg_user_mapping' ? 'pg_user_mappings' : name})`).join(',')},
        'relations',(select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace
          where n.nspname not in ('pg_catalog','information_schema','pg_toast') and
          (n.nspname not in ('public','auth','storage','supabase_migrations','backup_export') or
           c.relkind not in ('r','i','S','v','c') or c.relpersistence<>'p')),
        'sequences',(select count(*) from pg_sequence where seqincrement<=0 or seqcycle),
        'namespaces',(select count(*) from pg_namespace where nspname not in
          ('pg_catalog','pg_toast','information_schema','public','auth','storage','supabase_migrations','backup_export')),
        'extensions',(select count(*) from pg_extension where extname not in ('plpgsql','hstore')),
        'languages',(select count(*) from pg_language where lanname not in ('internal','c','sql','plpgsql')),
        'external_functions',(select count(*) from pg_proc p join pg_namespace n on n.oid=p.pronamespace
          join pg_language l on l.oid=p.prolang where n.nspname not in ('pg_catalog','information_schema')
          and l.lanname in ('c','internal') and not exists(select 1 from pg_depend d
            where d.classid='pg_proc'::regclass and d.objid=p.oid and d.deptype='e')),
        'tablespaces',(select count(*) from pg_tablespace where spcname not in ('pg_default','pg_global'))),
      'fingerprints',(select json_agg(f order by name) from (${fingerprints}) f),
      'managedRows',(select count(*) from backup_export.managed_versions),
      'managedNullRows',(select count(*) from backup_export.managed_versions where version is null),
      'managed', (select ${digest("coalesce(string_agg(to_jsonb(version)::text,E'\\n' order by version collate \"C\"),'')")}
        from backup_export.managed_versions),
      'deparsers',(select ${digest("coalesce(string_agg(to_jsonb(definition)::text,E'\\n' order by definition collate \"C\"),'')")}
        from (
          select pg_get_functiondef(p.oid) as definition from pg_proc p join pg_namespace n on n.oid=p.pronamespace
            where n.nspname in ('public','auth','storage','supabase_migrations','backup_export') and p.prokind<>'a'
          union all select pg_get_viewdef(c.oid,false) from pg_class c join pg_namespace n on n.oid=c.relnamespace
            where n.nspname in ('public','auth','storage','supabase_migrations','backup_export') and c.relkind='v'
        ) definitions));`;
}

export function inspectDisposableMetadata(connection) {
  if (connection.disposable !== true || connection.host !== '127.0.0.1' || connection.port !== 55432 ||
      !/^elb_backup_fixture_[a-f0-9]{32}$/.test(connection.database))
    throw new Error('Metadata experiment requires a marked loopback fixture on port 55432.');
  const result = JSON.parse(postgres(connection, 'psql', ['-X','-w','-qAt','-v','ON_ERROR_STOP=1'],
    `begin isolation level repeatable read read only; ${metadataGuardSql()} rollback;`).toString());
  if (!/^170\d{3}$/.test(result.server) || JSON.stringify(result.catalogs) !== JSON.stringify(catalogs) ||
      Object.values(result.unsupported).some(count => count !== 0) ||
      result.fingerprints.length !== catalogs.length - omitted.size + 1 ||
      result.managedRows < 1 || result.managedNullRows !== 0 ||
      !/^[a-f0-9]{64}$/.test(result.managed) || !/^[a-f0-9]{64}$/.test(result.deparsers) ||
      result.fingerprints.some(f => !/^[a-f0-9]{64}$/.test(f.digest)))
    throw new Error('Unsupported or incomplete disposable metadata contract.');
  return result;
}

export function requireSameMetadata(baseline, observed) {
  if (JSON.stringify(baseline) !== JSON.stringify(observed))
    throw new Error('Disposable metadata drift: a newly reviewed native baseline is required. ' +
      baseline.fingerprints.filter((f, i) => JSON.stringify(f) !== JSON.stringify(observed.fingerprints[i])).map(f => f.name).join(','));
}
