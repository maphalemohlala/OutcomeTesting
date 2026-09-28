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
