-- Allow an active account to grant itself the initial owner membership for a
-- logbook it owns. The direct logbooks subquery in the original policy was
-- itself subject to RLS, creating a circular dependency: the owner could not
-- see the logbook until the membership it was trying to create existed.

create or replace function public.elb_logbook_owned_by_current_account(p_logbook_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
    select exists (
        select 1
        from public.logbooks l
        join public.accounts a on a.account_id = l.owner_account_id
        where l.logbook_id = p_logbook_id
          and l.owner_account_id = auth.uid()
          and a.status = 'active'
    )
$$;

revoke all on function public.elb_logbook_owned_by_current_account(uuid) from public, anon;
grant execute on function public.elb_logbook_owned_by_current_account(uuid) to authenticated;

drop policy if exists memberships_insert_owner_grant on public.logbook_memberships;
create policy memberships_insert_owner_grant on public.logbook_memberships
    for insert
    with check (
        (
            account_id = auth.uid()
            and role = 'owner'
            and public.elb_logbook_owned_by_current_account(logbook_id)
        )
        or public.elb_has_logbook_role(logbook_id, 'owner')
    );
