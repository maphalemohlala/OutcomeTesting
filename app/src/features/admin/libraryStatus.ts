/**
 * Whether a row of checklist content is in force, and what to call the team that owns it
 * (AD-123). Mirrors `SectionRules` and `ResponseRules` in the plug-in assembly, which are
 * authoritative; this copy decides what the Question library shows, never what is allowed.
 *
 * Its own module rather than part of `useQuestionLibrary` so it can be tested: that hook
 * imports the generated services, which do not resolve under the test runner.
 */

/** The three teams a section may be owned by. Mirrors SectionRules.OwnerRoleBoth and friends. */
export const OWNER_ROLE_LABEL: Record<number, string> = {
  120910100: 'Tax',
  120910101: 'AQS',
  120910105: 'Both',
};

/** The UTC day a date-only column or timestamp falls on, or null when it is not set. */
function dayOf(value: string | Date | null | undefined): number | null {
  if (value === undefined || value === null) return null;
  const parsed = value instanceof Date ? value : new Date(value);
  const time = parsed.getTime();
  if (Number.isNaN(time)) return null;
  return Date.UTC(parsed.getUTCFullYear(), parsed.getUTCMonth(), parsed.getUTCDate());
}

/**
 * In force from the start of the effective-from day until the start of the effective-to day
 * — the same window a question version uses, applied to sections as well (AD-091, AD-123).
 * A retired row stays Active and keeps its answers; it is simply no longer asked.
 */
export function inForce(
  effectiveFrom: string | Date | null | undefined,
  effectiveTo: string | Date | null | undefined,
  asOf: Date,
): boolean {
  const day = dayOf(asOf);
  if (day === null) return true;

  const from = dayOf(effectiveFrom);
  if (from !== null && from > day) return false;

  const to = dayOf(effectiveTo);
  if (to !== null && to <= day) return false;

  return true;
}
