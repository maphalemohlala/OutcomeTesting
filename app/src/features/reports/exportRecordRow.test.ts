import { describe, expect, it } from 'vitest';
import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';
import { toExportRecordRow } from './exportRecordRow';

function record(fields: Record<string, unknown>): Al_exportrecords {
  return { al_exportrecordid: 'r-1', al_name: 'Export 1', ...fields } as unknown as Al_exportrecords;
}

describe('the case an export record links to', () => {
  it('carries the case id and its reference from the lookup annotation', () => {
    const row = toExportRecordRow(
      record({
        _al_outcomecaseid_value: 'c-1',
        '_al_outcomecaseid_value@OData.Community.Display.V1.FormattedValue': 'OT-000123',
      }),
    );

    expect(row.caseId).toBe('c-1');
    expect(row.caseReference).toBe('OT-000123');
  });

  it('leaves both empty on a record with no case', () => {
    const row = toExportRecordRow(record({}));

    expect(row.caseId).toBeNull();
    expect(row.caseReference).toBe('');
  });
});
