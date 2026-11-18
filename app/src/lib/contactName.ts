/** The name columns of a contact, as every screen that lists people reads them. */
export interface NamedContact {
  fullname?: string | null;
  firstname?: string | null;
  lastname?: string | null;
  emailaddress1?: string | null;
}

/** The columns `contactName` reads, for a select. */
export const CONTACT_NAME_COLUMNS = ['fullname', 'firstname', 'lastname', 'emailaddress1'];

/**
 * The display name for a contact, the one rule every people list uses.
 *
 * `fullname` is a calculated column, so it is present on a read but absent from anything
 * the app writes. Falling back through the parts it is calculated from means a contact
 * created in the same session still shows a name, and a contact with neither resolves to
 * its email rather than dropping out of a list or showing an empty cell.
 */
export function contactName(contact: NamedContact): string {
  const full = contact.fullname?.trim();
  if (full) return full;

  const parts = [contact.firstname?.trim(), contact.lastname?.trim()].filter(Boolean);
  if (parts.length > 0) return parts.join(' ');

  return contact.emailaddress1?.trim() ?? '';
}
