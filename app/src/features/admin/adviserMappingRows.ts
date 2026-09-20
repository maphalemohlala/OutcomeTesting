export interface AdviserMappingRow {
  id: string;
  adviserEmail: string;
  managerId: string | null;
  managerName: string | null;
}

export interface ContactOption {
  id: string;
  name: string;
  email: string;
}

/** The shape the Web API returns for an al_advisermapping row. */
export interface RawAdviserMapping {
  al_advisermappingid: string;
  al_adviseremail?: string | null;
  _al_tcmanagerid_value?: string | null;
  /**
   * Present only on a FetchXML/SDK read. The Web API expresses a lookup's label as the
   * annotation `_al_tcmanagerid_value@OData.Community.Display.V1.FormattedValue`, so on
   * this app's reads it is always undefined -- see `managerNameFor`.
   */
  al_tcmanageridname?: string | null;
  '_al_tcmanagerid_value@OData.Community.Display.V1.FormattedValue'?: string | null;
}

const FORMATTED = '_al_tcmanagerid_value@OData.Community.Display.V1.FormattedValue';

/**
 * The T&C Manager's name for one mapping row, or null when no manager is mapped.
 *
 * The page read `al_tcmanageridname` alone, which the Web API does not return, so every
 * mapping rendered as "No manager chosen" however it was set (F16, DEV, 2026-09-20). The
 * lookup id was always there and the edit dialog resolved it correctly, which is why the
 * table and the dialog disagreed about the same row.
 *
 * The id is resolved against the contacts the page has already loaded, so the name shown in
 * the table is the one the picker offers. The annotation and the SDK-style field are still
 * read first when present, so a read that does supply a label keeps using it.
 */
export function managerNameFor(
  row: RawAdviserMapping,
  contacts: readonly ContactOption[],
): string | null {
  const labelled = (row[FORMATTED] ?? row.al_tcmanageridname ?? '').trim();
  if (labelled !== '') return labelled;

  const managerId = (row._al_tcmanagerid_value ?? '').trim();
  if (managerId === '') return null;

  const contact = contacts.find((c) => c.id.toLowerCase() === managerId.toLowerCase());
  const name = (contact?.name ?? '').trim();
  return name === '' ? null : name;
}

/** The mapping rows the page renders, manager names resolved. */
export function toMappingRows(
  rows: readonly RawAdviserMapping[],
  contacts: readonly ContactOption[],
): AdviserMappingRow[] {
  return rows.map((row) => ({
    id: row.al_advisermappingid,
    adviserEmail: (row.al_adviseremail ?? '').trim(),
    managerId: row._al_tcmanagerid_value ?? null,
    managerName: managerNameFor(row, contacts),
  }));
}
