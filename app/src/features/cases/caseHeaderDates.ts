/**
 * The dates a case header may carry, as pure functions over primitives.
 *
 * The client-side mirror of `CaseHeaderRules` in the plug-in assembly, which is the
 * authority: `al_UpdateCaseDetails` re-checks every rule here server-side, and
 * `CaseHeaderRequestPlugin` reaches the same check through the same `ApplyFields`, so the
 * portal and this app cannot enforce different rules. This exists so a bad date costs a
 * keystroke rather than a round trip that comes back refused (AD-041).
 *
 * When one changes, change both.
 */

/**
 * The label the business gives `al_advicedate` (item 9, 2026-09-19). The schema name is
 * unchanged - nothing that reads the column breaks - and this is the wording every surface
 * shows. Held here so the panel, the checklist header and the export cannot drift apart.
 */
export const ADVICE_DATE_LABEL = 'Date of meeting - Client contact';

/**
 * Today in the UK, as `yyyy-MM-dd`.
 *
 * Users are in the UK and the browser may not be: a machine set to another zone would
 * otherwise decide what "today" means, and on a British Summer Time evening even a UK
 * machine reading UTC would be a day behind. `en-CA` is used because it formats as
 * `YYYY-MM-DD`, which is the order a date input takes and the order that sorts.
 */
export function ukToday(now: Date = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/London',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(now);
}

/**
 * Why this date of meeting cannot be saved, or null when it can.
 *
 * A meeting that has not happened yet cannot have been checked, so the date can never be in
 * the future. Today itself is allowed: a meeting held this morning is checked this afternoon
 * often enough that refusing it would be refusing the ordinary case.
 *
 * Compared as `yyyy-MM-dd` strings, which is a date comparison and not a timestamp one -
 * `al_advicedate` is a DateOnly column, so there is no time of day in play, and building
 * `Date` objects here would put the browser's zone back into a question that is about the
 * UK's day.
 *
 * An empty value is valid: clearing the field is a legitimate edit.
 */
export function adviceDateRefusal(
  value: string | null | undefined,
  today: string = ukToday(),
): string | null {
  const day = (value ?? '').trim();
  if (day.length === 0) return null;

  return day > today ? `${ADVICE_DATE_LABEL} cannot be in the future.` : null;
}
