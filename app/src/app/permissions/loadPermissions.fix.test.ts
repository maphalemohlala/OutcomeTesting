import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../../services/commands/commandClient', () => ({
  executeCommand: vi.fn(),
}));
vi.mock('../../generated', () => ({
  Al_pagepermissionsService: { getAll: vi.fn() },
  ContactsService: { getAll: vi.fn() },
}));
vi.mock('../../services/auth/useCurrentUser', () => ({ useCurrentUser: vi.fn() }));

import { loadPermissions } from './loadPermissions';
import { executeCommand } from '../../services/commands/commandClient';
import { Al_pagepermissionsService, ContactsService } from '../../generated';
import { RESOURCE_KEYS, can } from '../../types/permissions';

const asMock = (fn: unknown) => fn as ReturnType<typeof vi.fn>;
const ADVISER_PICKLIST = 120910762;
const MANAGE = 120910769;
const VIEW = 120910767;

describe('loadPermissions, audit fixes', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    asMock(ContactsService.getAll).mockResolvedValue({ success: true, data: [{ statecode: 0 }] });
  });

  /**
   * The same case RolesAuditFixTests holds on the server. Every live rule carries a role code
   * AND al_approle's default, Adviser; al_GetMyRoles reports a picklist-only Adviser mapping
   * as "Adviser". The rule is read as its role code alone on both sides, so neither grants it.
   * A rule that names only the picklist still matches, on both sides.
   */
  it('reads a rule by its role code, not by the picklist default it also carries', async () => {
    asMock(executeCommand).mockResolvedValue({ ok: true, data: { RoleCodes: JSON.stringify(['Adviser']) } });
    asMock(Al_pagepermissionsService.getAll).mockResolvedValue({
      success: true,
      data: [
        { al_rolecode: 'Administrators', al_approle: ADVISER_PICKLIST, al_resourcekey: 'permission.manage', al_accesslevel: MANAGE },
        { al_rolecode: null, al_approle: ADVISER_PICKLIST, al_resourcekey: 'page.dashboard', al_accesslevel: VIEW },
      ],
    });

    const resolved = await loadPermissions('adviser@example.com');

    expect(can(resolved.permissions, 'permission.manage', 'View')).toBe(false);
    expect(can(resolved.permissions, 'page.dashboard', 'View')).toBe(true);
  });

  /**
   * A failed read of the person's own contact row used to count as "not deactivated", so a
   * leaver whose row could not be read got their full menu. Not knowing is not a yes.
   */
  it('grants nothing when the contact read fails', async () => {
    asMock(ContactsService.getAll).mockResolvedValue({ success: false, data: [] });
    asMock(executeCommand).mockResolvedValue({
      ok: true,
      data: { RoleCodes: JSON.stringify(['AL Portal - Portal Administrator']) },
    });
    asMock(Al_pagepermissionsService.getAll).mockResolvedValue({
      success: true,
      data: [{ al_rolecode: 'AL Portal - Portal Administrator', al_resourcekey: 'permission.manage', al_accesslevel: MANAGE }],
    });

    const resolved = await loadPermissions('admin@example.com');

    expect(resolved.accessUnknown).toBe(true);
    expect(RESOURCE_KEYS.filter((key) => can(resolved.permissions, key))).toEqual([]);
  });
});
