import { describe, expect, it } from 'vitest';
import {
  accessMeets,
  APP_ROLES,
  can,
  DEFAULT_PERMISSIONS,
  levelFor,
  pageResourceForPath,
  resolvePermissions,
  rulesInForce,
  type AppRole,
  type PermissionRule,
} from './permissions';

describe('accessMeets', () => {
  it('treats the ladder None < View < Edit < Manage', () => {
    expect(accessMeets('Manage', 'Edit')).toBe(true);
    expect(accessMeets('Edit', 'Edit')).toBe(true);
    expect(accessMeets('View', 'Edit')).toBe(false);
    expect(accessMeets('None', 'View')).toBe(false);
  });
});

describe('resolvePermissions', () => {
  it('grants nothing to a user with no roles', () => {
    const set = resolvePermissions([]);
    expect(levelFor(set, 'page.cases')).toBe('None');
    expect(can(set, 'page.admin.security', 'Manage')).toBe(false);
  });

  it('gives the Administrator Manage on the security page and permission model', () => {
    const set = resolvePermissions(['AL Portal - Portal Administrator']);
    expect(can(set, 'page.admin.security', 'Manage')).toBe(true);
    expect(can(set, 'permission.manage', 'Manage')).toBe(true);
    expect(can(set, 'page.admin.questions', 'Manage')).toBe(true);
  });

  it('lets only the T&C Manager (or escalation) regrade and sign off', () => {
    expect(can(resolvePermissions(['AL Portal - T&C Supervisor']), 'command.regrade', 'Edit')).toBe(true);
    expect(can(resolvePermissions(['AL Portal - T&C Supervisor']), 'command.signoff', 'Edit')).toBe(true);
    expect(can(resolvePermissions(['AL Portal - Adviser Remediation']), 'command.regrade', 'Edit')).toBe(false);
    expect(can(resolvePermissions(['AL Portal - AQS Reviewer']), 'command.signoff', 'Edit')).toBe(false);
  });

  it('lets the Adviser complete their own remediation but not manage exports', () => {
    const set = resolvePermissions(['AL Portal - Adviser Remediation']);
    expect(can(set, 'remediation.complete', 'Edit')).toBe(true);
    expect(can(set, 'page.exports', 'Manage')).toBe(false);
  });

  it('takes the highest level across multiple roles', () => {
    const set = resolvePermissions(['AL Portal - AQS Reviewer', 'AL Portal - T&C Supervisor']);
    // AQS Checker has View on cases, T&C Manager has Edit; the higher wins.
    expect(levelFor(set, 'page.cases')).toBe('Edit');
  });

  it('honours a custom rule set over the defaults', () => {
    const rules = [{ role: 'AL Portal - Adviser Remediation' as AppRole, resource: 'page.exports' as const, level: 'Manage' as const }];
    const set = resolvePermissions(['AL Portal - Adviser Remediation'], rules);
    expect(can(set, 'page.exports', 'Manage')).toBe(true);
  });

  it('gives every role at least a dashboard view', () => {
    for (const role of APP_ROLES) {
      expect(can(resolvePermissions([role]), 'page.dashboard', 'View')).toBe(true);
    }
  });
});

describe('pageResourceForPath', () => {
  it('maps nav and deep paths to their governing page resource', () => {
    expect(pageResourceForPath('/')).toBe('page.dashboard');
    expect(pageResourceForPath('/cases')).toBe('page.cases');
    expect(pageResourceForPath('/cases/123/remediation')).toBe('page.cases');
    expect(pageResourceForPath('/admin/security')).toBe('page.admin.security');
    expect(pageResourceForPath('/admin/questions')).toBe('page.admin.questions');
    expect(pageResourceForPath('/exports')).toBe('page.exports');
  });

  it('returns null for an ungated path', () => {
    expect(pageResourceForPath('/nowhere')).toBeNull();
  });
});

describe('DEFAULT_PERMISSIONS integrity', () => {
  it('never grants the Adviser access to the security admin page', () => {
    expect(can(resolvePermissions(['AL Portal - Adviser Remediation']), 'page.admin.security')).toBe(false);
  });

  it('only the administrative roles can manage the permission model', () => {
    // Two roles carry administration: the portal's own administrator role and the Power
    // Pages built-in that real administrators already hold. Dropping the built-in would
    // have locked out the accounts currently configuring the system.
    const admins = ['AL Portal - Portal Administrator', 'Administrators'];
    const managers = DEFAULT_PERMISSIONS.filter(
      (r) => r.resource === 'permission.manage' && accessMeets(r.level, 'Manage'),
    );
    expect(managers.every((r) => admins.includes(r.role))).toBe(true);
    expect(managers.length).toBe(admins.length);
  });

  // The remediation route is gated on page.remediation; without these the oversight roles
  // would silently lose the access they had while the route was ungated.
  it('lets the oversight roles view remediation without being able to complete it', () => {
    for (const role of ['AL Portal - Outcome Testing Manager', 'AL Portal - Portal Administrator'] as AppRole[]) {
      const set = resolvePermissions([role]);
      expect(can(set, 'page.remediation')).toBe(true);
      expect(can(set, 'remediation.complete', 'Edit')).toBe(false);
    }
  });

  it('gives the Planner the same remediation authority as Adviser Remediation', () => {
    // OD-019: the two are separate roles that share remediation routing, and the portal
    // binds both to the same Contact-scoped permission and page rule.
    const planner = resolvePermissions(['AL Portal - Planner']);
    const adviser = resolvePermissions(['AL Portal - Adviser Remediation']);

    expect(can(planner, 'page.remediation', 'Edit')).toBe(true);
    expect(can(planner, 'remediation.complete', 'Edit')).toBe(true);
    expect(levelFor(planner, 'page.remediation')).toBe(levelFor(adviser, 'page.remediation'));

    // Sharing remediation is not sharing everything: neither allocates nor administers.
    expect(can(planner, 'command.assign', 'Edit')).toBe(false);
    expect(can(planner, 'permission.manage', 'Manage')).toBe(false);
  });

  it('excludes the Power Pages system roles from the vocabulary', () => {
    expect(APP_ROLES).not.toContain('Authenticated Users');
    expect(APP_ROLES).not.toContain('Anonymous Users');
  });

  it('lets the Adviser and T&C Manager work remediation', () => {
    for (const role of ['AL Portal - Adviser Remediation', 'AL Portal - T&C Supervisor'] as AppRole[]) {
      expect(can(resolvePermissions([role]), 'page.remediation', 'Edit')).toBe(true);
    }
  });
});

describe('rulesInForce', () => {
  const stored: PermissionRule[] = [
    { role: 'AL Portal - Tax Reviewer', resource: 'page.cases', level: 'View' },
  ];

  it('resolves against the stored rules alone once any exist, as the server gate does', () => {
    // PermissionHelpers.MaxLevel reads active al_pagepermission rows and nothing else, so a
    // (role, resource) with no active rule is None there. Overlaying the coded defaults
    // made the client offer a page the server then refused.
    const rules = rulesInForce(stored);
    expect(rules).toBe(stored);
    expect(can(resolvePermissions(['AL Portal - Tax Reviewer'], rules), 'page.reviews')).toBe(false);
  });

  it('falls back to the seed matrix only when nothing is stored', () => {
    expect(rulesInForce([])).toBe(DEFAULT_PERMISSIONS);
  });

  it('means a withdrawn rule is no access, not the default', () => {
    // Withdrawing removes the row from the active set; with another rule still stored the
    // resource resolves to None on both tiers.
    const afterWithdrawal: PermissionRule[] = stored
      .filter((rule) => rule.resource !== 'page.cases')
      .concat([{ role: 'AL Portal - Tax Reviewer', resource: 'page.reviews', level: 'Edit' }]);
    const set = resolvePermissions(['AL Portal - Tax Reviewer'], rulesInForce(afterWithdrawal));
    expect(can(set, 'page.cases')).toBe(false);
    expect(can(set, 'page.reviews', 'Edit')).toBe(true);
  });
});
