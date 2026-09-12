import { describe, expect, it } from 'vitest';
import { buildRoleGrants, buildRoleHolders, classifyHolder } from './roleDetail';
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
    expect(levelOf(grants, 'page.cases')).toMatchObject({ level: 'Edit', source: 'Default' });
    // Nothing grants the Tax Reviewer the admin screens, so they are not listed at all.
    expect(levelOf(grants, 'page.admin.security')).toBeUndefined();
    expect(grants.every((grant) => grant.level !== 'None')).toBe(true);
  });

  it('resolves from the stored rules alone once any exist, as the server gate does', () => {
    const grants = buildRoleGrants(TAX, [rule({ resource: 'page.cases', level: 'Manage' })]);

    expect(levelOf(grants, 'page.cases')).toMatchObject({
      level: 'Manage',
      source: 'Set by administrator',
      overrideId: 'perm-1',
    });
    // No default survives alongside a stored rule: PermissionHelpers.MaxLevel reads only
    // al_pagepermission, so showing the default Edit here would promise a page the server
    // refuses.
    expect(levelOf(grants, 'page.reviews')).toBeUndefined();
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

  it('falls back to the defaults only while no rule at all is stored', () => {
    // The bootstrap case: every rule withdrawn is the same as none ever stored, and the
    // seed matrix is what the first administrator resolves against.
    const grants = buildRoleGrants(TAX, [
      rule({ resource: 'page.reviews', level: 'None', active: false }),
    ]);

    expect(levelOf(grants, 'page.reviews')).toMatchObject({ level: 'Edit', source: 'Default' });
    expect(levelOf(grants, 'page.reviews')?.overrideId).toBeNull();
  });

  it('treats a withdrawn rule as no access once other rules are stored', () => {
    // This is the withdrawal the server sees: no active rule for (role, resource) is None.
    // The client used to fall back to the coded default here and offer the page anyway.
    const grants = buildRoleGrants(TAX, [
      rule({ id: 'perm-1', resource: 'page.reviews', level: 'Edit', active: false }),
      rule({ id: 'perm-2', resource: 'page.cases', level: 'View' }),
    ]);

    expect(levelOf(grants, 'page.reviews')).toBeUndefined();
    expect(levelOf(grants, 'page.cases')).toMatchObject({ level: 'View', source: 'Set by administrator' });
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

  it('does not lend another role\'s rule to this one, nor a default beside it', () => {
    const grants = buildRoleGrants(TAX, [
      rule({ id: 'perm-2', role: 'AL Portal - Planner', resource: 'page.cases', level: 'Manage' }),
    ]);

    // A rule for the Planner is a stored rule, so the environment is past bootstrap and
    // the Tax Reviewer, with no rule of its own, holds nothing — which is what the server
    // would answer.
    expect(grants).toHaveLength(0);
  });

  it('ignores a row whose resource or level the app does not recognise', () => {
    const grants = buildRoleGrants(TAX, [
      rule({ id: 'perm-3', resource: 'page.cases', level: 'Unrecognised (120910999)' }),
      rule({ id: 'perm-4', resource: 'page.invented', level: 'Manage' }),
    ]);

    expect(levelOf(grants, 'page.cases')).toMatchObject({ level: 'Edit', source: 'Default' });
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

describe('classifyHolder', () => {
  const base = {
    email: 'a@ascotlloyd.co.uk',
    name: 'A Person',
    mappingId: 'map-1' as string | null,
    mappingActive: true as boolean | null,
    associated: true,
  };

  it('reports an assignment made in the app as needing nothing', () => {
    const result = classifyHolder(base);
    expect(result.state).toBe('consistent');
    expect(result.canAdopt).toBe(false);
    expect(result.canRevoke).toBe(false);
  });

  it('reports a grant made only in Power Pages as unadopted', () => {
    const result = classifyHolder({ ...base, mappingId: null, mappingActive: null });
    expect(result.state).toBe('portal-only');
    expect(result.canAdopt).toBe(true);
    expect(result.canRevoke).toBe(true);
  });

  it('reports a withdrawn assignment that is still granted', () => {
    // The dangerous one: the app says withdrawn and the access is live.
    const result = classifyHolder({ ...base, mappingActive: false });
    expect(result.state).toBe('withdrawn-still-granted');
    expect(result.canAdopt).toBe(true);
    expect(result.canRevoke).toBe(true);
  });

  it('reports an assignment whose association was removed elsewhere', () => {
    const result = classifyHolder({ ...base, associated: false });
    expect(result.state).toBe('association-missing');
    expect(result.canAdopt).toBe(true);
    expect(result.canRevoke).toBe(true);
  });

  it('gives every state a label that says what is true, not what is wrong', () => {
    const states = [
      classifyHolder(base),
      classifyHolder({ ...base, mappingId: null, mappingActive: null }),
      classifyHolder({ ...base, mappingActive: false }),
      classifyHolder({ ...base, associated: false }),
      classifyHolder({ ...base, mappingId: null, mappingActive: null, associated: false }),
    ];
    expect(new Set(states.map((s) => s.label)).size).toBe(5);
    expect(states.every((s) => s.label.length > 0)).toBe(true);
  });

  it('treats a withdrawn assignment with no association as fully withdrawn', () => {
    const result = classifyHolder({ ...base, mappingActive: false, associated: false });
    expect(result.state).toBe('consistent');
    expect(result.canAdopt).toBe(false);
  });

  it('rejects the lying combination: mappingId null with mappingActive true', () => {
    // Though the server never emits this, the type permits it. The function must make it unreachable.
    const result = classifyHolder({ ...base, mappingId: null, mappingActive: true, associated: false });
    expect(result.state).not.toBe('association-missing');
    expect(result.label).not.toContain('Assigned in app');
  });

  it('ignores mappingActive null when a mapping exists', () => {
    const result = classifyHolder({ ...base, mappingActive: null });
    expect(result.state).toBe('consistent');
    expect(result.canAdopt).toBe(false);
    expect(result.canRevoke).toBe(false);
  });

  it('labels a person with no mapping and no association as not held', () => {
    const result = classifyHolder({ ...base, mappingId: null, mappingActive: null, associated: false });
    expect(result.label).toBe('Not held');
    expect(result.state).toBe('consistent');
  });

  it('labels a mapping with an unresolved active state as Held rather than asserting Withdrawn', () => {
    // mappingActive === null with a mapping present is a state nothing here actually
    // observed as withdrawn; 'Withdrawn' would assert a fact the data does not carry.
    const result = classifyHolder({ ...base, mappingActive: null });
    expect(result.label).toBe('Held');
    expect(result.state).toBe('consistent');
  });
});
