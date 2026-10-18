// Disposable API proof only; this does not install a hosted backup endpoint.
const literal = value => "'" + value.replaceAll("'", "''") + "'";
const identifier = value => {
  if (!/^[a-z][a-z0-9_]*$/.test(value)) throw new Error('Invalid fixture identifier.');
  return '"' + value + '"';
};
export { logicalShapeSql } from './logical-rpc.mjs';
import { logicalPayloadExpression } from './logical-rpc.mjs';

export function logicalRpcFixtureSql(database, role, contract, sequences, beforeLock = '', inSnapshot = '') {
  if (!/^elb_backup_fixture_[a-f0-9]{32}$/.test(database) || !/^elb_rpc_[a-f0-9]{32}$/.test(role) || !contract.length)
    throw new Error('Logical RPC requires a marked disposable fixture.');
  const qualified = value => {
    const parts = value.split('.');
    if (parts.length !== 2 || !['public','auth','storage','supabase_migrations'].includes(parts[0]))
      throw new Error('Unexpected fixture relation.');
    return parts.map(identifier).join('.');
  };
  return `begin; do $guard$ begin if current_database()<>${literal(database)}
    then raise exception 'Disposable database required'; end if; end $guard$;
    create or replace function public.elb_backup_snapshot_fixture() returns jsonb
    language plpgsql volatile security definer set search_path=pg_catalog set lock_timeout='250ms' as $export$
    declare result jsonb; begin
      if current_database()<>${literal(database)} or current_setting('request.method',true) is distinct from 'GET'
        or current_setting('transaction_read_only')<>'on' or current_setting('transaction_isolation')<>'read committed'
        then raise exception 'Unsupported export context'; end if;
      ${beforeLock}
      lock table ${contract.map(t => qualified(t.table)).join(',')} in access share mode;
      ${inSnapshot ? `with hold as materialized (${inSnapshot})` : ''}
      select ${logicalPayloadExpression(contract,sequences)} into result
        ${inSnapshot ? 'from hold' : ''};
      if result->'shape'<>${literal(JSON.stringify(contract))}::jsonb or octet_length(result::text)>32*1024*1024
        then raise exception 'Unsupported or oversized logical export'; end if;
      return result;
    end $export$;
    revoke all on function public.elb_backup_snapshot_fixture() from public,anon,authenticated;
    grant usage on schema public to ${identifier(role)};
    grant execute on function public.elb_backup_snapshot_fixture() to ${identifier(role)}; commit;`;
}
