/**
 * A single-record or list read returns the numeric option value but not the `*name`
 * formatted value for choice columns, so labels come from the generated choice maps.
 * The formatted name is still preferred when the platform does supply it.
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
