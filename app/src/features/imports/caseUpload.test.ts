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
    dueDate = '',
  } = options;
  return [
    taskId,
    'IOA07028411',
    client,
    'Jane Adviser',
    'Cara Checker',
    status,
    outcome,
    '',
    '',
    dueDate,
    'Pre-Advice Check Required',
    'Jessica Bell',
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
  it('takes the paraplanner from who raised the task', () => {
    // AssignedBy is the paraplanner who raised the pre-advice check (project owner,
    // 2026-09-12); AssignedTo is the name the file carried for the checker.
    const [only] = parse(row('1')).valid;

    expect(only.record.al_paraplanner).toBe('Jessica Bell');
    expect(only.record.al_checkername).toBe('Cara Checker');
    expect(only.record.al_assignedby).toBeUndefined();
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
    expect(parse(row('1', { dueDate: '2026-08-06' })).valid[0].record.al_duedate).toBe('2026-08-06');
  });

  it('still reads a UK date, for a hand-edited file', () => {
    expect(parse(row('1', { dueDate: '06/08/2026' })).valid[0].record.al_duedate).toBe('2026-08-06');
  });

  it('rejects a date that does not exist', () => {
    const result = parse(row('1', { dueDate: '31/02/2026' }));

    expect(result.valid).toHaveLength(0);
    expect(result.invalid[0].reason).toContain('not a valid date');
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
