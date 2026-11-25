import { describe, expect, it, vi } from 'vitest';
import { toOutcome } from './caseOutcomeMapping';
import { effectiveAccountability, NOBODY } from './failAccountability';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';

/** The administrator who carries a File Quality fail (requirements log row 10). */

const executeCommand = vi.fn(async () => ({ ok: true, data: { Status: 'Recorded', AuditEventId: 'a' } }));
vi.mock('../../services/commands/commandClient', () => ({ executeCommand }));

describe('the File Quality administrator', () => {
  it('is read off the outcome as a tick, with no name (2026-11-25)', () => {
    const outcome = toOutcome({
      al_outcomeid: 'o1',
      al_outcomecode: 'OUT-1',
      al_initialoutcome: 120910701,
      al_fqadministratoraccountable: true,
    } as Al_outcomes);
    expect(outcome.fqAdministrator).toBe(true);
  });

  it('reads a name recorded before the tick as ticked', () => {
    const outcome = toOutcome({
      al_outcomeid: 'o1',
      al_outcomecode: 'OUT-1',
      al_initialoutcome: 120910701,
      _al_fqadministratorcontactid_value: 'admin-1',
    } as Al_outcomes);
    expect(outcome.fqAdministrator).toBe(true);
  });

  it('is unticked where nobody was recorded', () => {
    const outcome = toOutcome({ al_outcomeid: 'o1', al_outcomecode: 'OUT-1', al_initialoutcome: 120910701 } as Al_outcomes);
    expect(outcome.fqAdministrator).toBe(false);
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

  it('is sent to the command as a tick, and sent false to clear it', async () => {
    const { setFailAccountability } = await import('../../services/commands/setFailAccountability');
    const base = {
      outcomeId: 'o',
      fqAdviser: false,
      fqParaplanner: false,
      aqAdviser: false,
      aqParaplanner: false,
      idempotencyKey: 'k',
    };

    await setFailAccountability({ ...base, fqAdministrator: true });
    expect(executeCommand).toHaveBeenLastCalledWith(
      'al_SetFailAccountability',
      expect.objectContaining({ FqAdministrator: true }),
    );

    await setFailAccountability(base);
    expect(executeCommand).toHaveBeenLastCalledWith(
      'al_SetFailAccountability',
      expect.objectContaining({ FqAdministrator: false }),
    );
    expect(executeCommand).toHaveBeenLastCalledWith(
      'al_SetFailAccountability',
      expect.not.objectContaining({ FqAdministratorId: expect.anything() }),
    );
  });
});
