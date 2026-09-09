import { describe, expect, it } from 'vitest';
import type { Al_outcomecases } from '../../generated/models/Al_outcomecasesModel';
import {
  caseHeaderFields,
  failPoints,
  inlineOptionsFor,
  isOutcomeLens,
  isTicked,
  optionGridColumns,
  optionsFor,
  remediationSummary,
  sectionLayout,
  type FailReasonRef,
  type TickedAnswer,
} from './checklistForm';
import type { FormRow, ReviewSection } from './reviewSections';

const row = (
  key: string,
  responseTypeValue: number | null,
  response: Partial<TickedAnswer> | null = null,
): FormRow<TickedAnswer> => ({
  key,
  question: key,
  responseTypeValue,
  responseType: '',
  mandatory: true,
  response: response
    ? {
        id: `r-${key}`,
        versionId: key,
        question: key,
        responseTypeValue,
        responseType: '',
        answerChoice: null,
        answerChoices: [],
        ...response,
      }
    : null,
});

const section = (code: string, rows: FormRow<TickedAnswer>[]): ReviewSection<TickedAnswer> => ({
  id: code,
  code,
  name: code,
  helpText: null,
  rows,
});

describe('sectionLayout', () => {
  it('lays a section whose rows share one tick scale out as a grid with that scale', () => {
    const layout = sectionLayout(
      section('S-E1', [row('a', 120910006), row('b', 120910006)]),
    );
    expect(layout).toEqual({
      kind: 'grid',
      options: [
        { value: 120910300, label: 'Pass' },
        { value: 120910301, label: 'Fail' },
        { value: 120910302, label: 'Insufficient evidence' },
      ],
    });
  });

  it('lays a mixed section out inline, as the document does for File Quality outcome', () => {
    const layout = sectionLayout(
      section('S-FQOUT', [row('a', 120910005), row('b', 120910001), row('c', 120910007)]),
    );
    expect(layout).toEqual({ kind: 'inline' });
  });

  it('lays a section of free text or select questions out inline', () => {
    expect(sectionLayout(section('S-GRADE', [row('a', 120910010), row('b', 120910003)]))).toEqual(
      { kind: 'inline' },
    );
    expect(sectionLayout(section('S-TAX', [row('a', 120910004)]))).toEqual({ kind: 'inline' });
  });

  it('has no grid for an empty section', () => {
    expect(sectionLayout(section('S-X', []))).toEqual({ kind: 'inline' });
  });
});

describe('optionsFor', () => {
  it('mirrors the permitted subset per response type', () => {
    expect(optionsFor(120910008).map((o) => o.label)).toEqual(['Yes', 'No', 'N/A']);
    expect(optionsFor(120910010).map((o) => o.label)).toEqual([
      'Pass',
      'Pass with issues',
      'Insufficient evidence',
      'Potential harm',
    ]);
    expect(optionsFor(120910004)).toHaveLength(5);
    expect(optionsFor(120910003)).toHaveLength(9);
  });

  it('has no options for a text, date or unknown type', () => {
    expect(optionsFor(120910000)).toEqual([]);
    expect(optionsFor(120910002)).toEqual([]);
    expect(optionsFor(null)).toEqual([]);
  });
});

describe('isTicked', () => {
  it('ticks the recorded single choice', () => {
    const r = row('a', 120910006, { answerChoice: 120910301 });
    expect(isTicked(r, { value: 120910301, label: 'Fail' })).toBe(true);
    expect(isTicked(r, { value: 120910300, label: 'Pass' })).toBe(false);
  });

  it('ticks each recorded multi-select value', () => {
    const r = row('a', 120910004, { answerChoices: [120910341, 120910344] });
    expect(isTicked(r, { value: 120910341, label: 'Trust' })).toBe(true);
    expect(isTicked(r, { value: 120910340, label: 'LSA' })).toBe(false);
  });

  it('ticks nothing on an unanswered row', () => {
    expect(isTicked(row('a', 120910006), { value: 120910300, label: 'Pass' })).toBe(false);
  });
});

describe('isOutcomeLens', () => {
  it('is true for the suitability sections E1 to E5 only', () => {
    expect(isOutcomeLens(section('S-E1', []))).toBe(true);
    expect(isOutcomeLens(section('S-E5', []))).toBe(true);
    expect(isOutcomeLens(section('S-CD', []))).toBe(false);
    expect(isOutcomeLens({ ...section('x', []), code: null })).toBe(false);
  });
});

describe('caseHeaderFields', () => {
  const record = {
    al_outcomecaseid: 'c1',
    al_name: 'IO-100001',
    al_casereference: 'IO-100001',
    al_casestatus: 120910585,
    al_advisername: 'A. Adviser',
    al_adviserstatus: 120910501,
    al_advisercode: 'ADV1',
    al_paraplanner: 'P. Planner',
    al_paraplannercode: 'PP1',
    al_products: 'ISA',
    al_casetype: 120910510,
    al_advicedate: '2026-08-01T00:00:00Z',
    al_productsolutiontype: 120910520,
    al_samplesource: 120910530,
    al_checkername: 'C. Checker',
    al_checkdate: '2026-08-20T00:00:00Z',
    al_clientname: 'J.S.',
    al_preorpostcheck: 120910541,
    al_vulnerableclient: 120910552,
    al_taxcheckrequired: 120910560,
    al_taxteamdisposition: 120910570,
  } as unknown as Al_outcomecases;

  it('lists the header fields in the order the document lays them out', () => {
    expect(caseHeaderFields(record).map((f) => f.label)).toEqual([
      'Adviser name',
      'Adviser status',
      'Adviser code',
      'Paraplanner',
      'Paraplanner code',
      'Product(s)',
      'Case type',
      'Advice date',
      'Product / solution type',
      'Sample source',
      'Checker name',
      'Check date',
      'Client name / initials',
      'IO reference',
      'Pre or post check',
      'Vulnerable client?',
      'Tax check required',
      'For Tax team usage',
    ]);
  });

  it('labels the choice columns and formats the dates', () => {
    const byLabel = new Map(caseHeaderFields(record).map((f) => [f.label, f.value]));
    expect(byLabel.get('Adviser status')).toBe('CAS');
    expect(byLabel.get('Case type')).toBe('New advice');
    expect(byLabel.get('Advice date')).toBe('01 Aug 2026');
    expect(byLabel.get('Vulnerable client?')).toBe('Potentially vulnerable');
    expect(byLabel.get('IO reference')).toBe('IO-100001');
    expect(byLabel.get('For Tax team usage')).toBe('Submit to AQS');
  });

  it('leaves an unrecorded field null rather than putting a raw value on screen', () => {
    const fields = caseHeaderFields({
      al_outcomecaseid: 'c2',
      al_name: '',
      al_casereference: 'IO-2',
      al_casestatus: 120910580,
    } as unknown as Al_outcomecases);
    expect(fields.find((f) => f.label === 'Adviser status')?.value).toBeNull();
    expect(fields.find((f) => f.label === 'Advice date')?.value).toBeNull();
  });
});

describe('failPoints', () => {
  const reasons: FailReasonRef[] = [
    { id: 'rec-1', name: 'Client consent not evident on file', category: 'Record Keeping', categoryValue: 120910402, order: 8 },
    { id: 'aml-1', name: 'ID verification issue', category: 'AML', categoryValue: 120910400, order: 1 },
    { id: 'tax-1', name: 'Not completed when this should have been', category: 'Tax check', categoryValue: 120910403, order: 19 },
    { id: 'bre-1', name: 'Any other process breach has been identified', category: 'Breach', categoryValue: 120910401, order: 6 },
  ];

  it('offers every reason, whatever its category, in display order', () => {
    // One undivided list on the document, and one here: the category groups the reasons, it
    // does not decide who may tick them.
    expect(failPoints(reasons, new Set()).map((p) => p.id)).toEqual([
      'aml-1',
      'bre-1',
      'rec-1',
      'tax-1',
    ]);
  });

  it('offers the Tax check reasons to whoever is looking, not only the Tax team', () => {
    expect(failPoints(reasons, new Set()).map((p) => p.id)).toContain('tax-1');
  });

  it('ticks a reason recorded on the review and labels it with its category', () => {
    const points = failPoints(reasons, new Set(['bre-1']));
    expect(points.find((p) => p.id === 'bre-1')).toEqual({
      id: 'bre-1',
      label: 'Breach - Any other process breach has been identified',
      ticked: true,
    });
    expect(points.find((p) => p.id === 'aml-1')?.ticked).toBe(false);
  });

  it('keeps a reason with no category rather than dropping it', () => {
    const points = failPoints(
      [{ id: 'x', name: 'New reason', category: null, categoryValue: null, order: 99 }],
      new Set(),
    );
    expect(points).toEqual([{ id: 'x', label: 'New reason', ticked: false }]);
  });
});

describe('remediationSummary', () => {
  const action = (over: Partial<Parameters<typeof remediationSummary>[0][number]>) => ({
    id: 'a1',
    reference: 'RA-1',
    description: 'Missing TOB',
    remedialAction: null,
    assignedTo: null,
    owner: 'Owner',
    dueOn: '30 Sep 2026',
    completedOn: null,
    clientContactRequired: null,
    recheckRequired: null,
    changesAdvice: null,
    ...over,
  });

  it('numbers one line per action with issue, action, owner, target date and sign-off', () => {
    const summary = remediationSummary(
      [
        action({
          remedialAction: 'TOB reissued',
          assignedTo: 'A. Adviser',
          completedOn: '25 Sep 2026',
        }),
      ],
      [{ decision: 'Approved', signedOffOn: '26 Sep 2026', remediationAction: 'RA-1', signedOffBy: 'T. Manager' }],
      [],
    );
    expect(summary.lines).toEqual([
      {
        id: 'a1',
        issue: 'Missing TOB',
        remedialAction: 'TOB reissued',
        owner: 'A. Adviser',
        targetDate: '30 Sep 2026',
        signOff: 'Approved 26 Sep 2026',
      },
    ]);
  });

  it('shows the completion as the sign-off when no decision has been recorded yet', () => {
    const summary = remediationSummary([action({ completedOn: '25 Sep 2026' })], [], []);
    expect(summary.lines[0].signOff).toBe('Completed 25 Sep 2026');
    expect(summary.lines[0].owner).toBe('Owner');
  });

  it('derives "all approved" from every action’s latest decision', () => {
    const two = [action({}), action({ id: 'a2', reference: 'RA-2' })];
    const approvedOne = [
      { decision: 'Approved', signedOffOn: null, remediationAction: 'RA-1', signedOffBy: null },
    ];
    expect(remediationSummary(two, approvedOne, []).allApproved).toBe('No');
    expect(
      remediationSummary(
        two,
        [
          ...approvedOne,
          { decision: 'Approved', signedOffOn: null, remediationAction: 'RA-2', signedOffBy: null },
        ],
        [],
      ).allApproved,
    ).toBe('Yes');
    expect(remediationSummary([], [], []).allApproved).toBeNull();
  });

  it('reads the three form answers off the actions, joining distinct values', () => {
    const summary = remediationSummary(
      [
        action({ clientContactRequired: 'Yes', recheckRequired: 'No', changesAdvice: 'No' }),
        action({ id: 'a2', reference: 'RA-2', clientContactRequired: 'Potentially', recheckRequired: 'No' }),
      ],
      [],
      [],
    );
    expect(summary.clientContactRequired).toBe('Yes, Potentially');
    expect(summary.recheckRequired).toBe('No');
    expect(summary.changesAdvice).toBe('No');
  });

  it('shows the regraded outcome and the sign-offs when recorded, and nothing otherwise', () => {
    const empty = remediationSummary([], [], []);
    expect(empty.regradedOutcome).toBeNull();
    expect(empty.supervisorSignOff).toBeNull();
    expect(empty.adviserSignOff).toBeNull();

    const summary = remediationSummary(
      [action({ assignedTo: 'A. Adviser', completedOn: '25 Sep 2026' })],
      [{ decision: 'Approved', signedOffOn: '26 Sep 2026', remediationAction: 'RA-1', signedOffBy: 'T. Manager' }],
      [{ finalOutcome: 'Pass', regradedOn: '27 Sep 2026' }],
    );
    expect(summary.regradedOutcome).toBe('Pass - 27 Sep 2026');
    expect(summary.supervisorSignOff).toBe('T. Manager, 26 Sep 2026');
    expect(summary.adviserSignOff).toBe('A. Adviser, 25 Sep 2026');
  });
});

describe('inlineOptionsFor', () => {
  it('upper-cases the outcome scales the document draws inline', () => {
    expect(inlineOptionsFor(120910005).map((o) => o.label)).toEqual(['PASS', 'FAIL']);
    expect(inlineOptionsFor(120910007).map((o) => o.label)).toEqual(['YES', 'NO']);
    expect(inlineOptionsFor(120910010).map((o) => o.label)).toEqual([
      'PASS',
      'PASS WITH ISSUES',
      'INSUFFICIENT EVIDENCE',
      'POTENTIAL HARM',
    ]);
  });

  it('orders the tax check outcome PASS, INSUFFICIENT EVIDENCE, FAIL as the document does', () => {
    expect(inlineOptionsFor(120910006)).toEqual([
      { value: 120910300, label: 'PASS' },
      { value: 120910302, label: 'INSUFFICIENT EVIDENCE' },
      { value: 120910301, label: 'FAIL' },
    ]);
  });

  it('leaves the grid scales alone, so a tick column stays titled Pass, Fail, N/A', () => {
    // The same values head a grid column in title case; only the inline path re-cases them.
    expect(optionsFor(120910006).map((o) => o.label)).toEqual([
      'Pass',
      'Fail',
      'Insufficient evidence',
    ]);
    expect(optionsFor(120910008).map((o) => o.label)).toEqual(['Yes', 'No', 'N/A']);
  });

  it('leaves the root cause list in the title case the document sets it in', () => {
    const labels = inlineOptionsFor(120910003).map((o) => o.label);
    expect(labels[0]).toBe('FactFind quality');
    expect(labels).toHaveLength(9);
  });

  it('reports nothing for a response type with no options', () => {
    expect(inlineOptionsFor(null)).toEqual([]);
    expect(inlineOptionsFor(120910001)).toEqual([]);
  });
});

describe('optionGridColumns', () => {
  it('grids the nine root causes three to a row, as the document lays them out', () => {
    expect(optionGridColumns(120910003)).toBe(3);
  });

  it('leaves every other list on one row', () => {
    expect(optionGridColumns(120910010)).toBeNull();
    expect(optionGridColumns(120910004)).toBeNull();
    expect(optionGridColumns(null)).toBeNull();
  });
});
