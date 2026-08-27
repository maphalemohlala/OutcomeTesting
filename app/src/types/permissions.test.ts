import { describe, expect, it } from 'vitest';
import {
  accessMeets,
  can,
  DEFAULT_PERMISSIONS,
  levelFor,
  pageResourceForPath,
  resolvePermissions,
  type AppRole,
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
    const set = resolvePermissions(['Administrator']);
    expect(can(set, 'page.admin.security', 'Manage')).toBe(true);
    expect(can(set, 'permission.manage', 'Manage')).toBe(true);
    expect(can(set, 'page.admin.questions', 'Manage')).toBe(true);
  });

  it('lets only the T&C Manager (or escalation) regrade and sign off', () => {
    expect(can(resolvePermissions(['T&C Manager']), 'command.regrade', 'Edit')).toBe(true);
    expect(can(resolvePermissions(['T&C Manager']), 'command.signoff', 'Edit')).toBe(true);
    expect(can(resolvePermissions(['Adviser']), 'command.regrade', 'Edit')).toBe(false);
    expect(can(resolvePermissions(['AQS Checker']), 'command.signoff', 'Edit')).toBe(false);
  });

  it('lets the Adviser complete their own remediation but not manage exports', () => {
    const set = resolvePermissions(['Adviser']);
    expect(can(set, 'remediation.complete', 'Edit')).toBe(true);
    expect(can(set, 'page.exports', 'Manage')).toBe(false);
  });

  it('takes the highest level across multiple roles', () => {
    const set = resolvePermissions(['AQS Checker', 'T&C Manager']);
    // AQS Checker has View on cases, T&C Manager has Edit; the higher wins.
    expect(levelFor(set, 'page.cases')).toBe('Edit');
  });

  it('honours a custom rule set over the defaults', () => {
    const rules = [{ role: 'Adviser' as AppRole, resource: 'page.exports' as const, level: 'Manage' as const }];
    const set = resolvePermissions(['Adviser'], rules);
    expect(can(set, 'page.exports', 'Manage')).toBe(true);
  });

  it('gives every role at least a dashboard view', () => {
    for (const role of ['Tax Checker', 'AQS Checker', 'Adviser', 'T&C Manager', 'Outcome Testing Manager', 'Administrator'] as AppRole[]) {
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
    expect(can(resolvePermissions(['Adviser']), 'page.admin.security')).toBe(false);
  });

  it('only the Administrator can manage the permission model', () => {
    const managers = DEFAULT_PERMISSIONS.filter(
      (r) => r.resource === 'permission.manage' && accessMeets(r.level, 'Manage'),
    );
    expect(managers.every((r) => r.role === 'Administrator')).toBe(true);
    expect(managers.length).toBeGreaterThan(0);
  });
});
