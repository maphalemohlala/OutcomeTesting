import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../../services/commands/commandClient', () => ({
  executeCommand: vi.fn(),
}));
vi.mock('../../generated', () => ({
  Al_pagepermissionsService: { getAll: vi.fn() },
  ContactsService: { getAll: vi.fn() },
}));
vi.mock('../../services/auth/useCurrentUser', () => ({ useCurrentUser: vi.fn() }));

import { loadPermissions } from './PermissionProvider';
import { executeCommand } from '../../services/commands/commandClient';
import { Al_pagepermissionsService, ContactsService } from '../../generated';
import { RESOURCE_KEYS, can } from '../../types/permissions';

const asMock = (fn: unknown) => fn as ReturnType<typeof vi.fn>;

/**
 * When the app cannot work out what somebody holds, it grants them nothing.
 *
 * Project owner, 2026-09-21: "A failed role should not be able to see anything at all, just a
 * no access banner." This replaces a documented fail-open, so the reasoning is worth keeping.
 *
 * `loadPermissions` used to hand back every role in the product when `al_GetMyRoles` could not
 * answer. The justification was bootstrap - so a first administrator could still configure a
 * fresh environment - and it does not survive contact with the code. A fresh environment does
 * not FAIL that call, it ANSWERS it, with an empty array, which already resolves to no access;
 * `GetMyRolesPlugin` has no administrator short-circuit either. So the permissive set never
 * served the case it was justified by. It only fired when the call ERRORED, which is exactly
 * when the app knows least about who is asking.
 *
 * What it did instead was F44, live in DEV: an account holding `Outcome Testing App User` but
 * not `Basic User` faulted the call, was handed every role, and was shown a ten-item
 * administrative menu - Case intake and Security configuration among them - both of which
 * opened with real data. Removing a privilege made the app offer MORE. Writes were refused
 * throughout, but reads on those screens are gated here and nowhere else.
 *
 * The distinction these tests pin is between three different answers that used to blur:
 * the roles could not be read (nothing, and say so), the person holds nothing (nothing,
 * quietly, because that is a real answer), and the RULEBOOK could not be read while the roles
 * are known (AD-136's stand-in, which is unchanged).
 */
describe('access that cannot be established grants nothing', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    asMock(ContactsService.getAll).mockResolvedValue({ success: true, data: [] });
  });

  /** The rules read succeeding is deliberate: it is what kept the old notice quiet. */
  const rulesReadSucceeds = () =>
    asMock(Al_pagepermissionsService.getAll).mockResolvedValue({
      success: true,
      data: [
        { al_rolecode: 'AL Portal - Tax Reviewer', al_resourcekey: 'page.cases', al_accesslevel: 120910768 },
      ],
    });

  it('grants nothing at all when the roles call fails', async () => {
    rulesReadSucceeds();
    asMock(executeCommand).mockResolvedValue({ ok: false, error: 'SecLib::CheckPrivilege failed' });

    const resolved = await loadPermissions('someone@example.com');

    expect(resolved.roles).toEqual([]);
    expect(resolved.accessUnknown).toBe(true);

    // Every resource, not a sample. The failure this replaces was that ONE unchecked screen
    // is a whole application: page.cases showed every case, page.admin.security offered role
    // creation. A guard that checks three keys and misses the fourth is the same bug again.
    const reachable = RESOURCE_KEYS.filter((key) => can(resolved.permissions, key));
    expect(reachable).toEqual([]);
  });

  it('grants nothing when the roles come back unparseable', async () => {
    rulesReadSucceeds();
    asMock(executeCommand).mockResolvedValue({ ok: true, data: { RoleCodes: 'not json' } });

    const resolved = await loadPermissions('someone@example.com');

    expect(resolved.roles).toEqual([]);
    expect(resolved.accessUnknown).toBe(true);
    expect(RESOURCE_KEYS.filter((key) => can(resolved.permissions, key))).toEqual([]);
  });

  it('does not claim the rulebook was a stand-in when the roles are the problem', async () => {
    // Two different messages for two different situations. Telling somebody the permission
    // RULES could not be read, when the real answer is that we do not know who they are,
    // sends their administrator to the wrong table.
    rulesReadSucceeds();
    asMock(executeCommand).mockResolvedValue({ ok: false, error: 'boom' });

    const resolved = await loadPermissions('someone@example.com');

    expect(resolved.rulesUnavailable).toBe(false);
  });

  it('stays silent for somebody who genuinely holds nothing', async () => {
    // An empty answer is an ANSWER. al_GetMyRoles unions the mapping table with the caller's
    // web roles server-side, so nothing back means they hold nothing - and telling a
    // correctly-restricted person that their access could not be confirmed would be a lie
    // that sends them to an administrator who will find nothing wrong.
    rulesReadSucceeds();
    asMock(executeCommand).mockResolvedValue({ ok: true, data: { RoleCodes: '[]' } });

    const resolved = await loadPermissions('someone@example.com');

    expect(resolved.roles).toEqual([]);
    expect(resolved.accessUnknown).toBe(false);
    expect(resolved.rulesUnavailable).toBe(false);
  });

  it('still resolves normally when both reads answer', async () => {
    rulesReadSucceeds();
    asMock(executeCommand).mockResolvedValue({
      ok: true,
      data: { RoleCodes: JSON.stringify(['AL Portal - Tax Reviewer']) },
    });

    const resolved = await loadPermissions('someone@example.com');

    expect(resolved.roles).toEqual(['AL Portal - Tax Reviewer']);
    expect(resolved.accessUnknown).toBe(false);
    expect(resolved.rulesUnavailable).toBe(false);
    expect(can(resolved.permissions, 'page.cases', 'Edit')).toBe(true);
  });

  it('leaves the rulebook stand-in alone when the roles are known', async () => {
    // AD-136 is a separate decision and is NOT what the project owner changed. The roles are
    // known here; only the rules could not be read, and the coded defaults still stand in for
    // them, bounded by the roles this person actually holds.
    asMock(Al_pagepermissionsService.getAll).mockResolvedValue({ success: false, data: [] });
    asMock(executeCommand).mockResolvedValue({
      ok: true,
      data: { RoleCodes: JSON.stringify(['AL Portal - Tax Reviewer']) },
    });

    const resolved = await loadPermissions('someone@example.com');

    expect(resolved.accessUnknown).toBe(false);
    expect(resolved.rulesUnavailable).toBe(true);
    expect(resolved.roles).toEqual(['AL Portal - Tax Reviewer']);
  });
});
