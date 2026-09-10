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
const DATE_FORMAT = new Intl.DateTimeFormat('en-GB', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
});

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
  return DATE_FORMAT.format(time);
}
