const FORMATTED_VALUE = '@OData.Community.Display.V1.FormattedValue';

/**
 * Resolves the display name of a lookup column. Dataverse returns it as an OData
 * annotation on the `_<attribute>_value` property; the generated `<attribute>name`
 * field is only populated on some reads, so it is used as the fallback.
 */
export function lookupLabel(record: object, attribute: string, name?: string): string | null {
  const annotation = (record as Record<string, unknown>)[`_${attribute}_value${FORMATTED_VALUE}`];
  const fromAnnotation = typeof annotation === 'string' ? annotation.trim() : '';
  if (fromAnnotation) return fromAnnotation;
  const fromName = name?.trim() ?? '';
  return fromName ? fromName : null;
}
