import { describe, expect, it } from 'vitest';
import { EMPTY_SHEET_NOTE, headerFor, toSheet } from './extractSheet';

/**
 * F20, found in DEV on 2026-09-20. The "Download all data" workbook used raw Dataverse keys
 * as its column headings, OData's own annotations included. The Cases sheet had 91 columns
 * of which 33 were plumbing — `@odata.etag`,
 * `_modifiedby_value@Microsoft.Dynamics.CRM.lookuplogicalname` and the formatted-value
 * annotations — and the Sign-offs sheet had no header row at all, so the tab was completely
 * blank and read as a broken file.
 *
 * Logical names are deliberate and stay: this extract is joined against Dataverse and
 * `al_duedate` is what it is joined on. The annotations are not data, and the formatted
 * value is data worth keeping under a heading that names the field.
 */

describe('headerFor', () => {
  it('keeps a logical name as it is', () => {
    expect(headerFor('al_duedate')).toBe('al_duedate');
    expect(headerFor('_al_tcmanagerid_value')).toBe('_al_tcmanagerid_value');
  });

  it('drops the etag', () => {
    expect(headerFor('@odata.etag')).toBeNull();
  });

  it('drops the lookup logical name', () => {
    expect(headerFor('_modifiedby_value@Microsoft.Dynamics.CRM.lookuplogicalname')).toBeNull();
  });

  it('names a formatted value after its field, not after the annotation', () => {
    expect(headerFor('statecode@OData.Community.Display.V1.FormattedValue')).toBe(
      'statecode (label)',
    );
    expect(
      headerFor('_al_tcmanagerid_value@OData.Community.Display.V1.FormattedValue'),
    ).toBe('_al_tcmanagerid_value (label)');
  });

  it('still drops the platform bookkeeping it always dropped', () => {
    for (const key of [
      'importsequencenumber',
      'overriddencreatedon',
      'timezoneruleversionnumber',
      'utcconversiontimezonecode',
      'versionnumber',
      'owneridtype',
      'owneridyominame',
    ]) {
      expect(headerFor(key), key).toBeNull();
    }
  });
});

describe('toSheet', () => {
  const record = {
    '@odata.etag': 'W/"123456"',
    al_duedate: '2026-09-23',
    al_clientname: 'A Client',
    statecode: 0,
    'statecode@OData.Community.Display.V1.FormattedValue': 'Active',
    _modifiedby_value: '00000000-0000-0000-0000-000000000001',
    '_modifiedby_value@Microsoft.Dynamics.CRM.lookuplogicalname': 'systemuser',
    '_modifiedby_value@OData.Community.Display.V1.FormattedValue': 'A Person',
    versionnumber: 99,
  };

  it('writes no heading containing an @', () => {
    const sheet = toSheet('Cases', [record]);
    for (const header of sheet.headers) {
      expect(header, header).not.toContain('@');
    }
  });

  it('keeps the field, its readable label and nothing else', () => {
    expect(toSheet('Cases', [record]).headers).toEqual([
      'al_duedate',
      'al_clientname',
      'statecode',
      'statecode (label)',
      '_modifiedby_value',
      '_modifiedby_value (label)',
    ]);
  });

  it('puts each value under its own heading', () => {
    const sheet = toSheet('Cases', [record]);
    const byHeader = new Map(sheet.headers.map((h, i) => [h, sheet.rows[0][i]]));

    expect(byHeader.get('al_duedate')).toBe('2026-09-23');
    expect(byHeader.get('statecode')).toBe(0);
    expect(byHeader.get('statecode (label)')).toBe('Active');
    expect(byHeader.get('_modifiedby_value')).toBe('00000000-0000-0000-0000-000000000001');
    expect(byHeader.get('_modifiedby_value (label)')).toBe('A Person');
  });

  it('reads a label column from the annotation, not from a field of that name', () => {
    // The row has no key called 'statecode (label)'. If the value were looked up by the
    // heading rather than by the key it came from, this cell would come back empty.
    const sheet = toSheet('Cases', [record]);
    expect(sheet.rows[0][sheet.headers.indexOf('statecode (label)')]).toBe('Active');
  });

  it('gives an empty table a heading and says it is empty', () => {
    const sheet = toSheet('Sign-offs', []);

    expect(sheet.headers).toEqual(['Note']);
    expect(sheet.rows).toEqual([[EMPTY_SHEET_NOTE]]);
    expect(sheet.headers.length).toBeGreaterThan(0);
  });

  it('turns booleans into Yes and No, and leaves nested objects out', () => {
    const sheet = toSheet('Cases', [
      { al_taxrequired: true, al_aqsrequired: false, nested: { a: 1 }, al_name: 'x' },
    ]);

    expect(sheet.headers).toEqual(['al_taxrequired', 'al_aqsrequired', 'al_name']);
    expect(sheet.rows[0]).toEqual(['Yes', 'No', 'x']);
  });

  it('covers every column across rows that do not all carry the same fields', () => {
    const sheet = toSheet('Cases', [{ a: 1 }, { b: 2 }]);

    expect(sheet.headers).toEqual(['a', 'b']);
    expect(sheet.rows).toEqual([
      [1, ''],
      ['', 2],
    ]);
  });
});
