import { describe, expect, it } from 'vitest';
import { buildRoleGrants, buildRoleHolders } from './roleDetail';
import type { PagePermissionRow, RoleMappingRow } from './useSecurityConfig';

const TAX = 'AL Portal - Tax Reviewer';

function rule(overrides: Partial<PagePermissionRow>): PagePermissionRow {
  return {
    id: 'perm-1',
    role: TAX,
    resource: 'page.cases',
    level: 'Manage',
    active: true,
    ...overrides,
  };
}

function mapping(overrides: Partial<RoleMappingRow>): RoleMappingRow {
  return {
    id: 'map-1',
    email: 'someone@ascotlloyd.co.uk',
    role: TAX,
    active: true,
    ...overrides,
  };
}

function levelOf(grants: ReturnType<typeof buildRoleGrants>, resource: string) {
  return grants.find((grant) => grant.resource === resource);
}

describe('buildRoleGrants', () => {
  it('lists what the code defaults grant, and nothing the role does not hold', () => {
    const grants = buildRoleGrants(TAX, []);

    expect(levelOf(grants, 'page.reviews')).toMatchObject({ level: 'Edit', source: 'Default' });
    expect(levelOf(grants, 'page.cases')).toMatchObject({ level: 'View', source: 'Default' });
    // Nothing grants the Tax Reviewer the admin screens, so they are not listed at all.
    expect(levelOf(grants, 'page.admin.security')).toBeUndefined();
    expect(grants.every((grant) => grant.level !== 'None')).toBe(true);
  });

  it('shows an administrator override in place of the default it replaces', () => {
    const grants = buildRoleGrants(TAX, [rule({ resource: 'page.cases', level: 'Manage' })]);

    expect(levelOf(grants, 'page.cases')).toMatchObject({
      level: 'Manage',
      source: 'Set by administrator',
      overrideId: 'perm-1',
    });
    // The defaults it did not touch are untouched.
    expect(levelOf(grants, 'page.reviews')).toMatchObject({ level: 'Edit', source: 'Default' });
  });

  it('keeps a revoked resource visible rather than dropping it off the list', () => {
    const grants = buildRoleGrants(TAX, [rule({ resource: 'page.reviews', level: 'None' })]);

    // A rule that revokes to None is the whole reason the row matters: without it the
    // screen would say the role simply has no rule for page.reviews, when in fact an
    // administrator took the default away.
    expect(levelOf(grants, 'page.reviews')).toMatchObject({
      level: 'None',
      source: 'Set by administrator',
      overrideId: 'perm-1',
    });
  });

  it('falls back to the default when the override has been withdrawn', () => {
    const grants = buildRoleGrants(TAX, [
      rule({ resource: 'page.reviews', level: 'None', active: false }),
    ]);

    expect(levelOf(grants, 'page.reviews')).toMatchObject({ level: 'Edit', source: 'Default' });
    expect(levelOf(grants, 'page.reviews')?.overrideId).toBeNull();
  });

  it('grants a custom role only what its rules say, since no default mentions it', () => {
    const grants = buildRoleGrants('Senior Checker', [
      rule({ role: 'Senior Checker', resource: 'command.regrade', level: 'Edit' }),
    ]);

    expect(grants).toHaveLength(1);
    expect(grants[0]).toMatchObject({
      resource: 'command.regrade',
      level: 'Edit',
      source: 'Set by administrator',
    });
  });

  it('ignores rules written against a different role', () => {
    const grants = buildRoleGrants(TAX, [
      rule({ id: 'perm-2', role: 'AL Portal - Planner', resource: 'page.cases', level: 'Manage' }),
    ]);

    expect(levelOf(grants, 'page.cases')).toMatchObject({ level: 'View', source: 'Default' });
  });

  it('ignores a row whose resource or level the app does not recognise', () => {
    const grants = buildRoleGrants(TAX, [
      rule({ id: 'perm-3', resource: 'page.cases', level: 'Unrecognised (120910999)' }),
      rule({ id: 'perm-4', resource: 'page.invented', level: 'Manage' }),
    ]);

    expect(levelOf(grants, 'page.cases')).toMatchObject({ level: 'View', source: 'Default' });
    expect(levelOf(grants, 'page.invented')).toBeUndefined();
  });

  it('orders pages before capabilities, each in the resource vocabulary order', () => {
    const grants = buildRoleGrants('AL Portal - Outcome Testing Manager', []);
    const resources = grants.map((grant) => grant.resource);

    expect(resources.indexOf('page.exports')).toBeLessThan(resources.indexOf('command.assign'));
    expect(resources.indexOf('page.cases')).toBeLessThan(resources.indexOf('page.exports'));
  });
});

describe('buildRoleHolders', () => {
  it('lists the people holding the role, active first', () => {
    const holders = buildRoleHolders(TAX, [
      mapping({ id: 'map-1', email: 'withdrawn@ascotlloyd.co.uk', active: false }),
      mapping({ id: 'map-2', email: 'zoe@ascotlloyd.co.uk' }),
      mapping({ id: 'map-3', email: 'adam@ascotlloyd.co.uk' }),
    ]);

    expect(holders.map((holder) => holder.email)).toEqual([
      'adam@ascotlloyd.co.uk',
      'zoe@ascotlloyd.co.uk',
      'withdrawn@ascotlloyd.co.uk',
    ]);
    expect(holders[2].active).toBe(false);
  });

  it('leaves out people holding a different role', () => {
    const holders = buildRoleHolders(TAX, [
      mapping({ id: 'map-1', role: 'AL Portal - Planner' }),
      mapping({ id: 'map-2', email: 'tax@ascotlloyd.co.uk' }),
    ]);

    expect(holders.map((holder) => holder.email)).toEqual(['tax@ascotlloyd.co.uk']);
  });

  it('matches the role code regardless of the casing the row was written with', () => {
    const holders = buildRoleHolders(TAX, [mapping({ role: 'al portal - tax reviewer' })]);

    expect(holders).toHaveLength(1);
  });

  it('says nothing rather than guessing when a row carries no email', () => {
    const holders = buildRoleHolders(TAX, [mapping({ email: '  ' })]);

    expect(holders).toHaveLength(1);
    expect(holders[0].email).toBe('');
  });
});
