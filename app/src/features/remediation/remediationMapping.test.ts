import { describe, expect, it } from 'vitest';
import type { Al_remediationactions } from '../../generated/models/Al_remediationactionsModel';
import { toAction } from './remediationMapping';

/**
 * The remedial form's per-action columns (AD-095). The three answers are not in the
 * generated model, which is regenerated from Dataverse; they are read off the record by
 * name and labelled here, and the SDK returns an unanswered one as null.
 */
function action(fields: Record<string, unknown>): Al_remediationactions {
  return {
    al_remediationactionid: 'a1a1a1a1-0000-4000-8000-000000000001',
    al_remediationactioncode: 'RA-1',
    al_name: 'RA-1',
    al_description: 'Suitability report missing',
    al_actionstatus: 120910601,
    al_actionstatusname: 'In progress',
    ...fields,
  } as unknown as Al_remediationactions;
}

describe('toAction', () => {
  it('reads the remedial action text and the three answers by label', () => {
    const row = toAction(
      action({
        al_adviserresponse: 'Reissued the report',
        al_evidencereference: 'IO-77',
        al_clientcontactrequired: 120910795,
        al_recheckrequired: 120910796,
        al_changesadvice: 120910799,
        al_assignedcontactidname: 'Sims Rad',
      }),
    );
    expect(row.remedialAction).toBe('Reissued the report');
    expect(row.evidenceReference).toBe('IO-77');
    expect(row.clientContactRequired).toBe('Potentially');
    expect(row.recheckRequired).toBe('Yes');
    expect(row.changesAdvice).toBe('No');
    expect(row.assignedTo).toBe('Sims Rad');
  });

  it('treats null answers as unanswered', () => {
    const row = toAction(
      action({
        al_adviserresponse: null,
        al_clientcontactrequired: null,
        al_recheckrequired: null,
        al_changesadvice: null,
      }),
    );
    expect(row.remedialAction).toBeNull();
    expect(row.clientContactRequired).toBeNull();
    expect(row.recheckRequired).toBeNull();
    expect(row.changesAdvice).toBeNull();
  });

  it('keeps the existing columns', () => {
    const row = toAction(action({ al_duedate: '2026-09-20T00:00:00Z' }));
    expect(row.description).toBe('Suitability report missing');
    expect(row.status).toBe('In progress');
    expect(row.dueOn).toBe('20 Sept 2026');
  });
});
