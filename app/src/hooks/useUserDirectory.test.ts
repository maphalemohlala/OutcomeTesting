import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../generated', () => ({ ContactsService: { getAll: vi.fn() } }));
vi.mock('../services/errors', () => ({ logTechnical: vi.fn() }));

import { ContactsService } from '../generated';
import { loadUserDirectory, resetUserDirectoryForTests } from './useUserDirectory';

const getAll = vi.mocked(ContactsService.getAll);

const page = (rows: object[]) =>
  ({ success: true, data: rows }) as unknown as Awaited<ReturnType<typeof ContactsService.getAll>>;
const failure = () =>
  ({ success: false, data: [], error: new Error('refused') }) as unknown as Awaited<
    ReturnType<typeof ContactsService.getAll>
  >;

const ANN = { contactid: 'c1', fullname: 'Ann Lee', emailaddress1: 'ann@example.com', statecode: 0 };

/**
 * The hook is a thin effect over loadUserDirectory, and the app has no DOM test runner, so
 * the sharing is checked on the loader every hook instance calls.
 */
describe('the shared user directory read', () => {
  beforeEach(() => {
    resetUserDirectoryForTests();
    getAll.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('costs one read however many callers ask, in flight or done', async () => {
    getAll.mockResolvedValue(page([ANN]));

    const [first, second] = await Promise.all([loadUserDirectory(), loadUserDirectory()]);
    const third = await loadUserDirectory();

    expect(getAll).toHaveBeenCalledTimes(1);
    expect(first).toBe(second);
    expect(third).toBe(first);
    expect(first).toEqual({
      status: 'ready',
      users: [
        {
          id: 'c1',
          name: 'Ann Lee',
          email: 'ann@example.com',
          active: true,
          createdOn: null,
          rowVersion: null,
          staffCode: null,
        },
      ],
    });
  });

  it('asks only for the columns a directory row is built from', async () => {
    getAll.mockResolvedValue(page([]));

    await loadUserDirectory();

    expect(getAll.mock.calls[0][0]?.select).toEqual([
      'contactid', 'fullname', 'firstname', 'lastname', 'emailaddress1', 'statecode', 'createdon',
      'versionnumber', 'al_staffcode',
    ]);
  });

  it('reads again when the reload key changes', async () => {
    getAll.mockResolvedValueOnce(page([])).mockResolvedValueOnce(page([ANN]));

    const before = await loadUserDirectory(0);
    const after = await loadUserDirectory(1);

    expect(getAll).toHaveBeenCalledTimes(2);
    expect(before).toEqual({ status: 'ready', users: [] });
    expect(after.status === 'ready' && after.users.map((user) => user.id)).toEqual(['c1']);
  });

  it('reads again for a caller arriving more than five minutes later', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-16T09:00:00Z'));
    getAll.mockResolvedValueOnce(page([])).mockResolvedValueOnce(page([ANN]));

    await loadUserDirectory();
    vi.setSystemTime(new Date('2026-11-16T09:04:59Z'));
    expect(await loadUserDirectory()).toEqual({ status: 'ready', users: [] });
    expect(getAll).toHaveBeenCalledTimes(1);

    vi.setSystemTime(new Date('2026-11-16T09:05:00Z'));
    const later = await loadUserDirectory();
    expect(getAll).toHaveBeenCalledTimes(2);
    expect(later.status === 'ready' && later.users.map((user) => user.id)).toEqual(['c1']);

    // and that read is the shared one for the next five minutes
    vi.setSystemTime(new Date('2026-11-16T09:09:59Z'));
    await loadUserDirectory();
    expect(getAll).toHaveBeenCalledTimes(2);
  });

  it('does not keep a failed read, whether it answers no or throws', async () => {
    getAll.mockResolvedValueOnce(failure()).mockRejectedValueOnce(new Error('offline'));
    getAll.mockResolvedValue(page([ANN]));

    expect((await loadUserDirectory()).status).toBe('unavailable');
    expect((await loadUserDirectory()).status).toBe('unavailable');
    expect((await loadUserDirectory()).status).toBe('ready');
    expect(getAll).toHaveBeenCalledTimes(3);
  });
});
