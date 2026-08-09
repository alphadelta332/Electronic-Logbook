-- Adversarial hosted-pilot RLS checks.
-- Run against a disposable local Supabase database after migrations are applied.

begin;

create schema if not exists elb_rls_test;

create or replace function elb_rls_test.assert_true(p_name text, p_value boolean)
returns void
language plpgsql
as $$
begin
    if not coalesce(p_value, false) then
        raise exception 'RLS assertion failed: %', p_name;
    end if;
end;
$$;

create or replace function elb_rls_test.expect_error(
    p_name text,
    p_sql text,
    p_expected_message text default null
)
returns void
language plpgsql
as $$
begin
    execute p_sql;
    raise exception 'RLS assertion failed: %, expected an error', p_name;
exception
    when others then
        if sqlerrm like 'RLS assertion failed:%' then
            raise;
        end if;

        if p_expected_message is null or sqlerrm like p_expected_message then
            return;
        end if;

        raise exception 'RLS assertion failed: %, expected error like %, got %',
            p_name,
            p_expected_message,
            sqlerrm;
end;
$$;

grant usage on schema elb_rls_test to authenticated;
grant execute on all functions in schema elb_rls_test to authenticated;

insert into auth.users (
    id,
    instance_id,
    aud,
    role,
    email,
    encrypted_password,
    email_confirmed_at,
    raw_app_meta_data,
    raw_user_meta_data,
    created_at,
    updated_at
)
values
    (
        '10000000-0000-0000-0000-000000000001',
        '00000000-0000-0000-0000-000000000000',
        'authenticated',
        'authenticated',
        'owner@example.invalid',
        '',
        now(),
        '{"provider":"email","providers":["email"]}'::jsonb,
        '{}'::jsonb,
        now(),
        now()
    ),
    (
        '10000000-0000-0000-0000-000000000002',
        '00000000-0000-0000-0000-000000000000',
        'authenticated',
        'authenticated',
        'writer@example.invalid',
        '',
        now(),
        '{"provider":"email","providers":["email"]}'::jsonb,
        '{}'::jsonb,
        now(),
        now()
    ),
    (
        '10000000-0000-0000-0000-000000000003',
        '00000000-0000-0000-0000-000000000000',
        'authenticated',
        'authenticated',
        'outsider@example.invalid',
        '',
        now(),
        '{"provider":"email","providers":["email"]}'::jsonb,
        '{}'::jsonb,
        now(),
        now()
    ),
    (
        '10000000-0000-0000-0000-000000000004',
        '00000000-0000-0000-0000-000000000000',
        'authenticated',
        'authenticated',
        'disabled@example.invalid',
        '',
        now(),
        '{"provider":"email","providers":["email"]}'::jsonb,
        '{}'::jsonb,
        now(),
        now()
    ),
    (
        '10000000-0000-0000-0000-000000000005',
        '00000000-0000-0000-0000-000000000000',
        'authenticated',
        'authenticated',
        'invited@example.invalid',
        '',
        now(),
        '{"provider":"email","providers":["email"]}'::jsonb,
        '{}'::jsonb,
        now(),
        now()
    ),
    (
        '10000000-0000-0000-0000-000000000006',
        '00000000-0000-0000-0000-000000000000',
        'authenticated',
        'authenticated',
        'selfregistered@example.invalid',
        '',
        now(),
        '{"provider":"email","providers":["email"]}'::jsonb,
        '{}'::jsonb,
        now(),
        now()
    )
on conflict (id) do nothing;

insert into public.accounts (account_id, invited_email, display_name, status, disabled_at)
values
    ('10000000-0000-0000-0000-000000000001', 'owner@example.invalid', 'Owner', 'active', null),
    ('10000000-0000-0000-0000-000000000002', 'writer@example.invalid', 'Writer', 'active', null),
    ('10000000-0000-0000-0000-000000000003', 'outsider@example.invalid', 'Outsider', 'active', null),
    ('10000000-0000-0000-0000-000000000004', 'disabled@example.invalid', 'Disabled', 'disabled', now()),
    ('10000000-0000-0000-0000-000000000005', 'invited@example.invalid', 'Invited', 'invited', null);

insert into public.logbooks (logbook_id, owner_account_id, display_name)
values
    ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'Owner logbook'),
    ('20000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000003', 'Outsider logbook');

insert into public.logbook_memberships (
    membership_id,
    logbook_id,
    account_id,
    role,
    granted_by_account_id,
    accepted_at
)
values
    (
        '30000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000001',
        'owner',
        '10000000-0000-0000-0000-000000000001',
        now()
    ),
    (
        '30000000-0000-0000-0000-000000000002',
        '20000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000002',
        'writer',
        '10000000-0000-0000-0000-000000000001',
        now()
    ),
    (
        '30000000-0000-0000-0000-000000000003',
        '20000000-0000-0000-0000-000000000003',
        '10000000-0000-0000-0000-000000000003',
        'owner',
        '10000000-0000-0000-0000-000000000003',
        now()
    );

insert into public.devices (device_id, account_id, device_type, platform_label, status, revoked_at)
values
    (
        '40000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000001',
        'android',
        'Owner Android',
        'active',
        null
    ),
    (
        '40000000-0000-0000-0000-000000000002',
        '10000000-0000-0000-0000-000000000002',
        'android',
        'Writer Android',
        'active',
        null
    ),
    (
        '40000000-0000-0000-0000-000000000003',
        '10000000-0000-0000-0000-000000000003',
        'android',
        'Outsider Android',
        'active',
        null
    ),
    (
        '40000000-0000-0000-0000-000000000004',
        '10000000-0000-0000-0000-000000000002',
        'android',
        'Writer revoked Android',
        'revoked',
        now()
    );

insert into public.operations (
    logbook_id,
    revision,
    operation_id,
    portable_revision_id,
    base_revision,
    parent_revision_ids,
    author_device_id,
    operation_type,
    operation_format_version,
    payload_ciphertext,
    payload_nonce,
    payload_tag,
    payload_hash,
    client_created_at
)
values (
    '20000000-0000-0000-0000-000000000001',
    1,
    '50000000-0000-0000-0000-000000000001',
    'owner-seed-1',
    0,
    '[]'::jsonb,
    '40000000-0000-0000-0000-000000000001',
    'seed',
    1,
    repeat('a', 32),
    repeat('b', 16),
    repeat('c', 32),
    repeat('0', 64),
    now()
);

set local role authenticated;

select set_config('request.jwt.claim.sub', '10000000-0000-0000-0000-000000000005', true);

select public.accept_hosted_invitation(
    'Accepted Pilot',
    'android',
    'Pixel 8 Pro',
    'public-signing-key',
    'fingerprint'
);

select elb_rls_test.assert_true(
    'invited user can accept invitation and becomes active',
    (
        select status = 'active' and display_name = 'Accepted Pilot'
        from public.accounts
        where account_id = '10000000-0000-0000-0000-000000000005'
    )
);

select elb_rls_test.assert_true(
    'invitation acceptance registers an active owned device',
    (
        select count(*) = 1
        from public.devices
        where account_id = '10000000-0000-0000-0000-000000000005'
          and device_type = 'android'
          and platform_label = 'Pixel 8 Pro'
          and status = 'active'
    )
);

select set_config('request.jwt.claim.sub', '10000000-0000-0000-0000-000000000004', true);

select elb_rls_test.expect_error(
    'disabled account cannot accept invitation or register a device',
    $sql$
        select public.accept_hosted_invitation(
            'Disabled Pilot',
            'android',
            'Disabled Android',
            null,
            null
        )
    $sql$,
    '%account disabled%'
);

select set_config('request.jwt.claim.sub', '10000000-0000-0000-0000-000000000006', true);

select elb_rls_test.expect_error(
    'authenticated user without invitation cannot self-register',
    $sql$
        select public.accept_hosted_invitation(
            'Self Registered',
            'android',
            'Uninvited Android',
            null,
            null
        )
    $sql$,
    '%invitation required%'
);

select set_config('request.jwt.claim.sub', '10000000-0000-0000-0000-000000000003', true);

select elb_rls_test.assert_true(
    'outsider cannot read another account logbook',
    (select count(*) = 0 from public.logbooks where logbook_id = '20000000-0000-0000-0000-000000000001')
);

select elb_rls_test.assert_true(
    'outsider cannot read another account devices',
    (select count(*) = 0 from public.devices where account_id = '10000000-0000-0000-0000-000000000001')
);

select elb_rls_test.assert_true(
    'outsider cannot read another account operations',
    (select count(*) = 0 from public.operations where logbook_id = '20000000-0000-0000-0000-000000000001')
);

select elb_rls_test.expect_error(
    'outsider cannot grant self owner membership on guessed logbook id',
    $sql$
        insert into public.logbook_memberships (
            logbook_id,
            account_id,
            role,
            granted_by_account_id,
            accepted_at
        )
        values (
            '20000000-0000-0000-0000-000000000001',
            '10000000-0000-0000-0000-000000000003',
            'owner',
            '10000000-0000-0000-0000-000000000003',
            now()
        )
    $sql$,
    '%row-level security%'
);

select elb_rls_test.expect_error(
    'outsider cannot write another account logbook',
    $sql$
        select public.append_hosted_operation(
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000003',
            '50000000-0000-0000-0000-000000000003',
            'outsider-write-1',
            null,
            1,
            '[]'::jsonb,
            'upsert-entry',
            1,
            repeat('d', 32),
            repeat('e', 16),
            repeat('f', 32),
            repeat('1', 64),
            now(),
            '{}'::jsonb
        )
    $sql$,
    '%logbook write access denied%'
);

select set_config('request.jwt.claim.sub', '10000000-0000-0000-0000-000000000002', true);

select elb_rls_test.assert_true(
    'explicitly granted writer can read granted logbook',
    (select count(*) = 1 from public.logbooks where logbook_id = '20000000-0000-0000-0000-000000000001')
);

select elb_rls_test.expect_error(
    'writer cannot spoof another account device',
    $sql$
        select public.append_hosted_operation(
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000001',
            '50000000-0000-0000-0000-000000000004',
            'writer-spoof-1',
            null,
            1,
            '[]'::jsonb,
            'upsert-entry',
            1,
            repeat('d', 32),
            repeat('e', 16),
            repeat('f', 32),
            repeat('2', 64),
            now(),
            '{}'::jsonb
        )
    $sql$,
    '%device access denied%'
);

select elb_rls_test.expect_error(
    'writer cannot reactivate a revoked device',
    $sql$
        update public.devices
        set status = 'active',
            revoked_at = null
        where device_id = '40000000-0000-0000-0000-000000000004'
    $sql$,
    '%device status changes require administrative access%'
);

select elb_rls_test.expect_error(
    'revoked writer device cannot append operations',
    $sql$
        select public.append_hosted_operation(
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000004',
            '50000000-0000-0000-0000-000000000005',
            'writer-revoked-1',
            null,
            1,
            '[]'::jsonb,
            'upsert-entry',
            1,
            repeat('d', 32),
            repeat('e', 16),
            repeat('f', 32),
            repeat('3', 64),
            now(),
            '{}'::jsonb
        )
    $sql$,
    '%device access denied%'
);

select public.append_hosted_operation(
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000002',
    '50000000-0000-0000-0000-000000000006',
    'writer-good-1',
    null,
    1,
    '[]'::jsonb,
    'upsert-entry',
    1,
    repeat('d', 32),
    repeat('e', 16),
    repeat('f', 32),
    repeat('4', 64),
    now(),
    '{}'::jsonb
);

select elb_rls_test.assert_true(
    'idempotent retry returns existing writer revision',
    (
        select revision = 2
        from public.append_hosted_operation(
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000002',
            '50000000-0000-0000-0000-000000000006',
            'writer-good-1',
            null,
            1,
            '[]'::jsonb,
            'upsert-entry',
            1,
            repeat('d', 32),
            repeat('e', 16),
            repeat('f', 32),
            repeat('4', 64),
            now(),
            '{}'::jsonb
        )
    )
);

select elb_rls_test.expect_error(
    'operation id replay with different payload is rejected',
    $sql$
        select public.append_hosted_operation(
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000002',
            '50000000-0000-0000-0000-000000000006',
            'writer-good-1-replay',
            null,
            1,
            '[]'::jsonb,
            'upsert-entry',
            1,
            repeat('x', 32),
            repeat('e', 16),
            repeat('f', 32),
            repeat('5', 64),
            now(),
            '{}'::jsonb
        )
    $sql$,
    '%operation id replayed with different payload%'
);

select elb_rls_test.expect_error(
    'plaintext operation payload is rejected before storage',
    $sql$
        select public.append_hosted_operation(
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000002',
            '50000000-0000-0000-0000-000000000007',
            'writer-plaintext-1',
            null,
            1,
            '[]'::jsonb,
            'upsert-entry',
            1,
            '{"entry":"plaintext flight"}',
            repeat('e', 16),
            repeat('f', 32),
            repeat('5', 64),
            now(),
            '{}'::jsonb
        )
    $sql$,
    '%plaintext operation payloads are not allowed%'
);

select elb_rls_test.expect_error(
    'malformed encrypted payload envelope is rejected',
    $sql$
        select public.append_hosted_operation(
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000002',
            '50000000-0000-0000-0000-000000000008',
            'writer-malformed-1',
            null,
            1,
            '[]'::jsonb,
            'upsert-entry',
            1,
            repeat('d', 32),
            'short',
            repeat('f', 32),
            repeat('5', 64),
            now(),
            '{}'::jsonb
        )
    $sql$,
    '%encrypted payload envelope is incomplete%'
);

select elb_rls_test.assert_true(
    'missing operation pulls are revision ordered and page bounded',
    (
        select count(*) = 1
          and min(revision) = 1
          and max(revision) = 1
          and bool_or(has_more)
        from public.read_missing_operations(
            '20000000-0000-0000-0000-000000000001',
            0,
            1
        )
    )
);

select public.record_operation_ack(
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000002',
    2,
    2,
    2,
    'synced'
);

select public.record_operation_ack(
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000002',
    1,
    1,
    1,
    'rollback-attempt'
);

select elb_rls_test.assert_true(
    'ack rollback cannot reduce durable cursor',
    (
        select highest_contiguous_revision = 2
        from public.operation_acks
        where logbook_id = '20000000-0000-0000-0000-000000000001'
          and device_id = '40000000-0000-0000-0000-000000000002'
    )
);

select elb_rls_test.expect_error(
    'acknowledgement cannot move beyond hosted history',
    $sql$
        select public.record_operation_ack(
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000002',
            3,
            3,
            3,
            'impossible'
        )
    $sql$,
    '%acknowledgement revision is outside hosted history%'
);

select elb_rls_test.expect_error(
    'writer cannot acknowledge another account device',
    $sql$
        select public.record_operation_ack(
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000001',
            2,
            2,
            2,
            'spoofed'
        )
    $sql$,
    '%device access denied%'
);

select elb_rls_test.assert_true(
    'redacted diagnostics omit ciphertext payloads and secrets',
    (
        select diagnostic->>'contains_ciphertext_payloads' = 'false'
          and diagnostic->>'supabase_url' = '[redacted]'
          and diagnostic::text not like '%dddd%'
        from public.create_redacted_hosted_diagnostics(
            '20000000-0000-0000-0000-000000000001'
        ) diagnostic
    )
);

select elb_rls_test.assert_true(
    'writer cannot create owner-only logical export manifest',
    (
        select public.create_hosted_logical_export_manifest(
            '20000000-0000-0000-0000-000000000001'
        ) is null
    )
);

select set_config('request.jwt.claim.sub', '10000000-0000-0000-0000-000000000001', true);

insert into public.logbooks (logbook_id, owner_account_id, display_name)
values (
    '20000000-0000-0000-0000-000000000004',
    '10000000-0000-0000-0000-000000000001',
    'Owner bootstrap logbook'
);

insert into public.logbook_memberships (
    logbook_id,
    account_id,
    role,
    granted_by_account_id,
    accepted_at
)
values (
    '20000000-0000-0000-0000-000000000004',
    '10000000-0000-0000-0000-000000000001',
    'owner',
    '10000000-0000-0000-0000-000000000001',
    now()
);

select elb_rls_test.assert_true(
    'owner can bootstrap membership for a newly created logbook',
    (
        select count(*) = 1
        from public.logbook_memberships
        where logbook_id = '20000000-0000-0000-0000-000000000004'
          and account_id = '10000000-0000-0000-0000-000000000001'
          and role = 'owner'
    )
);

select elb_rls_test.assert_true(
    'owner can create logical export manifest for restore rehearsal',
    (
        select manifest->>'contains_ciphertext_payloads' = 'true'
          and (manifest->>'operation_count')::integer = 2
          and manifest->>'restore_target' like '%Sydney Supabase%'
        from public.create_hosted_logical_export_manifest(
            '20000000-0000-0000-0000-000000000001'
        ) manifest
    )
);

select elb_rls_test.assert_true(
    'pilot health reports counts and upgrade triggers without service secrets',
    (
        select active_account_count >= 3
          and active_device_count >= 3
          and stored_operation_count = 2
          and jsonb_typeof(paid_plan_upgrade_triggers) = 'array'
        from public.get_hosted_pilot_health()
    )
);

select elb_rls_test.expect_error(
    'authenticated clients cannot call the managed recovery RPC directly',
    $sql$
        select public.elb_bind_device_recovery_key(
            '10000000-0000-0000-0000-000000000001',
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000001',
            repeat('A', 392),
            repeat('a', 64),
            'RSA-OAEP-256'
        )
    $sql$,
    '%permission denied%'
);

select elb_rls_test.expect_error(
    'authenticated clients cannot bind recovery keys through device update',
    $sql$
        update public.devices
        set recovery_public_key = repeat('A', 392),
            recovery_key_fingerprint = repeat('a', 64),
            recovery_key_algorithm = 'RSA-OAEP-256'
        where device_id = '40000000-0000-0000-0000-000000000001'
    $sql$,
    '%device recovery-key changes require managed service access%'
);

reset role;
select set_config('request.jwt.claim.role', 'service_role', true);
select set_config('request.jwt.claim.sub', '', true);

select public.elb_bind_device_recovery_key(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000001',
    repeat('A', 392),
    repeat('a', 64),
    'RSA-OAEP-256'
);

select elb_rls_test.assert_true(
    'managed recovery service binds the device public key without private material',
    (
        select recovery_key_fingerprint = repeat('a', 64)
          and recovery_key_algorithm = 'RSA-OAEP-256'
          and recovery_public_key = repeat('A', 392)
        from public.devices
        where device_id = '40000000-0000-0000-0000-000000000001'
    )
);

select public.elb_upsert_managed_recovery_envelope(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000001',
    'AES-256-GCM',
    'pilot-kek-v1',
    repeat('B', 64),
    repeat('C', 16)
);

select public.elb_upsert_managed_recovery_envelope(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000001',
    'AES-256-GCM',
    'pilot-kek-v1',
    repeat('X', 64),
    repeat('Y', 16)
);

select elb_rls_test.assert_true(
    'managed recovery enrollment is create-once and idempotent',
    (
        select count(*) = 1
          and min(ciphertext) = repeat('B', 64)
          and min(nonce) = repeat('C', 16)
        from public.key_envelopes
        where logbook_id = '20000000-0000-0000-0000-000000000001'
          and recovery_method = 'managed-service-v1'
          and recipient_device_id is null
          and revoked_at is null
    )
);

select elb_rls_test.assert_true(
    'managed recovery enrollment emits one redacted audit event',
    (
        select count(*) = 1
          and bool_and(source_metadata = '{"channel":"managed_recovery_service"}'::jsonb)
          and bool_and(redacted_details::text not like '%BBBB%')
        from public.security_events
        where event_type = 'managed_recovery_envelope_created'
          and logbook_id = '20000000-0000-0000-0000-000000000001'
    )
);

select public.elb_upsert_recovery_code_envelope(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000001',
    'PBKDF2-SHA256-600000+A256GCM',
    'recovery-code-v1',
    repeat('R', 64),
    repeat('N', 16),
    repeat('S', 24)
);

select elb_rls_test.assert_true(
    'recovery-code enrollment stores only the client-encrypted envelope',
    (
        select count(*) = 1
          and min(ciphertext) = repeat('R', 64)
          and min(nonce) = repeat('S', 24) || '.' || repeat('N', 16)
        from public.key_envelopes
        where logbook_id = '20000000-0000-0000-0000-000000000001'
          and recovery_method = 'recovery-code-v1'
          and recipient_device_id is null
          and revoked_at is null
    )
);

select elb_rls_test.assert_true(
    'recovery setup status reports both envelopes without returning key material',
    public.elb_get_recovery_setup_status(
        '10000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    ) = '{"managed_envelope_configured":true,"recovery_code_configured":true}'::jsonb
);

select elb_rls_test.assert_true(
    'recovery-code envelope read separates salt and nonce without secret material',
    public.elb_read_recovery_code_envelope(
        '10000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '40000000-0000-0000-0000-000000000001'
    ) = jsonb_build_object(
        'ciphertext', repeat('R', 64),
        'nonce', repeat('N', 16),
        'recovery_salt', repeat('S', 24),
        'key_version_id', 'recovery-code-v1',
        'wrapping_algorithm', 'PBKDF2-SHA256-600000+A256GCM'
    )
);

select elb_rls_test.assert_true(
    'recovery-code access is redacted and audited',
    (
        select count(*) = 1
          and bool_and(source_metadata = '{"channel":"recovery_code"}'::jsonb)
          and bool_and(redacted_details = '{}'::jsonb)
        from public.security_events
        where event_type = 'recovery_code_envelope_requested'
          and logbook_id = '20000000-0000-0000-0000-000000000001'
    )
);

select public.elb_register_pending_recovery_device(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000008',
    'android',
    'Recovery Code Replacement'
);
select public.elb_bind_device_recovery_key(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000008',
    repeat('H', 392),
    repeat('b', 64),
    'RSA-OAEP-256'
);
select public.elb_read_recovery_code_envelope(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000008'
);

set local role authenticated;
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config('request.jwt.claim.sub', '10000000-0000-0000-0000-000000000001', true);
select public.record_operation_ack(
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000008',
    2,
    0,
    2,
    'synced'
);

reset role;
select set_config('request.jwt.claim.role', 'service_role', true);
select set_config('request.jwt.claim.sub', '', true);
select public.elb_activate_recovered_device(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000008'
);

select elb_rls_test.assert_true(
    'recovery-code replacement activates only after key binding, audited envelope read, and complete acknowledgement',
    (
        select status = 'active'
          and recovery_key_fingerprint = repeat('b', 64)
        from public.devices
        where device_id = '40000000-0000-0000-0000-000000000008'
    ) and (
        select count(*) = 1
        from public.security_events
        where event_type = 'replacement_device_activated'
          and device_id = '40000000-0000-0000-0000-000000000008'
    )
);

select elb_rls_test.expect_error(
    'managed recovery service rejects a writer membership',
    $sql$
        select public.elb_read_managed_recovery_envelope(
            '10000000-0000-0000-0000-000000000002',
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000002'
        )
    $sql$,
    '%managed recovery logbook access denied%'
);

select public.elb_upsert_device_recovery_envelope(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000001',
    'pilot-kek-v1',
    repeat('D', 344)
);

select public.elb_upsert_device_recovery_envelope(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000001',
    'pilot-kek-v1',
    repeat('E', 344)
);

select elb_rls_test.assert_true(
    'device recovery envelope retries retain one short-lived row',
    (
        select count(*) = 1
          and min(ciphertext) = repeat('E', 344)
          and min(expires_at) > now()
        from public.key_envelopes
        where logbook_id = '20000000-0000-0000-0000-000000000001'
          and recipient_device_id = '40000000-0000-0000-0000-000000000001'
          and revoked_at is null
    )
);

select public.elb_register_pending_recovery_device(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000006',
    'android',
    'Replacement Pixel'
);
select public.elb_register_pending_recovery_device(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000006',
    'android',
    'Replacement Pixel'
);
select public.elb_register_pending_recovery_device(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000007',
    'android',
    'Interrupted Replacement'
);
select public.elb_bind_device_recovery_key(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000006',
    repeat('F', 392),
    repeat('f', 64),
    'RSA-OAEP-256'
);
select public.elb_upsert_device_recovery_envelope(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000006',
    'pilot-kek-v1',
    repeat('G', 344)
);

select elb_rls_test.expect_error(
    'replacement device cannot activate before acknowledging complete history',
    $sql$
        select public.elb_activate_recovered_device(
            '10000000-0000-0000-0000-000000000001',
            '20000000-0000-0000-0000-000000000001',
            '40000000-0000-0000-0000-000000000006'
        )
    $sql$,
    '%has not acknowledged complete hosted history%'
);

set local role authenticated;
select set_config('request.jwt.claim.role', 'authenticated', true);
select set_config('request.jwt.claim.sub', '10000000-0000-0000-0000-000000000001', true);
select public.record_operation_ack(
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000006',
    2,
    0,
    2,
    'synced'
);

reset role;
select set_config('request.jwt.claim.role', 'service_role', true);
select set_config('request.jwt.claim.sub', '', true);
select public.elb_activate_recovered_device(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000006'
);
select public.elb_activate_recovered_device(
    '10000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000006'
);

select elb_rls_test.assert_true(
    'replacement recovery is idempotent, audited, acknowledged, and supersedes stale attempts',
    (
        select (select count(*) from public.devices where device_id = '40000000-0000-0000-0000-000000000006') = 1
          and (select status = 'active' from public.devices where device_id = '40000000-0000-0000-0000-000000000006')
          and (select status = 'superseded' from public.devices where device_id = '40000000-0000-0000-0000-000000000007')
          and (select highest_contiguous_revision = 2 from public.operation_acks
               where logbook_id = '20000000-0000-0000-0000-000000000001'
                 and device_id = '40000000-0000-0000-0000-000000000006')
          and (select count(*) = 1 from public.security_events
               where event_type = 'replacement_device_registered_pending'
                 and device_id = '40000000-0000-0000-0000-000000000006')
          and (select count(*) = 1 from public.security_events
               where event_type = 'replacement_device_activated'
                 and device_id = '40000000-0000-0000-0000-000000000006')
    )
);

rollback;
