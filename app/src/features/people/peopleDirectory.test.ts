import { describe, expect, it } from 'vitest';
import {
  buildDirectory,
  caseloadByIdentity,
  casesForPerson,
  checkerIdentity,
  contactEmailsOf,
  emailFromIdentity,
  isPersonRole,
  labelForIdentity,
  positions,
} from './peopleDirectory';
import type { CaseSummary } from '../cases/caseWorklistMapping';

function caseRow(overrides: Partial<CaseSummary>): CaseSummary {
  return {
    id: 'case-1',
    caseReference: 'IO-1',
    route: null,
    status: 'Assigned',
    owner: null,
    priority: null,
    createdOn: null,
    ageInDays: 0,
    latestOutcome: null,
    taxOutcome: null,
    initialOutcome: null,
    finalOutcome: null,
    finalisedOn: null,
    nextAction: '',
    client: null,
    adviser: null,
    adviserCode: null,
    adviserEmail: null,
    paraplanner: null,
    paraplannerCode: null,
    paraplannerEmail: null,
    taxChecker: null,
    aqsChecker: null,
    taxCheckerId: null,
    aqsCheckerId: null,
    caseType: null,
    productSolutionType: null,
    products: null,
    adviceDate: null,
    checkDate: null,
    preOrPostCheck: null,
    dueDate: null,
    ...overrides,
  };
}

describe('buildDirectory', () => {
  it('counts one person once per position they hold on a case', () => {
    const directory = buildDirectory([
      caseRow({ id: 'a', adviser: 'Jane Adviser', aqsChecker: 'Jane Adviser' }),
    ]);

    expect(directory.map((p) => p.role).sort()).toEqual(['Adviser', 'Checker']);
    expect(directory.every((p) => p.totalCases === 1)).toBe(true);
  });

  it('splits open from closed and records the oldest open case', () => {
    const directory = buildDirectory([
      caseRow({ id: 'a', adviser: 'Jane', status: 'Closed', ageInDays: 90 }),
      caseRow({ id: 'b', adviser: 'Jane', status: 'Assigned', ageInDays: 12 }),
      caseRow({ id: 'c', adviser: 'Jane', status: 'No Check Required', ageInDays: 40 }),
    ]);

    const jane = directory[0];
    expect(jane.totalCases).toBe(3);
    expect(jane.openCases).toBe(1);
    expect(jane.closedCases).toBe(2);
    expect(jane.oldestOpenDays).toBe(12);
  });

  it('tallies the BR-005 grades and what remains ungraded', () => {
    const directory = buildDirectory([
      caseRow({ id: 'a', adviser: 'Jane', latestOutcome: 'Pass' }),
      caseRow({ id: 'b', adviser: 'Jane', latestOutcome: 'Potential harm' }),
      caseRow({ id: 'c', adviser: 'Jane' }),
    ]);

    const jane = directory[0];
    expect(jane.outcomes.Pass).toBe(1);
    expect(jane.outcomes['Potential harm']).toBe(1);
    expect(jane.outcomes['Pass with issues']).toBe(0);
    expect(jane.notGraded).toBe(1);
  });

  it('keeps the adviser code from whichever case carries it', () => {
    const directory = buildDirectory([
      caseRow({ id: 'a', adviser: 'Jane' }),
      caseRow({ id: 'b', adviser: 'Jane', adviserCode: 'ADV-01' }),
    ]);

    expect(directory[0].code).toBe('ADV-01');
  });

  it('ignores blank and whitespace-only names rather than creating a nameless person', () => {
    expect(buildDirectory([caseRow({ adviser: '   ', paraplanner: '' })])).toEqual([]);
  });

  it('orders the busiest person first', () => {
    const directory = buildDirectory([
      caseRow({ id: 'a', adviser: 'Quiet' }),
      caseRow({ id: 'b', adviser: 'Busy' }),
      caseRow({ id: 'c', adviser: 'Busy' }),
    ]);

    expect(directory[0].name).toBe('Busy');
  });
});

describe('casesForPerson', () => {
  const cases = [
    caseRow({ id: 'a', adviser: 'Jane Adviser' }),
    caseRow({ id: 'b', aqsChecker: 'Jane Adviser' }),
    caseRow({ id: 'c', adviser: 'Someone Else' }),
  ];

  it('returns only the cases where the person holds that position', () => {
    expect(casesForPerson(cases, 'Adviser', 'name:jane adviser').map((c) => c.id)).toEqual(['a']);
    expect(casesForPerson(cases, 'Checker', 'name:jane adviser').map((c) => c.id)).toEqual(['b']);
  });

  it('matches case-insensitively so a link survives a differently cased name', () => {
    expect(casesForPerson(cases, 'Adviser', 'name:Jane Adviser')).toHaveLength(1);
  });
});

describe('casesForPerson (a bookmark or saved filter with no identity prefix)', () => {
  // Item 2, 2026-10-04 review: `/people/Adviser/<bare name>` links made before identities
  // existed carry no `email:`/`contact:`/`name:` prefix at all. That bare key must still
  // match by name, rather than silently matching nobody now the comparison is to an identity.
  it('treats a raw, unprefixed key as a name match', () => {
    const cases = [
      caseRow({ id: 'a', adviser: 'Jane Adviser', adviserEmail: null }),
      caseRow({ id: 'b', adviser: 'Someone Else', adviserEmail: null }),
    ];
    expect(casesForPerson(cases, 'Adviser', 'Jane Adviser').map((c) => c.id)).toEqual(['a']);
  });
});

describe("positions (the People page's checker grouping, item 3 2026-10-04 review)", () => {
  it('collapses two checker columns naming the same contact id to one position', () => {
    const item = caseRow({
      taxChecker: 'Carol Checker',
      taxCheckerId: 'C1',
      aqsChecker: 'Carol Checker',
      aqsCheckerId: 'c1',
    });
    const checkers = positions(item).filter((p) => p.role === 'Checker');
    expect(checkers).toHaveLength(1);
  });

  it('keeps two checkers of the same name but different contact ids apart', () => {
    const item = caseRow({
      taxChecker: 'Carol Checker',
      taxCheckerId: 'C1',
      aqsChecker: 'Carol Checker',
      aqsCheckerId: 'C2',
    });
    const checkers = positions(item).filter((p) => p.role === 'Checker');
    expect(checkers).toHaveLength(2);
  });
});

describe('labelForIdentity (worklist scope notes, follow-up to the 2026-10-04 review)', () => {
  it('shows a legacy, unprefixed value exactly as it is - it is already a name', () => {
    expect(labelForIdentity([], 'C. Checker')).toBe('C. Checker');
  });

  it('finds the name behind an email: identity, from whichever position carries it', () => {
    const cases = [caseRow({ id: 'a', adviser: 'Jane Adviser', adviserEmail: 'jane@example.com' })];
    expect(labelForIdentity(cases, 'email:jane@example.com')).toBe('Jane Adviser');
  });

  it('finds the name behind a contact: identity, via a checker position', () => {
    const cases = [caseRow({ id: 'a', taxChecker: 'Carol Checker', taxCheckerId: 'C1' })];
    expect(labelForIdentity(cases, 'contact:c1')).toBe('Carol Checker');
  });

  it('resolves a contact id through the directory email, same as the identity it names', () => {
    const cases = [caseRow({ id: 'a', taxChecker: 'Carol Checker', taxCheckerId: 'C1' })];
    const contactEmails = contactEmailsOf([{ id: 'c1', email: 'carol@example.com' }]);
    expect(labelForIdentity(cases, 'email:carol@example.com', contactEmails)).toBe('Carol Checker');
  });

  it('falls back to the email for an email: identity nobody on these cases carries', () => {
    expect(labelForIdentity([], 'email:nobody@example.com')).toBe('nobody@example.com');
  });

  it('falls back to the id for a contact: identity nobody on these cases carries', () => {
    expect(labelForIdentity([], 'contact:c9')).toBe('c9');
  });
});

describe('isPersonRole', () => {
  it('rejects an unknown position from the URL', () => {
    expect(isPersonRole('Adviser')).toBe(true);
    expect(isPersonRole('Administrator')).toBe(false);
    expect(isPersonRole(undefined)).toBe(false);
  });
});

describe('caseloadByIdentity', () => {
  it('counts a case once for someone holding two positions on it', () => {
    const loads = caseloadByIdentity([
      caseRow({ id: 'a', adviser: 'Jane Adviser', aqsChecker: 'Jane Adviser' }),
    ]);

    const jane = loads.get('name:jane adviser');
    expect(jane?.totalCases).toBe(1);
    expect(jane?.roles).toEqual(['Adviser', 'Checker']);
  });

  it('aggregates a person across separate cases and positions', () => {
    const loads = caseloadByIdentity([
      caseRow({ id: 'a', adviser: 'Jane Adviser', status: 'Closed', latestOutcome: 'Pass' }),
      caseRow({ id: 'b', paraplanner: 'Jane Adviser', status: 'Assigned' }),
    ]);

    const jane = loads.get('name:jane adviser');
    expect(jane?.totalCases).toBe(2);
    expect(jane?.closedCases).toBe(1);
    expect(jane?.openCases).toBe(1);
    expect(jane?.outcomes.Pass).toBe(1);
    expect(jane?.notGraded).toBe(1);
  });

  it('matches on name case-insensitively so a directory join is not defeated by casing', () => {
    const loads = caseloadByIdentity([caseRow({ id: 'a', adviser: 'JANE ADVISER' })]);

    expect(loads.get('name:jane adviser')?.name).toBe('JANE ADVISER');
  });

  it('ignores blank names rather than inventing an empty person', () => {
    const loads = caseloadByIdentity([caseRow({ id: 'a', adviser: '   ', aqsChecker: null })]);

    expect(loads.size).toBe(0);
  });
});

describe('people are keyed by email (two people, one name)', () => {
  const a = caseRow({ id: '1', adviser: 'Adam Smith', adviserEmail: 'adam.smith@example.com' });
  const b = caseRow({ id: '2', adviser: 'Adam Smith', adviserEmail: 'adam.smith2@example.com' });

  it('keeps two advisers of one name apart', () => {
    const loads = caseloadByIdentity([a, b]);
    expect(loads.get('email:adam.smith@example.com')?.totalCases).toBe(1);
    expect(loads.get('email:adam.smith2@example.com')?.totalCases).toBe(1);
  });

  it('joins a checker to the same person through the directory email', () => {
    const c = caseRow({
      id: '3',
      adviser: null,
      adviserEmail: null,
      taxChecker: 'Adam Smith',
      taxCheckerId: 'C1',
    });
    const loads = caseloadByIdentity(
      [a, c],
      contactEmailsOf([{ id: 'c1', email: 'Adam.Smith@example.com' }]),
    );
    const load = loads.get('email:adam.smith@example.com');
    expect(load?.totalCases).toBe(2);
    expect(load?.roles).toEqual(['Adviser', 'Checker']);
  });

  it('falls back to the name only for a legacy case with no email', () => {
    const legacy = caseRow({ id: '4', adviser: 'Old Adviser', adviserEmail: null });
    expect(caseloadByIdentity([legacy]).has('name:old adviser')).toBe(true);
  });

  it("lists one adviser identity's cases", () => {
    expect(
      casesForPerson([a, b], 'Adviser', 'email:adam.smith2@example.com').map((c) => c.id),
    ).toEqual(['2']);
  });
});

describe('checkerIdentity', () => {
  // The worklist's Tax/AQS checker cells build a link from one name and one contact id at a
  // time, outside any CaseSummary - this pins that the identity it builds is exactly what
  // casesForPerson matches, so that link never again regresses to a bare, unprefixed name.
  it('matches a checker link built from their contact id, via the directory email', () => {
    const c = caseRow({ id: '5', taxChecker: 'Carol Checker', taxCheckerId: 'C9' });
    const contactEmails = contactEmailsOf([{ id: 'c9', email: 'carol.checker@example.com' }]);

    const identity = checkerIdentity('Carol Checker', 'C9', contactEmails);

    expect(identity).toBe('email:carol.checker@example.com');
    expect(casesForPerson([c], 'Checker', identity, contactEmails).map((item) => item.id)).toEqual([
      '5',
    ]);
  });

  it('matches a checker link built from their contact id when the directory holds no email for them', () => {
    const c = caseRow({ id: '6', aqsChecker: 'Dave Checker', aqsCheckerId: 'C10' });

    const identity = checkerIdentity('Dave Checker', 'C10');

    expect(identity).toBe('contact:c10');
    expect(casesForPerson([c], 'Checker', identity).map((item) => item.id)).toEqual(['6']);
  });
});

describe('emailFromIdentity', () => {
  // F6 of the 2026-10-04 final review: the People page's unregistered rows are keyed on
  // identityOf's own output, so an email:-prefixed one must show that address, not a blank.
  it('reads the address out of an email: identity', () => {
    expect(emailFromIdentity('email:adam.strumidlo@example.com')).toBe('adam.strumidlo@example.com');
  });

  it('is empty for a contact: identity, which names no email', () => {
    expect(emailFromIdentity('contact:c10')).toBe('');
  });

  it('is empty for a name: identity, the legacy no-email fallback', () => {
    expect(emailFromIdentity('name:sam adviser')).toBe('');
  });
});
