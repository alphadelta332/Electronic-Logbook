import assert from 'node:assert/strict';
import { postgres } from './postgres.mjs';

export const sourceRole = 'flightlogx_backup';
const schemas = ['public', 'auth', 'storage', 'supabase_migrations'];

export function sourceRoleSql(password) {
  assert.match(password, /^[a-f0-9]{64}$/);
  // No memberships or broad pg_read_all_data grant: BYPASSRLS does not grant SELECT.
  return `begin; set local transaction_read_only=off;
    create role ${sourceRole} nologin password '${password}' bypassrls
      nosuperuser nocreatedb nocreaterole noreplication noinherit connection limit 2;
    alter role ${sourceRole} set default_transaction_read_only=on;
    alter role ${sourceRole} set statement_timeout='120s';
    grant connect on database postgres to ${sourceRole};
    ${schemas.map(schema => `grant usage on schema ${schema} to ${sourceRole};
      grant select on all tables in schema ${schema} to ${sourceRole};
      grant select on all sequences in schema ${schema} to ${sourceRole};`).join('\n')}
    commit;`;
}

export function initializeSourceAccess(owner, password) {
  postgres(owner, 'psql', ['-X','-w','-At','-v','ON_ERROR_STOP=1'], sourceRoleSql(password), true);
  // Leave a failed setup unable to log in; do not change PUBLIC or application grants.
  inspectSourceAccess(owner, true);
  postgres(owner, 'psql', ['-X','-w','-At','-v','ON_ERROR_STOP=1'],
    `begin; set local transaction_read_only=off; alter role ${sourceRole} login; commit;`, true);
}

export function inspectSourceAccess(connection, ownerSetup = false) {
  const sql = `select json_build_object(
    'role_safe', (select not rolsuper and not rolcreatedb and not rolcreaterole and not rolreplication
      and not rolinherit and rolbypassrls from pg_roles where rolname='${sourceRole}'),
    'memberships', (select count(*) from pg_auth_members where member=(select oid from pg_roles where rolname='${sourceRole}')),
    'database_create', has_database_privilege('${sourceRole}',current_database(),'CREATE'),
    'schema_create', (select count(*) from pg_namespace where has_schema_privilege('${sourceRole}',oid,'CREATE')),
    'owned_objects', (select count(*) from pg_shdepend where refclassid='pg_authid'::regclass
      and refobjid=(select oid from pg_roles where rolname='${sourceRole}') and deptype='o'),
    'write_relations', (select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace
      where n.nspname not like 'pg_%' and n.nspname<>'information_schema'
      and has_schema_privilege('${sourceRole}',n.oid,'USAGE') and
      ((c.relkind in ('r','p','v','m','f') and (has_table_privilege('${sourceRole}',c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
        or has_any_column_privilege('${sourceRole}',c.oid,'INSERT,UPDATE,REFERENCES')))
      or (c.relkind='S' and has_sequence_privilege('${sourceRole}',c.oid,'USAGE,UPDATE')))),
    'outside_reads', (select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace
      where n.nspname not like 'pg_%' and n.nspname not in ('information_schema',${schemas.map(s => `'${s}'`).join(',')})
      and has_schema_privilege('${sourceRole}',n.oid,'USAGE') and
      ((c.relkind in ('r','p','v','m','f') and (has_table_privilege('${sourceRole}',c.oid,'SELECT')
        or has_any_column_privilege('${sourceRole}',c.oid,'SELECT')))
      or (c.relkind='S' and has_sequence_privilege('${sourceRole}',c.oid,'SELECT')))),
    'executable_definers', (select count(*) from pg_proc p join pg_namespace n on n.oid=p.pronamespace
      where p.prosecdef and n.nspname not in ('pg_catalog','information_schema')
      and has_schema_privilege('${sourceRole}',n.oid,'USAGE') and has_function_privilege('${sourceRole}',p.oid,'EXECUTE')),
    'unreadable_relations', (select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace
      where n.nspname in (${schemas.map(s => `'${s}'`).join(',')}) and
      (not has_schema_privilege('${sourceRole}',n.oid,'USAGE') or
      (c.relkind in ('r','p','v','m','f') and not has_table_privilege('${sourceRole}',c.oid,'SELECT'))
      or (c.relkind='S' and not has_sequence_privilege('${sourceRole}',c.oid,'SELECT')))));`;
  const result = JSON.parse(postgres(connection, 'psql', ['-X','-w','-At','-v','ON_ERROR_STOP=1'], sql, ownerSetup));
  assert.equal(result.role_safe, true, 'Unsafe source role attributes.');
  assert.equal(result.database_create, false, 'Source role can create schemas.');
  for (const key of ['memberships','schema_create','owned_objects','write_relations','outside_reads','executable_definers','unreadable_relations'])
    assert.equal(result[key], 0, `Source permission boundary failed: ${key}.`);
  return result;
}
