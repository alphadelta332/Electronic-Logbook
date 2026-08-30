-- Restore the current foundation append routine on projects whose recorded
-- foundation migration predates its payload-validation hardening.

begin;

create or replace function public.append_hosted_operation(
    p_logbook_id uuid,
    p_device_id uuid,
    p_operation_id uuid,
    p_portable_revision_id text,
    p_entry_id text,
    p_base_revision bigint,
    p_parent_revision_ids jsonb,
    p_operation_type text,
    p_operation_format_version integer,
    p_payload_ciphertext text,
    p_payload_nonce text,
    p_payload_tag text,
    p_payload_hash text,
    p_client_created_at timestamptz,
    p_redacted_routing_hints jsonb default '{}'::jsonb
)
returns public.operations
language plpgsql
security definer
set search_path = public
as $$
declare
    existing public.operations%rowtype;
    inserted public.operations%rowtype;
    next_revision bigint;
begin
    if not public.elb_has_logbook_role(p_logbook_id, 'writer') then
        raise exception 'logbook write access denied';
    end if;

    if not public.elb_device_belongs_to_current_account(p_device_id) then
        raise exception 'device access denied';
    end if;

    if not exists (
        select 1
        from public.devices d
        where d.device_id = p_device_id
          and d.account_id = auth.uid()
          and d.status = 'active'
    ) then
        raise exception 'device is not active for current account';
    end if;

    if p_operation_format_version <> 1 then
        raise exception 'unsupported operation format version';
    end if;

    if length(trim(p_portable_revision_id)) = 0
       or p_entry_id is not null and length(trim(p_entry_id)) = 0
       or length(trim(p_operation_type)) = 0 then
        raise exception 'operation identifiers are required';
    end if;

    if p_base_revision is not null and p_base_revision < 0 then
        raise exception 'base revision cannot be negative';
    end if;

    if jsonb_typeof(coalesce(p_parent_revision_ids, '[]'::jsonb)) <> 'array' then
        raise exception 'parent revision ids must be an array';
    end if;

    if length(p_payload_ciphertext) > 262144 then
        raise exception 'encrypted payload exceeds private pilot size limit';
    end if;

    if length(p_payload_ciphertext) < 16
       or length(p_payload_nonce) < 12
       or length(p_payload_tag) < 16
       or p_payload_hash !~ '^[0-9a-f]{64}$' then
        raise exception 'encrypted payload envelope is incomplete';
    end if;

    if position('"kind"' in lower(p_payload_ciphertext)) > 0
       or position('"entry"' in lower(p_payload_ciphertext)) > 0
       or position('"aircraft"' in lower(p_payload_ciphertext)) > 0
       or position('"route"' in lower(p_payload_ciphertext)) > 0
       or position('flight' in lower(p_payload_ciphertext)) > 0
       or position('remarks' in lower(p_payload_ciphertext)) > 0 then
        raise exception 'plaintext operation payloads are not allowed';
    end if;

    if jsonb_typeof(coalesce(p_redacted_routing_hints, '{}'::jsonb)) <> 'object' then
        raise exception 'routing hints must be a redacted object';
    end if;

    select *
    into existing
    from public.operations
    where logbook_id = p_logbook_id
      and operation_id = p_operation_id;

    if found then
        if existing.payload_hash = p_payload_hash
           and existing.payload_nonce = p_payload_nonce
           and existing.payload_tag = p_payload_tag
           and existing.payload_ciphertext = p_payload_ciphertext then
            return existing;
        end if;

        insert into public.security_events (
            account_id,
            logbook_id,
            device_id,
            event_type,
            severity,
            actor_account_id,
            redacted_details
        )
        values (
            auth.uid(),
            p_logbook_id,
            p_device_id,
            'operation_replay_rejected',
            'critical',
            auth.uid(),
            jsonb_build_object('operation_id', p_operation_id)
        );

        raise exception 'operation id replayed with different payload';
    end if;

    perform pg_advisory_xact_lock(hashtextextended(p_logbook_id::text, 0));

    select coalesce(max(revision), 0) + 1
    into next_revision
    from public.operations
    where logbook_id = p_logbook_id;

    insert into public.operations (
        logbook_id,
        revision,
        operation_id,
        portable_revision_id,
        entry_id,
        base_revision,
        parent_revision_ids,
        author_device_id,
        operation_type,
        operation_format_version,
        payload_ciphertext,
        payload_nonce,
        payload_tag,
        payload_hash,
        client_created_at,
        redacted_routing_hints
    )
    values (
        p_logbook_id,
        next_revision,
        p_operation_id,
        p_portable_revision_id,
        nullif(trim(p_entry_id), ''),
        p_base_revision,
        coalesce(p_parent_revision_ids, '[]'::jsonb),
        p_device_id,
        p_operation_type,
        p_operation_format_version,
        p_payload_ciphertext,
        p_payload_nonce,
        p_payload_tag,
        p_payload_hash,
        p_client_created_at,
        coalesce(p_redacted_routing_hints, '{}'::jsonb)
    )
    returning * into inserted;

    return inserted;
end;
$$;

revoke all on function public.append_hosted_operation(
    uuid, uuid, uuid, text, text, bigint, jsonb, text, integer, text, text, text, text, timestamptz, jsonb
) from public, anon, authenticated;
grant execute on function public.append_hosted_operation(
    uuid, uuid, uuid, text, text, bigint, jsonb, text, integer, text, text, text, text, timestamptz, jsonb
) to authenticated;

commit;
