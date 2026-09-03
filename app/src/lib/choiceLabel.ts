/**
 * A single-record or list read returns the numeric option value but not the `*name`
 * formatted value for choice columns, so labels come from the generated choice maps.
 * The formatted name is still preferred when the platform does supply it.
 *
 * Every screen that shows a choice column has to go through here. A `*name ?? String(value)`
 * fallback looks equivalent and is not: on the reads where Dataverse omits the formatted
 * value it puts "120910770" on the screen where "Draft" belongs, and a person has no way to
 * translate it. Returning null instead lets the caller choose an honest placeholder.
 *
 * Lives in lib/ rather than beside one feature because cases, reports and admin all read
 * choice columns, and a second copy of this rule is how they start disagreeing.
 */
export function choiceLabel(
  map: Record<number, string>,
  value: number | undefined,
  formatted: string | undefined,
): string | null {
  const trimmed = formatted?.trim();
  if (trimmed) return trimmed;
  return value == null ? null : (map[value] ?? null);
}
