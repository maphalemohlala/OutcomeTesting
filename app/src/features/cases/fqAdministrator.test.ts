import { describe, expect, it, vi } from 'vitest';
import { toOutcome } from './caseOutcomeMapping';
import { effectiveAccountability, NOBODY } from './failAccountability';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';

/** The administrator who carries a File Quality fail (requirements log row 10). */

const executeCommand = vi.fn(async () => ({ ok: true, data: { Status: 'Recorded', AuditEventId: 'a' } }));
vi.mock('../../services/commands/commandClient', () => ({ executeCommand }));

describe('the File Quality administrator', () => {
  it('is read off the outcome with its name', () => {
    const outcome = toOutcome({
      al_outcomeid: 'o1',
      al_outcomecode: 'OUT-1',
      al_initialoutcome: 120910701,
      _al_fqadministratorcontactid_value: 'admin-1',
      '_al_fqadministratorcontactid_value@OData.Community.Display.V1.FormattedValue': 'Ann Admin',
    } as unknown as Al_outcomes);
    expect(outcome.fqAdministrator).toEqual({ id: 'admin-1', name: 'Ann Admin' });
  });

  it('is absent where nobody was named', () => {
    const outcome = toOutcome({ al_outcomeid: 'o1', al_outcomecode: 'OUT-1', al_initialoutcome: 120910701 } as Al_outcomes);
    expect(outcome.fqAdministrator).toBeNull();
  });

  it('stops the paraplanner being derived for the file, as the export does', () => {
    const withAdmin = effectiveAccountability(NOBODY, true, false, true);
    expect(withAdmin.source).toBe('recorded');
    expect(withAdmin.flags.fqParaplanner).toBe(false);

    const without = effectiveAccountability(NOBODY, true, false, false);
    expect(without.source).toBe('derived');
    expect(without.flags.fqParaplanner).toBe(true);
  });

  it('leaves the advice quality default alone', () => {
    expect(effectiveAccountability(NOBODY, true, true, true).flags.aqAdviser).toBe(true);
  });

  it('is sent to the command, and sent empty to clear it', async () => {
    const { setFailAccountability } = await import('../../services/commands/setFailAccountability');
    const base = {
      outcomeId: 'o',
      fqAdviser: false,
      fqParaplanner: false,
      aqAdviser: false,
      aqParaplanner: false,
      idempotencyKey: 'k',
    };

    await setFailAccountability({ ...base, fqAdministratorId: 'admin-1' });
    expect(executeCommand).toHaveBeenLastCalledWith(
      'al_SetFailAccountability',
      expect.objectContaining({ FqAdministratorId: 'admin-1' }),
    );

    await setFailAccountability(base);
    expect(executeCommand).toHaveBeenLastCalledWith(
      'al_SetFailAccountability',
      expect.objectContaining({ FqAdministratorId: '' }),
    );
  });
});
