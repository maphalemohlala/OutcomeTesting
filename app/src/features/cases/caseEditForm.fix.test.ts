import { describe, expect, it } from 'vitest';
import { allocationOpenFor, changedFrom, rebaseForm, statusOf } from './caseEditForm';
import type { CaseEditValues } from './caseDetailMapping';

const QUEUED = 120910583;
const ASSIGNED = 120910584;
const CLOSED = 120910591;

function values(over: Partial<CaseEditValues> = {}): CaseEditValues {
  return {
    al_clientname: 'Client A',
    al_advisername: 'Ann Adviser',
    al_adviseremail: 'ann@example.invalid',
    al_advisercode: '',
    al_adviserstatus: null,
    al_paraplanner: '',
    al_paraplanneremail: '',
    al_paraplannercode: '',
    al_products: '',
    al_casetype: null,
    al_advicedate: '',
    al_productsolutiontype: null,
    al_producttypeid: '',
    al_samplesourceid: '',
    al_casetypeid: '',
    al_preorpostcheckid: '',
    al_vulnerableclientid: '',
    al_productids: '',
    al_samplesource: null,
    al_preorpostcheck: null,
    al_vulnerableclient: null,
    al_taxcheckrequired: null,
    al_taxteamdisposition: null,
    al_casestatus: QUEUED,
    al_priority: null,
    al_duedate: '2026-11-20',
    ...over,
  };
}

const ATTRS = Object.keys(values()) as (keyof CaseEditValues)[];

describe('saving again after a conflict', () => {
  // Opened on `base`; meanwhile someone else moved the due date and the conflict re-read
  // brought `next`. This user only changed the client name.
  const base = values();
  const form = values({ al_clientname: 'Client B' });
  const next = values({ al_duedate: '2026-11-25' });

  it('keeps what this user changed and takes the other change', () => {
    const rebased = rebaseForm(form, base, next);
    expect(rebased.al_clientname).toBe('Client B');
    expect(rebased.al_duedate).toBe('2026-11-25');
  });

  it('sends only this user’s change, so the other person’s due date is not reverted', () => {
    const rebased = rebaseForm(form, base, next);
    expect(changedFrom(rebased, next, ATTRS)).toEqual({ al_clientname: 'Client B' });
  });

  it('would have reverted it without the rebase', () => {
    expect(changedFrom(form, next, ATTRS)).toHaveProperty('al_duedate', '2026-11-20');
  });

  it('sends a cleared choice as an empty string', () => {
    expect(changedFrom(values({ al_priority: null }), values({ al_priority: 1 }), ATTRS)).toEqual({
      al_priority: '',
    });
  });
});

describe('when the edit case modal offers allocation', () => {
  it('only where the lifecycle lets the case move to Assigned, as al_AssignCase does', () => {
    expect(allocationOpenFor(QUEUED)).toBe(true);
    expect(allocationOpenFor(ASSIGNED)).toBe(true);
    expect(allocationOpenFor(CLOSED)).toBe(false);
  });

  it('names the status the form holds', () => {
    expect(statusOf(CLOSED)).toBe('Closed');
    expect(statusOf(null as never)).toBeNull();
  });
});
