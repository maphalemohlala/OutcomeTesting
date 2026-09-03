/**
 * Working-day arithmetic for the BR-010 remediation clock (OD-018, resolved 2026-08-30).
 *
 * The settled rules:
 * - Working week is Monday to Friday.
 * - The clock runs in UK local time (Europe/London), so the day boundary moves with BST.
 *   Dataverse returns UTC, and a timestamp late on a British Summer Time evening belongs to
 *   the next UK day; taking the UTC date would age those cases a day short.
 * - The day a case enters remediation is day 1, not day 0.
 * - Bank holidays are NOT excluded. OD-018 settled the holiday set as England's but directed
 *   that it be applied manually rather than encoded, so this counts every Monday to Friday.
 *   The consequence is deliberate and worth knowing: a remediation spanning Christmas or
 *   Easter reads older here than it truly is, so the 10-day threshold can flag a case that
 *   has not really breached. Introducing a maintained holiday list is the fix; until then a
 *   breach near a bank holiday wants a human look.
 */

/** BR-010: remediation is expected to complete within ten working days. */
export const REMEDIATION_THRESHOLD_WORKING_DAYS = 10;

const UK_TIME_ZONE = 'Europe/London';

/**
 * The calendar date in UK local time, as {y, m, d}. Uses Intl rather than getDate() so the
 * result does not depend on the machine's own time zone — a browser in another region would
 * otherwise bucket cases against a different midnight.
 */
function ukParts(value: Date): { y: number; m: number; d: number } {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: UK_TIME_ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(value);

  const get = (type: string): number =>
    Number(parts.find((part) => part.type === type)?.value ?? '0');

  return { y: get('year'), m: get('month'), d: get('day') };
}

/**
 * A UK calendar date pinned to midday UTC. Midday keeps the date stable under both GMT and
 * BST, so day arithmetic never slips across a boundary.
 */
function ukMidday(value: Date): Date {
  const { y, m, d } = ukParts(value);
  return new Date(Date.UTC(y, m - 1, d, 12, 0, 0));
}

/** Monday to Friday. Saturday and Sunday are not working days. */
export function isWorkingDay(value: Date): boolean {
  const day = ukMidday(value).getUTCDay();
  return day >= 1 && day <= 5;
}

/**
 * Working days elapsed from `start` up to and including `end`, counting the start day as
 * day 1 (OD-018). Returns 0 when `end` falls before `start`.
 *
 * Counting inclusively is what makes "day 1" true: a case raised and chased the same
 * working morning is on day 1, not day 0.
 */
export function workingDaysBetween(start: Date, end: Date): number {
  const from = ukMidday(start);
  const to = ukMidday(end);
  if (to.getTime() < from.getTime()) return 0;

  let count = 0;
  const cursor = new Date(from.getTime());
  while (cursor.getTime() <= to.getTime()) {
    if (isWorkingDay(cursor)) count += 1;
    cursor.setUTCDate(cursor.getUTCDate() + 1);
  }

  return count;
}

/**
 * The working-day age of an ISO timestamp as at `now`, or 0 when the value is missing or
 * unparseable. This is the remediation clock: it replaces the provisional calendar-day
 * count OD-018 required be labelled as such.
 */
export function workingDayAge(value: string | undefined, now: Date = new Date()): number {
  if (!value) return 0;
  const then = new Date(value);
  if (Number.isNaN(then.getTime())) return 0;
  return workingDaysBetween(then, now);
}

/**
 * `start` advanced by `count` working days, counting the start day as day 1 to match
 * workingDaysBetween. Ten working days from a Monday is therefore the Friday of the
 * following week, which is the date the BR-010 threshold falls due.
 */
export function addWorkingDays(start: Date, count: number): Date {
  const cursor = ukMidday(start);
  if (count <= 0) return cursor;

  let remaining = count;
  if (isWorkingDay(cursor)) remaining -= 1;

  while (remaining > 0) {
    cursor.setUTCDate(cursor.getUTCDate() + 1);
    if (isWorkingDay(cursor)) remaining -= 1;
  }

  // Land on a working day even when the start was a weekend and count consumed nothing.
  while (!isWorkingDay(cursor)) {
    cursor.setUTCDate(cursor.getUTCDate() + 1);
  }

  return cursor;
}

/** Whether a remediation raised at `value` has passed the BR-010 ten-working-day threshold. */
export function hasBreachedRemediationThreshold(
  value: string | undefined,
  now: Date = new Date(),
): boolean {
  if (!value) return false;
  return workingDayAge(value, now) > REMEDIATION_THRESHOLD_WORKING_DAYS;
}

/** The clock as it stands on one remediation action (OD-018). */
export interface RemediationClock {
  /** Working days in the period now running. The BR-010 threshold measures this one. */
  current: number;
  /** Working days in the period a rejected sign-off ended, or null if never reset. */
  previous: number | null;
  /** Both periods added, for a "total time in remediation" reading. */
  total: number;
  /** True when a rejected sign-off has restarted the clock. */
  wasReset: boolean;
  /** Whether the current period has passed the ten-working-day threshold. */
  breached: boolean;
}

/** The columns the clock is read from. Anything holding these can be measured. */
export interface RemediationClockSource {
  createdon?: string;
  al_clockstartedon?: string;
  al_completedon?: string;
}

/**
 * The BR-010 remediation clock for one action (OD-018, resolved 2026-08-30).
 *
 * A rejected sign-off (BR-008) resets the clock, and the period it ended is preserved
 * rather than merged: `createdon` keeps the original start and `al_clockstartedon` holds
 * the current one, so a case that has been round twice reads as two timers instead of one
 * long age. Merging them is the thing to avoid — a second round would show as breached
 * before the adviser had had a day on it, and the ten-day threshold would be measuring
 * time that was never theirs.
 *
 * The clock stops at completion (PP-13 is "clock start and stop"), so a completed action
 * awaiting sign-off does not keep ageing while it sits with the T&C Manager.
 *
 * An action with no reset behaves exactly as before: one period from `createdon`.
 *
 * Only the most recent reset is on the action. A third round's earlier boundaries live on
 * the al_signoff rows, which is where OD-018 puts the audit trail; this reports the two
 * periods the columns can answer for and does not guess at the rest.
 */
export function remediationClock(
  action: RemediationClockSource,
  now: Date = new Date(),
): RemediationClock {
  const started = action.al_clockstartedon;
  const created = action.createdon;
  const stop = action.al_completedon ? new Date(action.al_completedon) : now;
  const stopAt = Number.isNaN(stop.getTime()) ? now : stop;

  const currentStart = started ?? created;
  const current = currentStart ? workingDaysBetween(new Date(currentStart), stopAt) : 0;

  let previous: number | null = null;
  if (started && created) {
    const from = new Date(created);
    const to = new Date(started);
    if (!Number.isNaN(from.getTime()) && !Number.isNaN(to.getTime()) && to.getTime() > from.getTime()) {
      previous = workingDaysBetween(from, to);
    }
  }

  return {
    current,
    previous,
    total: current + (previous ?? 0),
    wasReset: previous !== null,
    breached: current > REMEDIATION_THRESHOLD_WORKING_DAYS,
  };
}
