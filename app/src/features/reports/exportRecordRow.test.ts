import { describe, expect, it } from 'vitest';
import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';
import { toExportRecordRow } from './exportRecordRow';

function record(fields: Record<string, unknown>): Al_exportrecords {
  return { al_exportrecordid: 'r-1', al_name: 'Export 1', ...fields } as unknown as Al_exportrecords;
}

describe('the batch an export record is shown under', () => {
  it('reads the batch name from the lookup annotation', () => {
    // DEV, 2026-09-28: the Batch column was blank on every row. getAll leaves
    // al_exportbatchidname empty and sends the name as the lookup's formatted value.
    const row = toExportRecordRow(
      record({
        _al_exportbatchid_value: 'b-1',
        '_al_exportbatchid_value@OData.Community.Display.V1.FormattedValue': 'Trail Light export 2026-09-28 11:57',
      }),
      new Map(),
    );

    expect(row.batchName).toBe('Trail Light export 2026-09-28 11:57');
  });

  it('falls back to the batch the page already loaded when no annotation is sent', () => {
    const row = toExportRecordRow(
      record({ _al_exportbatchid_value: 'b-1' }),
      new Map([['b-1', 'Trail Light export 2026-09-23 08:58']]),
    );

    expect(row.batchName).toBe('Trail Light export 2026-09-23 08:58');
  });
});

describe('the case an export record links to', () => {
  it('carries the case id and its reference from the lookup annotation', () => {
    const row = toExportRecordRow(
      record({
        _al_outcomecaseid_value: 'c-1',
        '_al_outcomecaseid_value@OData.Community.Display.V1.FormattedValue': 'OT-000123',
      }),
      new Map(),
    );

    expect(row.caseId).toBe('c-1');
    expect(row.caseReference).toBe('OT-000123');
  });

  it('leaves both empty on a record with no case', () => {
    const row = toExportRecordRow(record({}), new Map());

    expect(row.caseId).toBeNull();
    expect(row.caseReference).toBe('');
  });
});
