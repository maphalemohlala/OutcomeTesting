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
  dueDate?: string | null,
): string | null {
  const day = (value ?? '').trim();
  if (day.length === 0) return null;

  if (day > today) return `${ADVICE_DATE_LABEL} cannot be in the future.`;

  // A meeting cannot be later than the day the case fell due (item 8, 2026-09-19). The
  // project owner settled this as ONE comparison: the batch asked for "not later than the
  // Submission date, and not later than the Due date", and there is no submission date on the
  // case - the due date is what was meant.
  //
  // Ordered after the future check, and only one message comes back, because a date beyond
  // today is usually a typo in the year and naming the due date would send the user to look
  // at the wrong field. CaseHeaderRules orders them the same way.
  const due = (dueDate ?? '').slice(0, 10).trim();
  if (due.length > 0 && day > due) {
    return `${ADVICE_DATE_LABEL} cannot be later than the due date (${humanDay(due)}).`;
  }

  return null;
}

/**
 * `yyyy-MM-dd` as the refusal reads it, e.g. "10 Jan 2026" - the same wording
 * CaseHeaderRules builds server-side, so the two messages are one message.
 *
 * Formatted in UTC deliberately: the value is a plain day, and letting the browser's zone
 * interpret its midnight would move it by one in either direction.
 */
function humanDay(day: string): string {
  const parsed = new Date(`${day}T00:00:00Z`);
  if (Number.isNaN(parsed.getTime())) return day;
  return new Intl.DateTimeFormat('en-GB', {
    timeZone: 'UTC',
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  }).format(parsed);
}
