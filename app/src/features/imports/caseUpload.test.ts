import { describe, expect, it } from 'vitest';
// The fixture is loaded through Vite rather than node:fs, so the app project keeps its
// browser-only type scope.
import fixture from '../../../../data/io-task-extract-sample.csv?raw';
import {
  parseCaseCsv,
  TAX_CHECK_REQUIRED_NO,
  TAX_CHECK_REQUIRED_YES,
} from './caseUpload';

/**
 * The preview side of the same rules `ImportRulesTests` pins server-side. A rule that
 * disagrees between the two shows a row as importable here and then rejects it there, so
 * these cases deliberately mirror that file.
 */
const HEADER =
  'TaskID,ServiceCaseSequentialRef,Client,AdviserName,AssignedTo,Status,Outcome,' +
  'CompletedBy,CompletedDate,DueDate,TaskType,AssignedBy,' +
  'ChecklistItem1,CompletedBy1,CompletionDate1,ChecklistItem2,CompletedBy2,CompletionDate2';

const STAMP = '2026-09-04T13:54:00';

interface RowOptions {
  item1?: string;
  by1?: string;
  date1?: string;
  item2?: string;
  by2?: string;
  date2?: string;
  status?: string;
  outcome?: string;
  client?: string;
  /**
   * Feeds CompletedDate, which the date-parsing cases below use as their vehicle. They
   * rode on DueDate until 2026-09-19, when that column stopped being mapped: the check's
   * deadline is derived from the upload, so the sheet's own due date is ignored. The
   * parsing rules they pin are unchanged, so they only needed a column still being read.
   */
  completedDate?: string;
  /** Feeds DueDate, which is deliberately ignored. Only the case below passes one. */
  dueDate?: string;
}

/** A row carrying one stamped Tax item by default, so a test not about the checklist still imports. */
function row(taskId: string, options: RowOptions = {}): string {
  const {
    item1 = 'Tax Check',
    by1 = 'Miko Stewart',
    date1 = STAMP,
    item2 = '',
    by2 = '',
    date2 = '',
    status = 'Complete',
    outcome = '',
    client = 'A. Client',
    completedDate = '',
    dueDate = '',
  } = options;
  return [
    taskId,
    'IOA07028411',
    client,
    'Jane Adviser',
    // AssignedTo. The extract puts the CHECKER here; calling this value "Pat Paraplanner"
    // is the assumption that got written into the column map twice.
    'Chris Checker',
    status,
    outcome,
    '',
    completedDate,
    dueDate,
    'Pre-Advice Check Required',
    // AssignedBy: the para-planner who raised the task (AD-160).
    'Pat Paraplanner',
    item1,
    by1,
    date1,
    item2,
    by2,
    date2,
  ].join(',');
}

function parse(...rows: string[]) {
  return parseCaseCsv(`${HEADER}\r\n${rows.join('\r\n')}\r\n`);
}

describe('the import key', () => {
  it('keys a case on the task id', () => {
    const result = parse(row('253925362'));

    expect(result.fatal).toBeNull();
    expect(result.valid).toHaveLength(1);
    expect(result.valid[0].reference).toBe('253925362');
    expect(result.valid[0].record.al_casereference).toBe('253925362');
  });

  it('rejects a row with no task id', () => {
    const result = parse(row(''));

    expect(result.valid).toHaveLength(0);
    expect(result.invalid[0].reason).toContain('Missing TaskID');
  });

  it('rejects the second of two rows naming the same task id', () => {
    const result = parse(row('1', { client: 'First' }), row('1', { client: 'Second' }));

    expect(result.valid).toHaveLength(1);
    expect(result.invalid[0].reason).toContain('Duplicate TaskID');
  });

  it('imports two tasks that share one service case', () => {
    const result = parse(row('254471517'), row('254471891'));

    expect(result.invalid).toHaveLength(0);
    expect(result.valid).toHaveLength(2);
    expect(result.valid[0].record.al_servicecaseref).toBe('IOA07028411');
  });

  it('reports a file with no task id column as fatal', () => {
    expect(parseCaseCsv('Client,AdviserName\r\nA. Client,Jane Adviser\r\n').fatal).toContain('TaskID');
  });
});

describe('who the file names', () => {
  it('takes the paraplanner from AssignedBy', () => {
    // REVERSED 2026-09-20 (AD-160). This asserted AssignedTo until today, on the 2026-09-14
    // direction that the real extract put the two the other way round from the sample.
    //
    // The owner reaffirmed AssignedBy with that conflict put to them explicitly. It is what
    // the written specification always said, and what the only extract in this repository
    // shows - AssignedBy is "Paraplanner N" in every row of it.
    //
    // This file and ImportRules.cs must agree: the app transcribes the workbook and the
    // plug-in parses the CSV, so a mapping that changed in one and not the other would
    // import different people depending on which route the upload took.
    const [only] = parse(row('1')).valid;

    expect(only.record.al_paraplanner).toBe('Pat Paraplanner');
    expect(only.record.al_assignedby).toBeUndefined();
  });

  it('does not import the AssignedTo name anywhere', () => {
    // Not merely mapped elsewhere - absent. AD-113: a name the file carried never proved a
    // case was allocated, and a re-import must not overwrite whoever is.
    const [only] = parse(row('1')).valid;

    expect(Object.values(only.record)).not.toContain('Chris Checker');
  });

  it('does not import a checker name', () => {
    // The checker is set manually (project owner, 2026-09-14) - by allocation, by a claim, or
    // by editing the case. Not written at all rather than written from another column, so a
    // re-import of the same TaskID cannot overwrite whoever is actually allocated.
    const [only] = parse(row('1')).valid;

    expect(only.record.al_checkername).toBeUndefined();
  });
});

describe('route derivation', () => {
  it('asks for a tax check when a tax item is selected', () => {
    expect(parse(row('1', { item1: 'Tax Check' })).valid[0].record.al_taxcheckrequired).toBe(
      TAX_CHECK_REQUIRED_YES,
    );
  });

  it('treats trust documentation as a tax item', () => {
    expect(
      parse(row('1', { item1: 'Trust Documentation Check' })).valid[0].record.al_taxcheckrequired,
    ).toBe(TAX_CHECK_REQUIRED_YES);
  });

  it.each([
    'High Risk Item 1',
    'High Risk Item 2',
    'Enhanced Supervision',
    'Pre-CAS Adviser',
    'Leaver',
  ])('asks for no tax check when only %s is selected', (item) => {
    expect(parse(row('1', { item1: item })).valid[0].record.al_taxcheckrequired).toBe(
      TAX_CHECK_REQUIRED_NO,
    );
  });

  it('starts a case with both disciplines at tax', () => {
    const result = parse(
      row('1', { item1: 'High Risk Item 1', item2: 'Tax Check', by2: 'Miko Stewart', date2: STAMP }),
    );

    expect(result.valid[0].record.al_taxcheckrequired).toBe(TAX_CHECK_REQUIRED_YES);
  });

  it('accepts the short names the client used for the tax items', () => {
    const result = parse(row('1', { item1: 'Tax' }), row('2', { item1: 'Trust documentation' }));

    expect(result.invalid).toHaveLength(0);
    expect(result.valid.map((c) => c.record.al_taxcheckrequired)).toEqual([
      TAX_CHECK_REQUIRED_YES,
      TAX_CHECK_REQUIRED_YES,
    ]);
  });
});

describe('the stamp rule', () => {
  it('does not treat an unstamped item as selected', () => {
    const result = parse(row('1', { item1: 'High Risk Item 1', item2: 'Tax Check', by2: '', date2: '' }));

    expect(result.valid[0].record.al_taxcheckrequired).toBe(TAX_CHECK_REQUIRED_NO);
    expect(result.valid[0].record.al_checklistitems).toBe('High Risk Item 1');
  });

  it('reads a selection wherever the columns place it', () => {
    const packed = parse(row('1', { item1: 'Tax Check' }));
    const fixedSlot = parse(
      row('2', {
        item1: 'High Risk Item 1',
        by1: '',
        date1: '',
        item2: 'Tax Check',
        by2: 'Miko Stewart',
        date2: STAMP,
      }),
    );

    expect(packed.valid[0].record.al_taxcheckrequired).toBe(TAX_CHECK_REQUIRED_YES);
    expect(fixedSlot.valid[0].record.al_taxcheckrequired).toBe(TAX_CHECK_REQUIRED_YES);
  });
});

describe('the checklist on the case', () => {
  it('keeps every selected item name one per line', () => {
    const result = parse(
      row('1', { item1: 'High Risk Item 1', item2: 'Tax Check', by2: 'Miko Stewart', date2: STAMP }),
    );

    expect(result.valid[0].record.al_checklistitems).toBe('High Risk Item 1\nTax Check');
  });

  it('collapses the repeated stamp to one completed by and one date', () => {
    const result = parse(
      row('1', { item1: 'High Risk Item 1', item2: 'Tax Check', by2: 'Miko Stewart', date2: STAMP }),
    );

    expect(result.valid[0].record.al_checklistcompletedby).toBe('Miko Stewart');
    expect(result.valid[0].record.al_checklistcompleteddate).toBe(STAMP);
  });

  it('keeps the earliest stamp when the items disagree', () => {
    const result = parse(
      row('1', {
        item1: 'High Risk Item 1',
        date1: '2026-09-05T09:00:00',
        item2: 'Tax Check',
        by2: 'Miko Stewart',
        date2: '2026-09-04T13:54:00',
      }),
    );

    expect(result.valid[0].record.al_checklistcompleteddate).toBe('2026-09-04T13:54:00');
  });
});

describe('the two new rejections', () => {
  it('rejects a row with no selected checklist item', () => {
    const result = parse(row('1', { item1: '', by1: '', date1: '' }));

    expect(result.valid).toHaveLength(0);
    expect(result.invalid[0].reason).toContain('No checklist items');
    expect(result.invalid[0].reason).toContain('route');
  });

  it('rejects a row carrying a checklist item it does not recognise', () => {
    const result = parse(row('1', { item1: 'Vulnerability Review' }));

    expect(result.valid).toHaveLength(0);
    expect(result.invalid[0].reason).toContain('Vulnerability Review');
    expect(result.invalid[0].reason).toContain('not recognised');
  });

  it('does not let a recognised item excuse an unrecognised one', () => {
    const result = parse(
      row('1', {
        item1: 'Tax Check',
        item2: 'Vulnerability Review',
        by2: 'Miko Stewart',
        date2: STAMP,
      }),
    );

    expect(result.valid).toHaveLength(0);
    expect(result.invalid[0].reason).toContain('Vulnerability Review');
  });

  it('ignores an unrecognised name that carries no stamp', () => {
    const result = parse(
      row('1', { item1: 'Tax Check', item2: 'Vulnerability Review', by2: '', date2: '' }),
    );

    expect(result.invalid).toHaveLength(0);
    expect(result.valid[0].record.al_checklistitems).toBe('Tax Check');
  });
});

describe('the IO outcome is reference data, not a grade', () => {
  it('stores the io outcome in its own column', () => {
    const result = parse(row('1', { outcome: 'Pass with issues' }));

    expect(result.valid[0].record.al_iooutcome).toBe(120910611);
    expect(result.valid[0].record.al_outcome).toBeUndefined();
  });

  it('rejects an io outcome it does not know', () => {
    const result = parse(row('1', { outcome: 'Referred' }));

    expect(result.valid).toHaveLength(0);
    expect(result.invalid[0].reason).toContain('Referred');
  });

  it('maps the task status', () => {
    expect(parse(row('1', { status: 'Not Started' })).valid[0].record.al_iotaskstatus).toBe(120910620);
  });
});

describe('dates and derived values', () => {
  it('reads the ISO dates the workbook reader emits', () => {
    expect(parse(row('1', { completedDate: '2026-08-06' })).valid[0].record.al_iocompleteddate).toBe('2026-08-06');
  });

  it('still reads a UK date, for a hand-edited file', () => {
    expect(parse(row('1', { completedDate: '06/08/2026' })).valid[0].record.al_iocompleteddate).toBe('2026-08-06');
  });

  it('rejects a month-first date rather than misreading it, as ImportRules.ParseDate does', () => {
    expect(parse(row('1', { completedDate: '01/13/2026' })).valid).toHaveLength(0);
  });

  it('accepts a written-out date, which cannot be misread', () => {
    expect(parse(row('1', { completedDate: '31 Jan 2026' })).valid[0].record.al_iocompleteddate).toBe('2026-01-31');
  });

  it('reads an ISO timestamp as its date', () => {
    expect(parse(row('1', { completedDate: '2026-08-06T09:30:00Z' })).valid[0].record.al_iocompleteddate).toBe('2026-08-06');
  });

  it('rejects a date that does not exist', () => {
    const result = parse(row('1', { completedDate: '31/02/2026' }));

    expect(result.valid).toHaveLength(0);
    expect(result.invalid[0].reason).toContain('not a valid date');
  });

  it('ignores the due date the sheet carries', () => {
    // Project owner, 2026-09-19. DueDate is Intelligent Office's deadline for the
    // paraplanner's task; the check is due 72 hours after the upload, which
    // ImportCasesPlugin stamps server-side. Reading the column would let the spreadsheet
    // override the rule.
    const result = parse(row('1', { dueDate: '2026-08-06' }));

    expect(result.valid).toHaveLength(1);
    expect(result.valid[0].record.al_duedate).toBeUndefined();
  });

  it('does not reject a row whose due date is unreadable', () => {
    // The column is not read at all, so nothing in it can make a row invalid. Before
    // 2026-09-19 this row failed validation on a date nobody was going to use.
    expect(parse(row('1', { dueDate: 'not a date at all' })).valid).toHaveLength(1);
  });

  it('marks a pre-advice task as a pre-check', () => {
    expect(parse(row('1')).valid[0].record.al_preorpostcheck).toBe(120910540);
  });
});

describe('the supplied extract', () => {
  // data/io-task-extract-sample.csv is the workbook transcribed, with the personal columns
  // dropped (D8). Parsing it here is what catches a mapping that compiles but does not line
  // up with the real file.
  it('imports every row but the one with no checklist', () => {
    const result = parseCaseCsv(fixture);

    expect(result.fatal).toBeNull();
    expect(result.valid).toHaveLength(6);
    expect(result.invalid).toHaveLength(1);
    expect(result.invalid[0].reason).toContain('No checklist items');
  });

  it('routes every checklisted row through tax first', () => {
    // Every completed row in the sample carries both a Tax item and a High Risk item, so
    // each is a Tax-then-AQS case.
    const result = parseCaseCsv(fixture);

    expect(result.valid.map((c) => c.record.al_taxcheckrequired)).toEqual(
      Array(6).fill(TAX_CHECK_REQUIRED_YES),
    );
  });

  it('keeps the seven selected reasons and one stamp', () => {
    const [first] = parseCaseCsv(fixture).valid;

    expect(String(first.record.al_checklistitems).split('\n')).toEqual([
      'High Risk Item 1',
      'High Risk Item 2',
      'Enhanced Supervision',
      'Pre-CAS Adviser',
      'Leaver',
      'Tax Check',
      'Trust Documentation Check',
    ]);
    expect(first.record.al_checklistcompleteddate).toBe('2026-08-18T13:54:00');
  });

  it('reads the two tasks that share a service case as two cases', () => {
    const result = parseCaseCsv(fixture);
    const shared = result.valid.filter((c) => c.record.al_servicecaseref === 'IOA07066846');

    expect(shared).toHaveLength(2);
    expect(new Set(shared.map((c) => c.reference)).size).toBe(2);
  });
});
