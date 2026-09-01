/*
 * OData query-building helpers.
 *
 * Dataverse filters are assembled as strings and OData has no parameter binding, so any
 * value reaching a filter from outside the app — a route parameter, an uploaded file, a
 * form field — is untrusted input. A raw interpolation lets a crafted value close the
 * clause and append its own, and breaks legitimately on an apostrophe in a client name.
 * Every interpolated value goes through one of these two helpers.
 */

/** Matches a canonical Dataverse record id. Deliberately strict: no braces, no partial ids. */
const RECORD_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * True when `value` is a well-formed record id and therefore safe to interpolate into a
 * filter unquoted. Guard a lookup filter with this rather than escaping it: an id that is
 * not a GUID is a bad request, not a value to sanitise.
 */
export function isRecordId(value: string | undefined | null): value is string {
  return typeof value === 'string' && RECORD_ID.test(value.trim());
}

/**
 * Escapes a string literal for an OData filter by doubling embedded apostrophes. The
 * result still has to be wrapped in single quotes at the call site.
 */
export function odataEscape(value: string): string {
  return value.replace(/'/g, "''");
}
