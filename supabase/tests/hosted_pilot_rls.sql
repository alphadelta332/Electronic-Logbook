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
    )
on conflict (id) do nothing;

insert into public.accounts (account_id, invited_email, display_name, status)
values
    ('10000000-0000-0000-0000-000000000001', 'owner@example.invalid', 'Owner', 'active'),
    ('10000000-0000-0000-0000-000000000002', 'writer@example.invalid', 'Writer', 'active'),
    ('10000000-0000-0000-0000-000000000003', 'outsider@example.invalid', 'Outsider', 'active');

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

rollback;
