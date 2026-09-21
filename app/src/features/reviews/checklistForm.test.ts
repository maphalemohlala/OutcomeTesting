import { describe, expect, it } from 'vitest';
import type { Al_outcomecases } from '../../generated/models/Al_outcomecasesModel';
import {
  caseChecklist,
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
  code: string | null = null,
): FormRow<TickedAnswer> => ({
  key,
  question: key,
  responseTypeValue,
  responseType: '',
  mandatory: true,
  code,
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
    // Deliberately unlike the IO reference below. They were both 'IO-100001', so the header
    // binding to the wrong one of the two read as correct and the test agreed with it
    // (F6, 2026-09-20).
    al_casereference: 'TASK-100001',
    al_ioreference: 'IO-100001',
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
    al_taxcheckername: 'T. Tax',
    al_aqscheckername: 'A. Aqs',
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
      'Date of meeting - Client contact',
      'Product / solution type',
      'Sample source',
      'Tax Checker',
      'AQS Checker',
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
    expect(byLabel.get('Date of meeting - Client contact')).toBe('01 Aug 2026');
    expect(byLabel.get('Vulnerable client?')).toBe('Potentially vulnerable');
    // The IO reference the business quotes is ClientRef, held in al_ioreference. The case
    // reference is the TaskID and belongs to the heading, not to this field.
    expect(byLabel.get('IO reference')).toBe('IO-100001');
    expect(byLabel.get('IO reference')).not.toBe('TASK-100001');
    expect(byLabel.get('For Tax team usage')).toBe('Submit to AQS');
  });

  it('names the products the case covers, not the free text they replaced', () => {
    // The header read al_products and nothing else, so a checker who ticked two products
    // saved them, reopened the case and still saw the imported text (project owner,
    // 2026-09-21: "the product page is still a free text").
    const byLabel = new Map(
      caseHeaderFields(record, ['Pension', 'ISA']).map((f) => [f.label, f.value]),
    );
    expect(byLabel.get('Product(s)')).toBe('Pension; ISA');
  });

  it('falls back to the free text for a case that has no products ticked', () => {
    // Cases imported before the list existed carry their products as text and must keep
    // showing it; reading only the new field would blank the header on every one of them.
    const byLabel = new Map(caseHeaderFields(record).map((f) => [f.label, f.value]));
    expect(byLabel.get('Product(s)')).toBe('ISA');
  });

  it('prefers the managed option over the choice column on every migrated list', () => {
    // Pre or post check was left reading its choice column when the other three moved, so
    // a case pointed at a managed option showed the value it was migrated FROM.
    const byLabel = new Map(
      caseHeaderFields({
        ...record,
        al_casetypeidname: 'Chosen case type',
        al_producttypeidname: 'Chosen product type',
        al_samplesourceidname: 'Chosen sample source',
        al_preorpostcheckidname: 'Chosen check point',
      } as unknown as Al_outcomecases).map((f) => [f.label, f.value]),
    );
    expect(byLabel.get('Case type')).toBe('Chosen case type');
    expect(byLabel.get('Product / solution type')).toBe('Chosen product type');
    expect(byLabel.get('Sample source')).toBe('Chosen sample source');
    expect(byLabel.get('Pre or post check')).toBe('Chosen check point');
  });

  it('leaves an unrecorded field null rather than putting a raw value on screen', () => {
    const fields = caseHeaderFields({
      al_outcomecaseid: 'c2',
      al_name: '',
      al_casereference: 'IO-2',
      al_casestatus: 120910580,
    } as unknown as Al_outcomecases);
    expect(fields.find((f) => f.label === 'Adviser status')?.value).toBeNull();
    expect(fields.find((f) => f.label === 'Date of meeting - Client contact')?.value).toBeNull();
  });
});

describe('failPoints', () => {
  // al_Name holds the document's whole row, category prefix included; al_Category is the
  // grouping beside it, not a piece the label is built from.
  const reasons: FailReasonRef[] = [
    { id: 'rec-1', name: 'Record Keeping - Client consent not evident on file', category: 'Record Keeping', categoryValue: 120910402, order: 8 },
    { id: 'aml-1', name: 'AML - ID verification issue', category: 'AML', categoryValue: 120910400, order: 1 },
    { id: 'tax-2', name: 'Tax check – insufficient evidence to complete the check or to pass', category: 'Tax check', categoryValue: 120910403, order: 20 },
    { id: 'tax-1', name: 'Tax check - not completed when this should have been', category: 'Tax check', categoryValue: 120910403, order: 19 },
    { id: 'bre-1', name: 'Breach - Any other process breach has been identified', category: 'Breach', categoryValue: 120910401, order: 6 },
  ];

  it('offers every reason, whatever its category, in display order', () => {
    // One undivided list on the document, and one here: the category groups the reasons, it
    // does not decide who may tick them.
    expect(failPoints(reasons, new Set()).map((p) => p.id)).toEqual([
      'aml-1',
      'bre-1',
      'rec-1',
      'tax-1',
      'tax-2',
    ]);
  });

  it('offers the Tax check reasons to whoever is looking, not only the Tax team', () => {
    expect(failPoints(reasons, new Set()).map((p) => p.id)).toContain('tax-1');
  });

  it('ticks a reason recorded on the review and labels it with the document row', () => {
    const points = failPoints(reasons, new Set(['bre-1']));
    expect(points.find((p) => p.id === 'bre-1')).toEqual({
      id: 'bre-1',
      label: 'Breach - Any other process breach has been identified',
      ticked: true,
    });
    expect(points.find((p) => p.id === 'aml-1')?.ticked).toBe(false);
  });

  it("keeps the last row's en-dash, which the other nineteen rows do not use", () => {
    // The document punctuates row 20 differently from rows 1-19. The label is the stored row,
    // not a category plus a separator, so the inconsistency survives instead of being tidied.
    const labels = failPoints(reasons, new Set()).map((p) => p.label);
    expect(labels).toContain('Tax check – insufficient evidence to complete the check or to pass');
    expect(labels).toContain('Tax check - not completed when this should have been');
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

  it('orders the tax check outcome PASS, PASS WITH ISSUES, FAIL as the document does', () => {
    expect(inlineOptionsFor(120910006, true)).toEqual([
      { value: 120910300, label: 'PASS' },
      { value: 120910302, label: 'PASS WITH ISSUES' },
      { value: 120910301, label: 'FAIL' },
    ]);
  });

  it('reads an inline 120910006 off a Tax review as the scale, not as the tax check', () => {
    // The reorder and the relabel above are the Tax check's, not the scale's (AD-055
    // amended). Until now the inline path applied them to every 120910006 question it drew,
    // on the reading that it was Q-TAX-02's alone - which AD-123 checklist administration
    // made false. Retyping a question to this scale breaks its section's uniform grid, the
    // section falls to the inline path, and an AQS question was drawn as a tax check:
    // "PASS / PASS WITH ISSUES / FAIL" over the suitability scale's own values.
    //
    // A tick on what read "PASS WITH ISSUES" saved 120910302 - Insufficient evidence, a
    // non-pass that raises remediation. The values were never wrong; the wording over them
    // was, which is the worse half to get wrong.
    expect(inlineOptionsFor(120910006, false)).toEqual([
      { value: 120910300, label: 'PASS' },
      { value: 120910301, label: 'FAIL' },
      { value: 120910302, label: 'INSUFFICIENT EVIDENCE' },
    ]);
  });

  it('defaults to the scale rather than to the tax check when no discipline is given', () => {
    // The safe default of the pair: optionsFor already reads an absent discipline as "not a
    // tax check", and a caller that forgets to say gets the wording six AQS sections share
    // rather than the one question's rename.
    expect(inlineOptionsFor(120910006)).toEqual(inlineOptionsFor(120910006, false));
  });

  it('renames 120910302 for the tax check only, leaving the value it saves alone', () => {
    // AD-055 amended: the Tax check reads 120910302 as "Pass with issues" where the
    // suitability grid that shares 120910006 still reads it as "Insufficient evidence".
    // Wording only - the value the checker ticks is the same one, which is what the value
    // assertions here are for, and so the remediation it triggers is unchanged too.
    expect(inlineOptionsFor(120910006, true)[1]).toEqual({
      value: 120910302,
      label: 'PASS WITH ISSUES',
    });
    expect(optionsFor(120910006)[2]).toEqual({
      value: 120910302,
      label: 'Insufficient evidence',
    });
  });

  it('heads a Tax review grid Pass with issues, and every other review Insufficient evidence', () => {
    // Reaches only a section added through checklist administration (AD-123) whose questions
    // all share 120910006 - Q-TAX-02 itself is drawn inline. Keyed on the review because
    // "for tax checks" is what was reworded, so a Both-owned section answered on a Tax
    // review is a tax check. Same value either way; only the wording moves.
    expect(optionsFor(120910006, true).map((o) => o.label)).toEqual([
      'Pass',
      'Fail',
      'Pass with issues',
    ]);
    expect(optionsFor(120910006, false).map((o) => o.label)).toEqual([
      'Pass',
      'Fail',
      'Insufficient evidence',
    ]);
    expect(optionsFor(120910006, true)[2].value).toBe(120910302);
  });

  it('leaves a scale the Tax check never reworded alone on a Tax review', () => {
    // The Consumer Duty overlay shares 120910302 and was not part of the rename.
    expect(optionsFor(120910009, true).map((o) => o.label)).toEqual([
      'Yes',
      'No',
      'Insufficient evidence',
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


describe('caseChecklist', () => {
  const caseRecord = (overrides: Partial<Al_outcomecases> = {}): Al_outcomecases =>
    ({ al_outcomecaseid: 'case-1', ...overrides }) as Al_outcomecases;

  it('reads the ticked items, one per line', () => {
    const result = caseChecklist(
      caseRecord({ al_checklistitems: 'Tax Check\nHigh Risk Item 1\nEnhanced Supervision' }),
    );

    expect(result.items).toEqual(['Tax Check', 'High Risk Item 1', 'Enhanced Supervision']);
  });

  it('keeps the order the import wrote', () => {
    const result = caseChecklist(caseRecord({ al_checklistitems: 'Leaver\nTax Check' }));

    expect(result.items).toEqual(['Leaver', 'Tax Check']);
  });

  it('drops blank lines rather than rendering empty bullets', () => {
    // A trailing newline is ordinary in a memo column: the import joins with "\n" and
    // does not trim what it stores.
    const result = caseChecklist(caseRecord({ al_checklistitems: 'Tax Check\n\n  \nLeaver\n' }));

    expect(result.items).toEqual(['Tax Check', 'Leaver']);
  });

  it('reads no items when the case carries none', () => {
    expect(caseChecklist(caseRecord()).items).toEqual([]);
    expect(caseChecklist(caseRecord({ al_checklistitems: '' })).items).toEqual([]);
    expect(caseChecklist(caseRecord({ al_checklistitems: '   ' })).items).toEqual([]);
  });

  it('carries who completed the checklist and when', () => {
    const result = caseChecklist(
      caseRecord({
        al_checklistitems: 'Tax Check',
        al_checklistcompletedby: 'Miko Stewart',
        al_checklistcompleteddate: '2026-09-04T13:54:00Z',
      }),
    );

    expect(result.completedBy).toBe('Miko Stewart');
    expect(result.completedOn).toBe('2026-09-04T13:54:00Z');
  });

  it('reports a missing or blank stamp as absent rather than empty', () => {
    const result = caseChecklist(
      caseRecord({ al_checklistitems: 'Tax Check', al_checklistcompletedby: '  ' }),
    );

    expect(result.completedBy).toBeNull();
    expect(result.completedOn).toBeNull();
  });
});
