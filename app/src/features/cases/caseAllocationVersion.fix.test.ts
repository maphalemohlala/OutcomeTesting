import { describe, expect, it } from 'vitest';
import { allocationVersionAfterSave } from './caseEditForm';
import { ownerIdOf } from './reviewRows';

describe('allocating a check after a field save in the same submit', () => {
  const opened = { ownerId: 'aaaa', rowVersion: '100' };

  it('uses the re-read version when nobody else took the check', () => {
    expect(allocationVersionAfterSave(opened, { ownerId: 'aaaa', rowVersion: '107' })).toEqual({ rowVersion: '107' });
  });

  it('is a conflict when someone else allocated it while the form was open', () => {
    expect(allocationVersionAfterSave(opened, { ownerId: 'bbbb', rowVersion: '107' })).toBe('conflict');
  });

  it('refuses rather than skipping the check when the re-read failed', () => {
    expect(allocationVersionAfterSave(opened, null)).toBe('unknown');
  });

  it('treats an unowned check that is still unowned as unchanged', () => {
    expect(allocationVersionAfterSave({ ownerId: null, rowVersion: '5' }, { ownerId: null, rowVersion: '6' })).toEqual({ rowVersion: '6' });
  });
});

describe('ownerIdOf', () => {
  it('reads the raw owner id the Web API returns, case-folded', () => {
    expect(ownerIdOf({ _ownerid_value: ' AB12-CD ' })).toBe('ab12-cd');
    expect(ownerIdOf({})).toBeNull();
  });
});
