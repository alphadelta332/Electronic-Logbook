-- Electronic Logbook hosted pilot foundation.
-- Apply to both development and private-pilot Supabase projects.

begin;

create extension if not exists citext with schema public;
create extension if not exists pgcrypto with schema public;

do $$
begin
    create type public.elb_account_status as enum ('invited', 'active', 'disabled', 'deletion_requested', 'deleted');
exception
    when duplicate_object then null;
end $$;

do $$
begin
    create type public.elb_logbook_role as enum ('owner', 'writer', 'viewer');
exception
    when duplicate_object then null;
end $$;

do $$
begin
    create type public.elb_device_type as enum ('android', 'workbook');
exception
    when duplicate_object then null;
end $$;

do $$
begin
    create type public.elb_device_status as enum ('active', 'revoked', 'disabled');
exception
    when duplicate_object then null;
end $$;

do $$
begin
    create type public.elb_security_severity as enum ('info', 'warning', 'critical');
exception
    when duplicate_object then null;
end $$;

create table if not exists public.accounts (
    account_id uuid primary key references auth.users (id) on delete cascade,
    invited_email citext not null,
    display_name text,
    status public.elb_account_status not null default 'invited',
    created_at timestamptz not null default now(),
    disabled_at timestamptz,
    deletion_requested_at timestamptz,
    deleted_at timestamptz,
    constraint accounts_invited_email_not_blank check (length(trim(invited_email::text)) > 3),
    constraint accounts_disabled_time_matches_status check (
        (status = 'disabled' and disabled_at is not null)
        or (status <> 'disabled')
    ),
    constraint accounts_deleted_time_matches_status check (
        (status = 'deleted' and deleted_at is not null)
        or (status <> 'deleted')
    )
);

create table if not exists public.logbooks (
    logbook_id uuid primary key default gen_random_uuid(),
    owner_account_id uuid not null references public.accounts (account_id),
    display_name text not null,
    current_schema_version integer not null default 2,
    operation_format_version integer not null default 1,
    retention_policy text not null default 'pilot-default',
    created_at timestamptz not null default now(),
    deletion_requested_at timestamptz,
    deleted_at timestamptz,
    constraint logbooks_display_name_not_blank check (length(trim(display_name)) > 0),
    constraint logbooks_supported_schema_version check (current_schema_version = 2),
    constraint logbooks_supported_operation_format check (operation_format_version = 1)
);

create table if not exists public.logbook_memberships (
    membership_id uuid primary key default gen_random_uuid(),
    logbook_id uuid not null references public.logbooks (logbook_id) on delete cascade,
    account_id uuid not null references public.accounts (account_id) on delete cascade,
    role public.elb_logbook_role not null,
    granted_by_account_id uuid references public.accounts (account_id),
    granted_at timestamptz not null default now(),
    accepted_at timestamptz,
    revoked_at timestamptz,
    constraint logbook_memberships_unique_account unique (logbook_id, account_id),
    constraint logbook_memberships_owner_grant_required check (
        role <> 'owner' or granted_by_account_id is null or granted_by_account_id = account_id
    )
);

create table if not exists public.devices (
    device_id uuid primary key default gen_random_uuid(),
    account_id uuid not null references public.accounts (account_id) on delete cascade,
    device_type public.elb_device_type not null,
    platform_label text not null,
    public_signing_key text,
    signing_key_fingerprint text,
    first_seen_at timestamptz not null default now(),
    last_seen_at timestamptz,
    status public.elb_device_status not null default 'active',
    revoked_at timestamptz,
    constraint devices_platform_label_not_blank check (length(trim(platform_label)) > 0),
    constraint devices_revoked_time_matches_status check (
        (status = 'revoked' and revoked_at is not null)
        or (status <> 'revoked')
    )
);

create table if not exists public.operations (
    operation_row_id uuid primary key default gen_random_uuid(),
    logbook_id uuid not null references public.logbooks (logbook_id) on delete cascade,
    revision bigint not null,
    operation_id uuid not null,
    portable_revision_id text not null,
    entry_id text,
    base_revision bigint,
    parent_revision_ids jsonb not null default '[]'::jsonb,
    author_device_id uuid not null references public.devices (device_id),
    operation_type text not null,
    operation_format_version integer not null,
    payload_ciphertext text not null,
    payload_nonce text not null,
    payload_tag text not null,
    payload_hash text not null,
    client_created_at timestamptz not null,
    received_at timestamptz not null default now(),
    redacted_routing_hints jsonb not null default '{}'::jsonb,
    constraint operations_revision_positive check (revision > 0),
    constraint operations_base_revision_nonnegative check (base_revision is null or base_revision >= 0),
    constraint operations_parent_revision_array check (jsonb_typeof(parent_revision_ids) = 'array'),
    constraint operations_routing_hints_object check (jsonb_typeof(redacted_routing_hints) = 'object'),
    constraint operations_supported_format check (operation_format_version = 1),
    constraint operations_type_not_blank check (length(trim(operation_type)) > 0),
    constraint operations_portable_revision_not_blank check (length(trim(portable_revision_id)) > 0),
    constraint operations_ciphertext_present check (length(payload_ciphertext) between 16 and 262144),
    constraint operations_nonce_present check (length(payload_nonce) between 12 and 256),
    constraint operations_tag_present check (length(payload_tag) between 16 and 512),
    constraint operations_hash_sha256_hex check (payload_hash ~ '^[0-9a-f]{64}$'),
    constraint operations_unique_revision unique (logbook_id, revision),
    constraint operations_unique_operation unique (logbook_id, operation_id)
);

create table if not exists public.operation_acks (
    logbook_id uuid not null references public.logbooks (logbook_id) on delete cascade,
    device_id uuid not null references public.devices (device_id) on delete cascade,
    highest_contiguous_revision bigint not null default 0,
    last_upload_revision bigint not null default 0,
    last_pull_revision bigint not null default 0,
    local_queue_state text not null default 'unknown',
    last_successful_sync_at timestamptz,
    updated_at timestamptz not null default now(),
    primary key (logbook_id, device_id),
    constraint operation_acks_revisions_nonnegative check (
        highest_contiguous_revision >= 0
        and last_upload_revision >= 0
        and last_pull_revision >= 0
    ),
    constraint operation_acks_queue_state_not_blank check (length(trim(local_queue_state)) > 0)
);

create table if not exists public.pairing_requests (
    pairing_request_id uuid primary key default gen_random_uuid(),
    requester_account_id uuid not null references public.accounts (account_id) on delete cascade,
    logbook_id uuid not null references public.logbooks (logbook_id) on delete cascade,
    target_type public.elb_device_type not null,
    short_code_hash text not null,
    expires_at timestamptz not null,
    consumed_at timestamptz,
    approved_device_id uuid references public.devices (device_id),
    failure_count integer not null default 0,
    created_at timestamptz not null default now(),
    constraint pairing_requests_hash_present check (length(short_code_hash) >= 32),
    constraint pairing_requests_failure_count_nonnegative check (failure_count >= 0),
    constraint pairing_requests_expiry_future check (expires_at > created_at)
);

create table if not exists public.key_envelopes (
    key_envelope_id uuid primary key default gen_random_uuid(),
    logbook_id uuid not null references public.logbooks (logbook_id) on delete cascade,
    recipient_device_id uuid references public.devices (device_id) on delete cascade,
    recovery_method text,
    wrapping_algorithm text not null,
    key_version_id text not null,
    ciphertext text not null,
    nonce text not null,
    created_at timestamptz not null default now(),
    created_by_device_id uuid references public.devices (device_id),
    expires_at timestamptz,
    revoked_at timestamptz,
    constraint key_envelopes_recipient_or_recovery check (
        recipient_device_id is not null or recovery_method is not null
    ),
    constraint key_envelopes_no_plaintext_key_material check (
        length(ciphertext) >= 16 and length(nonce) >= 12
    ),
    constraint key_envelopes_algorithm_not_blank check (length(trim(wrapping_algorithm)) > 0),
    constraint key_envelopes_key_version_not_blank check (length(trim(key_version_id)) > 0)
);

create table if not exists public.security_events (
    security_event_id uuid primary key default gen_random_uuid(),
    account_id uuid references public.accounts (account_id) on delete set null,
    logbook_id uuid references public.logbooks (logbook_id) on delete set null,
    device_id uuid references public.devices (device_id) on delete set null,
    event_type text not null,
    severity public.elb_security_severity not null default 'info',
    actor_account_id uuid references public.accounts (account_id) on delete set null,
    source_metadata jsonb not null default '{}'::jsonb,
    redacted_details jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint security_events_type_not_blank check (length(trim(event_type)) > 0),
    constraint security_events_source_object check (jsonb_typeof(source_metadata) = 'object'),
    constraint security_events_details_object check (jsonb_typeof(redacted_details) = 'object')
);

create index if not exists idx_accounts_status on public.accounts (status);
create index if not exists idx_logbooks_owner on public.logbooks (owner_account_id);
create index if not exists idx_memberships_account on public.logbook_memberships (account_id, revoked_at);
create index if not exists idx_devices_account_status on public.devices (account_id, status);
create index if not exists idx_operations_logbook_revision on public.operations (logbook_id, revision);
create index if not exists idx_operations_author_device on public.operations (author_device_id);
create index if not exists idx_operation_acks_device on public.operation_acks (device_id);
create index if not exists idx_pairing_requests_active on public.pairing_requests (logbook_id, expires_at)
    where consumed_at is null;
create index if not exists idx_key_envelopes_recipient on public.key_envelopes (recipient_device_id, revoked_at);
create index if not exists idx_security_events_account_created on public.security_events (account_id, created_at desc);

alter table public.accounts enable row level security;
alter table public.logbooks enable row level security;
alter table public.logbook_memberships enable row level security;
alter table public.devices enable row level security;
alter table public.operations enable row level security;
alter table public.operation_acks enable row level security;
alter table public.pairing_requests enable row level security;
alter table public.key_envelopes enable row level security;
alter table public.security_events enable row level security;

create or replace function public.elb_current_account_id()
returns uuid
language sql
stable
security definer
set search_path = public
as $$
    select auth.uid()
$$;

create or replace function public.elb_is_active_account(p_account_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
    select exists (
        select 1
        from public.accounts a
        where a.account_id = p_account_id
          and a.status in ('invited', 'active')
    )
$$;

create or replace function public.elb_has_logbook_role(
    p_logbook_id uuid,
    p_min_role public.elb_logbook_role default 'viewer'
)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
    select exists (
        select 1
        from public.logbook_memberships m
        join public.accounts a on a.account_id = m.account_id
        where m.logbook_id = p_logbook_id
          and m.account_id = auth.uid()
          and m.revoked_at is null
          and m.accepted_at is not null
          and a.status = 'active'
          and (
              p_min_role = 'viewer'
              or (p_min_role = 'writer' and m.role in ('owner', 'writer'))
              or (p_min_role = 'owner' and m.role = 'owner')
          )
    )
$$;

create or replace function public.elb_device_belongs_to_current_account(p_device_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
    select exists (
        select 1
        from public.devices d
        join public.accounts a on a.account_id = d.account_id
        where d.device_id = p_device_id
          and d.account_id = auth.uid()
          and d.status = 'active'
          and a.status = 'active'
    )
$$;

create or replace function public.elb_reject_operation_mutation()
returns trigger
language plpgsql
as $$
begin
    raise exception 'operations are append-only';
end;
$$;

create or replace function public.elb_reject_authenticated_device_status_change()
returns trigger
language plpgsql
as $$
begin
    if current_user = 'authenticated' and new.status is distinct from old.status then
        raise exception 'device status changes require administrative access';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_operations_append_only_update on public.operations;
create trigger trg_operations_append_only_update
    before update on public.operations
    for each row execute function public.elb_reject_operation_mutation();

drop trigger if exists trg_operations_append_only_delete on public.operations;
create trigger trg_operations_append_only_delete
    before delete on public.operations
    for each row execute function public.elb_reject_operation_mutation();

drop trigger if exists trg_devices_authenticated_status_guard on public.devices;
create trigger trg_devices_authenticated_status_guard
    before update on public.devices
    for each row execute function public.elb_reject_authenticated_device_status_change();

drop policy if exists accounts_select_self on public.accounts;
create policy accounts_select_self on public.accounts
    for select
    using (account_id = auth.uid());

drop policy if exists accounts_update_self_limited on public.accounts;
create policy accounts_update_self_limited on public.accounts
    for update
    using (account_id = auth.uid() and status = 'active')
    with check (account_id = auth.uid() and status = 'active');

drop policy if exists logbooks_select_member on public.logbooks;
create policy logbooks_select_member on public.logbooks
    for select
    using (public.elb_has_logbook_role(logbook_id, 'viewer'));

drop policy if exists logbooks_insert_owner on public.logbooks;
create policy logbooks_insert_owner on public.logbooks
    for insert
    with check (
        owner_account_id = auth.uid()
        and public.elb_is_active_account(auth.uid())
    );

drop policy if exists logbooks_update_owner on public.logbooks;
create policy logbooks_update_owner on public.logbooks
    for update
    using (public.elb_has_logbook_role(logbook_id, 'owner'))
    with check (public.elb_has_logbook_role(logbook_id, 'owner'));

drop policy if exists memberships_select_member on public.logbook_memberships;
create policy memberships_select_member on public.logbook_memberships
    for select
    using (public.elb_has_logbook_role(logbook_id, 'viewer'));

drop policy if exists memberships_insert_owner_grant on public.logbook_memberships;
create policy memberships_insert_owner_grant on public.logbook_memberships
    for insert
    with check (
        (
            account_id = auth.uid()
            and role = 'owner'
            and exists (
                select 1
                from public.logbooks l
                where l.logbook_id = logbook_memberships.logbook_id
                  and l.owner_account_id = auth.uid()
            )
        )
        or public.elb_has_logbook_role(logbook_id, 'owner')
    );

drop policy if exists memberships_update_owner on public.logbook_memberships;
create policy memberships_update_owner on public.logbook_memberships
    for update
    using (public.elb_has_logbook_role(logbook_id, 'owner'))
    with check (public.elb_has_logbook_role(logbook_id, 'owner'));

drop policy if exists devices_select_self on public.devices;
create policy devices_select_self on public.devices
    for select
    using (account_id = auth.uid());

drop policy if exists devices_insert_self on public.devices;
create policy devices_insert_self on public.devices
    for insert
    with check (
        account_id = auth.uid()
        and public.elb_is_active_account(auth.uid())
    );

drop policy if exists devices_update_self on public.devices;
create policy devices_update_self on public.devices
    for update
    using (account_id = auth.uid())
    with check (account_id = auth.uid());

drop policy if exists operations_select_member on public.operations;
create policy operations_select_member on public.operations
    for select
    using (public.elb_has_logbook_role(logbook_id, 'viewer'));

drop policy if exists operation_acks_select_member on public.operation_acks;
create policy operation_acks_select_member on public.operation_acks
    for select
    using (public.elb_has_logbook_role(logbook_id, 'viewer'));

drop policy if exists operation_acks_write_own_device on public.operation_acks;
create policy operation_acks_write_own_device on public.operation_acks
    for all
    using (
        public.elb_has_logbook_role(logbook_id, 'viewer')
        and public.elb_device_belongs_to_current_account(device_id)
    )
    with check (
        public.elb_has_logbook_role(logbook_id, 'viewer')
        and public.elb_device_belongs_to_current_account(device_id)
    );

drop policy if exists pairing_requests_select_owner on public.pairing_requests;
create policy pairing_requests_select_owner on public.pairing_requests
    for select
    using (
        requester_account_id = auth.uid()
        or public.elb_has_logbook_role(logbook_id, 'owner')
    );

drop policy if exists pairing_requests_insert_member on public.pairing_requests;
create policy pairing_requests_insert_member on public.pairing_requests
    for insert
    with check (
        requester_account_id = auth.uid()
        and public.elb_has_logbook_role(logbook_id, 'owner')
    );

drop policy if exists pairing_requests_update_owner on public.pairing_requests;
create policy pairing_requests_update_owner on public.pairing_requests
    for update
    using (public.elb_has_logbook_role(logbook_id, 'owner'))
    with check (public.elb_has_logbook_role(logbook_id, 'owner'));

drop policy if exists key_envelopes_select_recipient_or_owner on public.key_envelopes;
create policy key_envelopes_select_recipient_or_owner on public.key_envelopes
    for select
    using (
        public.elb_has_logbook_role(logbook_id, 'owner')
        or public.elb_device_belongs_to_current_account(recipient_device_id)
    );

drop policy if exists key_envelopes_insert_owner on public.key_envelopes;
create policy key_envelopes_insert_owner on public.key_envelopes
    for insert
    with check (public.elb_has_logbook_role(logbook_id, 'owner'));

drop policy if exists key_envelopes_update_owner on public.key_envelopes;
create policy key_envelopes_update_owner on public.key_envelopes
    for update
    using (public.elb_has_logbook_role(logbook_id, 'owner'))
    with check (public.elb_has_logbook_role(logbook_id, 'owner'));

drop policy if exists security_events_select_own_context on public.security_events;
create policy security_events_select_own_context on public.security_events
    for select
    using (
        account_id = auth.uid()
        or public.elb_has_logbook_role(logbook_id, 'owner')
    );

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

create or replace function public.read_missing_operations(
    p_logbook_id uuid,
    p_after_revision bigint,
    p_page_size integer default 100
)
returns table (
    revision bigint,
    operation_id uuid,
    portable_revision_id text,
    entry_id text,
    base_revision bigint,
    parent_revision_ids jsonb,
    author_device_id uuid,
    operation_type text,
    operation_format_version integer,
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
        select coalesce(max(o.revision), 0) as highest_revision
        from public.operations o
        where o.logbook_id = p_logbook_id
    ),
    page as (
        select o.*
        from public.operations o, authorized a, bounds b
        where a.allowed
          and o.logbook_id = p_logbook_id
          and o.revision > b.after_revision
        order by o.revision
        limit (select page_size from bounds)
    )
    select
        page.revision,
        page.operation_id,
        page.portable_revision_id,
        page.entry_id,
        page.base_revision,
        page.parent_revision_ids,
        page.author_device_id,
        page.operation_type,
        page.operation_format_version,
        page.payload_ciphertext,
        page.payload_nonce,
        page.payload_tag,
        page.payload_hash,
        page.client_created_at,
        page.received_at,
        max_revision.highest_revision,
        max_revision.highest_revision > coalesce((select max(revision) from page), p_after_revision) as has_more
    from page
    cross join max_revision;
$$;

create or replace function public.record_operation_ack(
    p_logbook_id uuid,
    p_device_id uuid,
    p_highest_contiguous_revision bigint,
    p_last_upload_revision bigint default 0,
    p_last_pull_revision bigint default 0,
    p_local_queue_state text default 'unknown'
)
returns public.operation_acks
language plpgsql
security definer
set search_path = public
as $$
declare
    saved public.operation_acks%rowtype;
    hosted_highest bigint;
begin
    if not public.elb_has_logbook_role(p_logbook_id, 'viewer') then
        raise exception 'logbook read access denied';
    end if;

    if not public.elb_device_belongs_to_current_account(p_device_id) then
        raise exception 'device access denied';
    end if;

    select coalesce(max(revision), 0)
    into hosted_highest
    from public.operations
    where logbook_id = p_logbook_id;

    if p_highest_contiguous_revision < 0 or p_highest_contiguous_revision > hosted_highest then
        raise exception 'acknowledgement revision is outside hosted history';
    end if;

    insert into public.operation_acks (
        logbook_id,
        device_id,
        highest_contiguous_revision,
        last_upload_revision,
        last_pull_revision,
        local_queue_state,
        last_successful_sync_at,
        updated_at
    )
    values (
        p_logbook_id,
        p_device_id,
        p_highest_contiguous_revision,
        greatest(p_last_upload_revision, 0),
        greatest(p_last_pull_revision, 0),
        p_local_queue_state,
        now(),
        now()
    )
    on conflict (logbook_id, device_id) do update
    set highest_contiguous_revision = greatest(
            public.operation_acks.highest_contiguous_revision,
            excluded.highest_contiguous_revision
        ),
        last_upload_revision = greatest(public.operation_acks.last_upload_revision, excluded.last_upload_revision),
        last_pull_revision = greatest(public.operation_acks.last_pull_revision, excluded.last_pull_revision),
        local_queue_state = excluded.local_queue_state,
        last_successful_sync_at = excluded.last_successful_sync_at,
        updated_at = excluded.updated_at
    returning * into saved;

    return saved;
end;
$$;

create or replace function public.get_hosted_pilot_health()
returns table (
    active_account_count bigint,
    active_device_count bigint,
    stored_operation_count bigint,
    estimated_database_bytes bigint,
    database_size_status text,
    paid_plan_upgrade_triggers jsonb
)
language sql
stable
security definer
set search_path = public
as $$
    with measured as (
        select
            (select count(*) from public.accounts where status = 'active') as active_account_count,
            (select count(*) from public.devices where status = 'active') as active_device_count,
            (select count(*) from public.operations) as stored_operation_count,
            (
                pg_total_relation_size('public.accounts'::regclass)
                + pg_total_relation_size('public.logbooks'::regclass)
                + pg_total_relation_size('public.logbook_memberships'::regclass)
                + pg_total_relation_size('public.devices'::regclass)
                + pg_total_relation_size('public.operations'::regclass)
                + pg_total_relation_size('public.operation_acks'::regclass)
                + pg_total_relation_size('public.security_events'::regclass)
            ) as estimated_database_bytes
    )
    select
        active_account_count,
        active_device_count,
        stored_operation_count,
        estimated_database_bytes,
        case
            when estimated_database_bytes >= 450000000 then 'upgrade_required'
            when estimated_database_bytes >= 400000000 then 'near_limit'
            else 'ok'
        end as database_size_status,
        to_jsonb(array_remove(array[
            case when estimated_database_bytes >= 450000000 then 'database storage reached private-pilot upgrade trigger' end,
            case when active_account_count >= 45 then 'active pilot accounts nearing configured free-tier ceiling' end,
            case when stored_operation_count >= 50000 then 'operation count requires restore rehearsal and paid-plan review' end
        ], null)) as paid_plan_upgrade_triggers
    from measured;
$$;

create or replace function public.create_redacted_hosted_diagnostics(
    p_logbook_id uuid default null
)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
    with health as (
        select *
        from public.get_hosted_pilot_health()
    ),
    events as (
        select coalesce(jsonb_agg(jsonb_build_object(
            'created_at', e.created_at,
            'event_type', e.event_type,
            'severity', e.severity,
            'details', e.redacted_details
        ) order by e.created_at desc), '[]'::jsonb) as rows
        from public.security_events e
        where (p_logbook_id is null or e.logbook_id = p_logbook_id)
          and (
              e.account_id = auth.uid()
              or e.logbook_id is null
              or public.elb_has_logbook_role(e.logbook_id, 'owner')
          )
        limit 100
    )
    select jsonb_build_object(
        'created_at', now(),
        'supabase_url', '[redacted]',
        'anon_key', '[redacted]',
        'account_id', case when auth.uid() is null then '[anonymous]' else '[redacted]' end,
        'logbook_id', case when p_logbook_id is null then '[none]' else '[redacted]' end,
        'contains_ciphertext_payloads', false,
        'health', to_jsonb(health),
        'security_events', events.rows
    )
    from health, events;
$$;

create or replace function public.create_hosted_logical_export_manifest(
    p_logbook_id uuid
)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
    select jsonb_build_object(
        'logbook_id', p_logbook_id,
        'exported_at', now(),
        'contains_ciphertext_payloads', true,
        'account_count', (
            select count(*)
            from public.logbook_memberships m
            where m.logbook_id = p_logbook_id
              and m.revoked_at is null
        ),
        'device_count', (
            select count(*)
            from public.devices d
            join public.logbook_memberships m on m.account_id = d.account_id
            where m.logbook_id = p_logbook_id
              and m.revoked_at is null
        ),
        'operation_count', (
            select count(*)
            from public.operations o
            where o.logbook_id = p_logbook_id
        ),
        'highest_revision', (
            select coalesce(max(o.revision), 0)
            from public.operations o
            where o.logbook_id = p_logbook_id
        ),
        'restore_target', 'separate Sydney Supabase project or disposable local database'
    )
    where public.elb_has_logbook_role(p_logbook_id, 'owner');
$$;

create or replace function public.accept_hosted_invitation(
    p_display_name text,
    p_device_type public.elb_device_type,
    p_platform_label text,
    p_public_signing_key text default null,
    p_signing_key_fingerprint text default null
)
returns public.devices
language plpgsql
security definer
set search_path = public
as $$
declare
    account public.accounts%rowtype;
    saved_device public.devices%rowtype;
begin
    if auth.uid() is null then
        raise exception 'authentication required';
    end if;

    select *
    into account
    from public.accounts
    where account_id = auth.uid();

    if not found then
        raise exception 'invitation required';
    end if;

    if account.status = 'disabled' then
        raise exception 'account disabled';
    end if;

    if account.status not in ('invited', 'active') then
        raise exception 'account cannot accept invitation';
    end if;

    if length(trim(p_platform_label)) = 0 then
        raise exception 'platform label is required';
    end if;

    update public.accounts
    set status = 'active',
        display_name = coalesce(nullif(trim(p_display_name), ''), display_name)
    where account_id = auth.uid()
      and status = 'invited';

    insert into public.devices (
        account_id,
        device_type,
        platform_label,
        public_signing_key,
        signing_key_fingerprint,
        last_seen_at,
        status
    )
    values (
        auth.uid(),
        p_device_type,
        trim(p_platform_label),
        nullif(trim(p_public_signing_key), ''),
        nullif(trim(p_signing_key_fingerprint), ''),
        now(),
        'active'
    )
    returning * into saved_device;

    insert into public.security_events (
        account_id,
        device_id,
        event_type,
        severity,
        actor_account_id,
        redacted_details
    )
    values (
        auth.uid(),
        saved_device.device_id,
        case when account.status = 'invited' then 'invitation_accepted' else 'device_registered' end,
        'info',
        auth.uid(),
        jsonb_build_object('device_type', p_device_type, 'platform_label', trim(p_platform_label))
    );

    return saved_device;
end;
$$;

revoke all on
    public.accounts,
    public.logbooks,
    public.logbook_memberships,
    public.devices,
    public.operations,
    public.operation_acks,
    public.pairing_requests,
    public.key_envelopes,
    public.security_events
from anon, authenticated;

revoke all on function public.append_hosted_operation(
    uuid, uuid, uuid, text, text, bigint, jsonb, text, integer, text, text, text, text, timestamptz, jsonb
) from anon, authenticated;
revoke all on function public.read_missing_operations(uuid, bigint, integer) from anon, authenticated;
revoke all on function public.record_operation_ack(uuid, uuid, bigint, bigint, bigint, text) from anon, authenticated;
revoke all on function public.get_hosted_pilot_health() from anon, authenticated;
revoke all on function public.create_redacted_hosted_diagnostics(uuid) from anon, authenticated;
revoke all on function public.create_hosted_logical_export_manifest(uuid) from anon, authenticated;
revoke all on function public.accept_hosted_invitation(
    text, public.elb_device_type, text, text, text
) from anon, authenticated;

grant usage on schema public to authenticated;
grant select, insert, update on public.accounts to authenticated;
grant select, insert, update on public.logbooks to authenticated;
grant select, insert, update on public.logbook_memberships to authenticated;
grant select, insert, update on public.devices to authenticated;
grant select on public.operations to authenticated;
grant select, insert, update on public.operation_acks to authenticated;
grant select, insert, update on public.pairing_requests to authenticated;
grant select, insert, update on public.key_envelopes to authenticated;
grant select on public.security_events to authenticated;
grant execute on function public.append_hosted_operation(
    uuid, uuid, uuid, text, text, bigint, jsonb, text, integer, text, text, text, text, timestamptz, jsonb
) to authenticated;
grant execute on function public.read_missing_operations(uuid, bigint, integer) to authenticated;
grant execute on function public.record_operation_ack(uuid, uuid, bigint, bigint, bigint, text) to authenticated;
grant execute on function public.get_hosted_pilot_health() to authenticated;
grant execute on function public.create_redacted_hosted_diagnostics(uuid) to authenticated;
grant execute on function public.create_hosted_logical_export_manifest(uuid) to authenticated;
grant execute on function public.accept_hosted_invitation(
    text, public.elb_device_type, text, text, text
) to authenticated;

commit;
