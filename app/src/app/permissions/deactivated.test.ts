import { describe, expect, it } from 'vitest';
import { isDeactivated } from '../../types/permissions';

describe('whether a person has left', () => {
  it('is yes when their only contact is inactive', () => {
    expect(isDeactivated([{ statecode: 1 }])).toBe(true);
  });

  it('is no for a rehire, whichever record comes back first', () => {
    expect(isDeactivated([{ statecode: 1 }, { statecode: 0 }])).toBe(false);
    expect(isDeactivated([{ statecode: 0 }, { statecode: 1 }])).toBe(false);
  });

  it('is no when the email is not in the registry, as roles then decide', () => {
    expect(isDeactivated([])).toBe(false);
  });
});
