begin;

create table if not exists public.configuration_revisions (
    configuration_row_id uuid primary key default gen_random_uuid(),
    logbook_id uuid not null references public.logbooks (logbook_id) on delete cascade,
    revision bigint not null,
    configuration_id uuid not null,
    portable_revision_id text not null,
    author_device_id uuid not null references public.devices (device_id),
    configuration_format_version integer not null,
    payload_ciphertext text not null,
    payload_nonce text not null,
    payload_tag text not null,
    payload_hash text not null,
    client_created_at timestamptz not null,
    received_at timestamptz not null default now(),
    constraint configuration_revisions_revision_positive check (revision > 0),
    constraint configuration_revisions_supported_format check (configuration_format_version = 1),
    constraint configuration_revisions_portable_id_not_blank check (length(trim(portable_revision_id)) > 0),
    constraint configuration_revisions_ciphertext_present check (length(payload_ciphertext) between 16 and 262144),
    constraint configuration_revisions_nonce_present check (length(payload_nonce) between 12 and 256),
    constraint configuration_revisions_tag_present check (length(payload_tag) between 16 and 512),
    constraint configuration_revisions_hash_sha256_hex check (payload_hash ~ '^[0-9a-f]{64}$'),
    constraint configuration_revisions_unique_revision unique (logbook_id, revision),
    constraint configuration_revisions_unique_id unique (logbook_id, configuration_id)
);

create index if not exists idx_configuration_revisions_logbook_revision
    on public.configuration_revisions (logbook_id, revision);
create index if not exists idx_configuration_revisions_author_device
    on public.configuration_revisions (author_device_id);

alter table public.configuration_revisions enable row level security;

create or replace function public.elb_reject_configuration_revision_mutation()
returns trigger
language plpgsql
as $$
begin
    raise exception 'configuration revisions are append-only';
end;
$$;

drop trigger if exists trg_configuration_revisions_append_only_update on public.configuration_revisions;
create trigger trg_configuration_revisions_append_only_update
    before update on public.configuration_revisions
    for each row execute function public.elb_reject_configuration_revision_mutation();

drop trigger if exists trg_configuration_revisions_append_only_delete on public.configuration_revisions;
create trigger trg_configuration_revisions_append_only_delete
    before delete on public.configuration_revisions
    for each row execute function public.elb_reject_configuration_revision_mutation();

drop policy if exists configuration_revisions_select_member on public.configuration_revisions;
create policy configuration_revisions_select_member on public.configuration_revisions
    for select
    using (public.elb_has_logbook_role(logbook_id, 'viewer'));

create or replace function public.append_hosted_configuration_revision(
    p_logbook_id uuid,
    p_device_id uuid,
    p_configuration_id uuid,
    p_portable_revision_id text,
    p_configuration_format_version integer,
    p_payload_ciphertext text,
    p_payload_nonce text,
    p_payload_tag text,
    p_payload_hash text,
    p_client_created_at timestamptz
)
returns public.configuration_revisions
language plpgsql
security definer
set search_path = public
as $$
declare
    existing public.configuration_revisions%rowtype;
    inserted public.configuration_revisions%rowtype;
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

    if p_configuration_format_version <> 1 then
        raise exception 'unsupported configuration format version';
    end if;

    if length(trim(p_portable_revision_id)) = 0 then
        raise exception 'configuration revision identifier is required';
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

    if position('customfield' in lower(p_payload_ciphertext)) > 0
       or position('currencyoverride' in lower(p_payload_ciphertext)) > 0
       or position('flightreview' in lower(p_payload_ciphertext)) > 0 then
        raise exception 'plaintext configuration payloads are not allowed';
    end if;

    select *
    into existing
    from public.configuration_revisions
    where logbook_id = p_logbook_id
      and configuration_id = p_configuration_id;

    if found then
        if existing.portable_revision_id = p_portable_revision_id
           and existing.author_device_id = p_device_id
           and existing.configuration_format_version = p_configuration_format_version
           and existing.payload_hash = p_payload_hash
           and existing.payload_nonce = p_payload_nonce
           and existing.payload_tag = p_payload_tag
           and existing.payload_ciphertext = p_payload_ciphertext
           and existing.client_created_at = p_client_created_at then
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
            'configuration_revision_replay_rejected',
            'critical',
            auth.uid(),
            jsonb_build_object('configuration_id', p_configuration_id)
        );

        raise exception 'configuration revision id replayed with different payload';
    end if;

    perform pg_advisory_xact_lock(hashtextextended(p_logbook_id::text || ':configuration', 0));

    select coalesce(max(revision), 0) + 1
    into next_revision
    from public.configuration_revisions
    where logbook_id = p_logbook_id;

    insert into public.configuration_revisions (
        logbook_id,
        revision,
        configuration_id,
        portable_revision_id,
        author_device_id,
        configuration_format_version,
        payload_ciphertext,
        payload_nonce,
        payload_tag,
        payload_hash,
        client_created_at
    )
    values (
        p_logbook_id,
        next_revision,
        p_configuration_id,
        p_portable_revision_id,
        p_device_id,
        p_configuration_format_version,
        p_payload_ciphertext,
        p_payload_nonce,
        p_payload_tag,
        p_payload_hash,
        p_client_created_at
    )
    returning * into inserted;

    return inserted;
end;
$$;

create or replace function public.read_hosted_configuration_revisions(
    p_logbook_id uuid,
    p_after_revision bigint,
    p_page_size integer default 100
)
returns table (
    revision bigint,
    configuration_id uuid,
    portable_revision_id text,
    author_device_id uuid,
    configuration_format_version integer,
    payload_ciphertext text,
    payload_nonce text,
    payload_tag text,
    payload_hash text,
    client_created_at timestamptz,
    received_at timestamptz,
    highest_revision bigint,
    has_more boolean
)
language sql
stable
security definer
set search_path = public
as $$
    with authorized as (
        select public.elb_has_logbook_role(p_logbook_id, 'viewer') as allowed
    ),
    bounds as (
        select
            greatest(p_after_revision, 0) as after_revision,
            least(greatest(p_page_size, 1), 200) as page_size
    ),
    max_revision as (
        select coalesce(max(c.revision), 0) as highest_revision
        from public.configuration_revisions c
        where c.logbook_id = p_logbook_id
    ),
    page as (
        select c.*
        from public.configuration_revisions c, authorized a, bounds b
        where a.allowed
          and c.logbook_id = p_logbook_id
          and c.revision > b.after_revision
        order by c.revision
        limit (select page_size from bounds)
    )
    select
        page.revision,
        page.configuration_id,
        page.portable_revision_id,
        page.author_device_id,
        page.configuration_format_version,
        page.payload_ciphertext,
        page.payload_nonce,
        page.payload_tag,
        page.payload_hash,
        page.client_created_at,
        page.received_at,
        max_revision.highest_revision,
        page.revision < max_revision.highest_revision as has_more
    from page
    cross join max_revision
    order by page.revision;
$$;

revoke all on public.configuration_revisions from anon, authenticated;
revoke all on function public.append_hosted_configuration_revision(
    uuid, uuid, uuid, text, integer, text, text, text, text, timestamptz
) from anon, authenticated;
revoke all on function public.read_hosted_configuration_revisions(uuid, bigint, integer)
    from anon, authenticated;

grant select on public.configuration_revisions to authenticated;
grant execute on function public.append_hosted_configuration_revision(
    uuid, uuid, uuid, text, integer, text, text, text, text, timestamptz
) to authenticated;
grant execute on function public.read_hosted_configuration_revisions(uuid, bigint, integer)
    to authenticated;

commit;
