/*
 * The two value formatters every screen in the app reaches for.
 *
 * They lived in six and four copies respectively, which meant the house date format had six
 * definitions and a change to it was a six-file change no test would have caught if one were
 * missed. The signatures here are the widest of the copies (`string | null | undefined`),
 * because the Dataverse SDK returns an empty column as null and a missing one as undefined,
 * and both mean "nothing to show".
 */

/*
 * Built once, not per call. `toLocaleDateString` with an options object constructs a fresh
 * Intl.DateTimeFormat every time, and these run once per date per row: on a worklist of a
 * few thousand cases that was the hottest line in the render path.
 */
const UK_DAY = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Europe/London',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

// Spelled out because en-GB writes "Sept" in current browsers, and the portal writes "Sep".
export const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** A trimmed string, or null when there is nothing left after trimming. */
export function text(value: string | null | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

/**
 * A date as the app writes them ("07 Sep 2026"), or null when the value is absent or is not
 * a date. An unparseable value reads as blank rather than as "Invalid Date".
 */
export function date(value: string | null | undefined): string | null {
  if (!value) return null;
  const time = new Date(value).getTime();
  if (Number.isNaN(time)) return null;
  const [year, month, day] = UK_DAY.format(time).split('-');
  return `${day} ${MONTHS[Number(month) - 1]} ${year}`;
}
/** The UK clock time, 24-hour. Built once for the same reason as UK_DAY. */
const UK_CLOCK = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Europe/London',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

/**
 * A timestamp as the app writes it ("16 Nov 2026, 22:30"), on the UK clock, or an em dash
 * when the value is absent or is not a date. UK time rather than the browser's, so a time
 * lands on the same day the UK-day filters put it on wherever the reader is.
 */
export function dateTime(iso: string | null | undefined): string {
  const day = date(iso);
  return day && iso ? `${day}, ${UK_CLOCK.format(new Date(iso))}` : '—';
}

/**
 * The calendar day of an ISO value (`YYYY-MM-DD`), read from the digits so no time zone can
 * move it. `empty` is what an absent value reads as: blank in an export, a dash on a screen.
 */
export function isoDay(value: string | null | undefined, empty = ''): string {
  return value ? value.slice(0, 10) : empty;
}
