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

const asMock = (fn: unknown) => fn as ReturnType<typeof vi.fn>;

/**
 * When the menu is a guess, the app says so.
 *
 * F44, found in DEV on 2026-09-21 running APP-003. The permissive stand-in and the notice
 * that admits to it were wired to two DIFFERENT signals. `rulesUnavailable` tracked the
 * al_pagepermission read; the stand-in is triggered by the al_GetMyRoles read. So the one
 * failure that actually produces a guessed menu was the one failure the warning did not
 * cover.
 *
 * It is not a hypothetical pairing. An account holding `Outcome Testing App User` but not
 * `Basic User` - a mis-provisioning this project has hit three times in a week - lands
 * exactly there: that role grants al_pagepermission directly so the rules read SUCCEEDS,
 * while al_GetMyRoles goes through a plug-in whose own reads fault. Observed live: the app
 * handed back every role in the product, rendered the full administrative menu including
 * Case intake and Security configuration, opened both, and showed no notice at all. Removing
 * a privilege INCREASED what the UI offered.
 *
 * Writes were never at risk - every command refused server-side, and nothing was written -
 * which is exactly why the silence mattered: the person had no way to tell that what they
 * were looking at was not theirs.
 */
describe('a stand-in menu admits to being one', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    asMock(ContactsService.getAll).mockResolvedValue({ success: true, data: [] });
  });

  /** The rules read succeeding is the whole point: it is what kept the notice quiet. */
  const rulesReadSucceeds = () =>
    asMock(Al_pagepermissionsService.getAll).mockResolvedValue({
      success: true,
      data: [
        { al_rolecode: 'AL Portal - Tax Reviewer', al_resourcekey: 'page.cases', al_accesslevel: 120910768 },
      ],
    });

  it('says so when the caller\'s own roles could not be read', async () => {
    rulesReadSucceeds();
    asMock(executeCommand).mockResolvedValue({ ok: false, error: 'SecLib::CheckPrivilege failed' });

    const resolved = await loadPermissions('someone@example.com');

    // The stand-in itself is the documented behaviour and is not what changed here.
    expect(resolved.roles.length).toBeGreaterThan(1);
    expect(resolved.rulesUnavailable).toBe(true);
  });

  it('says so when the roles come back as something it cannot parse', async () => {
    rulesReadSucceeds();
    asMock(executeCommand).mockResolvedValue({ ok: true, data: { RoleCodes: 'not json' } });

    const resolved = await loadPermissions('someone@example.com');

    expect(resolved.rulesUnavailable).toBe(true);
  });

  it('stays quiet when both reads answered', async () => {
    // The other half of the guard. A notice on every ordinary sign-in would be worth
    // nothing by the second day, so the flag has to stay false when nothing is wrong.
    rulesReadSucceeds();
    asMock(executeCommand).mockResolvedValue({
      ok: true,
      data: { RoleCodes: JSON.stringify(['AL Portal - Tax Reviewer']) },
    });

    const resolved = await loadPermissions('someone@example.com');

    expect(resolved.roles).toEqual(['AL Portal - Tax Reviewer']);
    expect(resolved.rulesUnavailable).toBe(false);
  });

  it('stays quiet for a genuinely role-less person', async () => {
    // An empty answer is an ANSWER, not a failure: al_GetMyRoles unions the mapping table
    // with the caller's web roles server-side, so nothing back means they hold nothing.
    // Warning here would tell a correctly-restricted user their access was unconfirmed.
    rulesReadSucceeds();
    asMock(executeCommand).mockResolvedValue({ ok: true, data: { RoleCodes: '[]' } });

    const resolved = await loadPermissions('someone@example.com');

    expect(resolved.roles).toEqual([]);
    expect(resolved.rulesUnavailable).toBe(false);
  });
});
