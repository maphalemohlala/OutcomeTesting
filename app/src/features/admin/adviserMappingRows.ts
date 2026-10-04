export interface AdviserMappingRow {
  id: string;
  adviserEmail: string;
  /** The contact with the adviser's email, for display and search; null when none has it. */
  adviserName: string | null;
  managerId: string | null;
  managerName: string | null;
  managerEmail: string | null;
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

/** The mapping rows the page renders, adviser and manager resolved against the contacts. */
export function toMappingRows(
  rows: readonly RawAdviserMapping[],
  contacts: readonly ContactOption[],
): AdviserMappingRow[] {
  return rows.map((row) => {
    const adviserEmail = (row.al_adviseremail ?? '').trim();
    const managerId = row._al_tcmanagerid_value ?? null;
    return {
      id: row.al_advisermappingid,
      adviserEmail,
      adviserName: nonBlank(contactByEmail(contacts, adviserEmail)?.name),
      managerId,
      managerName: managerNameFor(row, contacts),
      managerEmail: nonBlank(contactById(contacts, managerId)?.email),
    };
  });
}

/**
 * The rows whose adviser or manager matches the search, by name or email, ignoring case and
 * matching part of a word. A blank search keeps every row.
 */
export function filterMappings(
  rows: readonly AdviserMappingRow[],
  search: string,
): AdviserMappingRow[] {
  const needle = search.trim().toLowerCase();
  if (needle === '') return [...rows];

  return rows.filter((row) =>
    [row.adviserEmail, row.adviserName, row.managerName, row.managerEmail].some(
      (value) => (value ?? '').toLowerCase().includes(needle),
    ),
  );
}

function contactByEmail(contacts: readonly ContactOption[], email: string): ContactOption | undefined {
  const wanted = email.toLowerCase();
  return wanted === '' ? undefined : contacts.find((c) => c.email.trim().toLowerCase() === wanted);
}

function contactById(contacts: readonly ContactOption[], id: string | null): ContactOption | undefined {
  const wanted = (id ?? '').trim().toLowerCase();
  return wanted === '' ? undefined : contacts.find((c) => c.id.toLowerCase() === wanted);
}

function nonBlank(value: string | null | undefined): string | null {
  const text = (value ?? '').trim();
  return text === '' ? null : text;
}
