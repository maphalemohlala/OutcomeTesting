import { describe, expect, it } from 'vitest';
import {
  RESOURCE_KEYS,
  DEFAULT_PERMISSIONS,
  pageResourceForPath,
  resolvePermissions,
  can,
} from '../../types/permissions';

/**
 * The adviser mapping screen's permission wiring (Fixes 5, AD-162).
 *
 * The screen itself is administration, so it is gated like the other administration pages.
 * What it configures is NOT access: the mapping decides who is told a sign-off is waiting,
 * and every T&C Manager can already read every case and perform any sign-off.
 *
 * The remediation brief asked for a test proving an unmapped T&C Manager cannot read a case.
 * That test is deliberately absent here and in the plug-in tests. It would assert the
 * opposite of the access model the owner confirmed on 2026-09-20 and Phase 1 built, so
 * writing it to satisfy the brief would have pinned a behaviour nobody wants.
 */
describe('the adviser mapping page permission', () => {
  it('is a known resource key', () => {
    expect(RESOURCE_KEYS).toContain('page.admin.advisers');
  });

  it('is the resource for its path', () => {
    expect(pageResourceForPath('/admin/advisers')).toBe('page.admin.advisers');
  });

  it('is granted to the administrators who run the configuration', () => {
    for (const role of ['AL Portal - Portal Administrator', 'Administrators']) {
      expect(can(resolvePermissions([role]), 'page.admin.advisers', 'Manage')).toBe(true);
    }
  });

  it('is granted to the Outcome Testing Manager, who runs the process', () => {
    // Not administrators alone. A stale mapping shows up as a manager who never hears about
    // a sign-off, which is the checking team's problem to notice and to fix.
    expect(
      can(resolvePermissions(['AL Portal - Outcome Testing Manager']), 'page.admin.advisers', 'Manage'),
    ).toBe(true);
  });

  it('is not granted to a checker or an adviser', () => {
    for (const role of ['AL Portal - Tax Reviewer', 'AL Portal - Adviser Remediation']) {
      expect(can(resolvePermissions([role]), 'page.admin.advisers', 'View')).toBe(false);
    }
  });

  it('does not carry the power to sign off with it', () => {
    // The guard against configuring who is TOLD quietly becoming who may ACT. The Outcome
    // Testing Manager maintains this mapping and cannot sign off a remediation, which is
    // exactly the separation the routing-only decision rests on.
    const set = resolvePermissions(['AL Portal - Outcome Testing Manager']);

    expect(can(set, 'page.admin.advisers', 'Manage')).toBe(true);
    expect(can(set, 'command.signoff', 'Edit')).toBe(false);
  });

  it('is granted by explicit rules, not inherited by accident', () => {
    const rules = DEFAULT_PERMISSIONS.filter((r) => r.resource === 'page.admin.advisers');

    expect(rules.length).toBeGreaterThan(0);
    expect(rules.every((r) => r.level === 'Manage')).toBe(true);
  });
});
