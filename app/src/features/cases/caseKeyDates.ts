import { workingDaysSince } from '../../lib/workingDays';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** "23 Sep 2026", as lib/format's date() writes it, to a day number; null for anything else. */
function dayOf(shown: string): number | null {
  const match = /^(\d{1,2}) ([A-Z][a-z]{2}) (\d{4})$/.exec(shown.trim());
  const month = match ? MONTHS.indexOf(match[2]) : -1;
  if (!match || month < 0) return null;
  return Date.UTC(Number(match[3]), month, Number(match[1])) / 86_400_000;
}

export interface KeyDate {
  date: string;
  label: string;
}

interface ReviewDates {
  type: string;
  startedOn: string | null;
  submittedOn: string | null;
}

/**
 * The case's dates in the order they happened: each check started and submitted, and the due
 * date. A submission after the due date says by how many working days (weekends left out). Dates that cannot be read are
 * left out rather than guessed into place.
 */
export function caseKeyDates(reviews: readonly ReviewDates[], dueDate: string | null): KeyDate[] {
  const due = dueDate ? dayOf(dueDate) : null;
  const dates: (KeyDate & { day: number })[] = [];
  const add = (date: string | null, label: string) => {
    const day = date ? dayOf(date) : null;
    if (date && day !== null) dates.push({ date, label, day });
  };

  for (const review of reviews) {
    add(review.startedOn, `${review.type} check started`);
    const submitted = review.submittedOn ? dayOf(review.submittedOn) : null;
    const late =
      submitted !== null && due !== null
        ? workingDaysSince(new Date(due * 86_400_000), new Date(submitted * 86_400_000))
        : 0;
    add(
      review.submittedOn,
      late > 0
        ? `${review.type} check submitted, ${late} working day${late === 1 ? '' : 's'} after the due date`
        : `${review.type} check submitted`,
    );
  }
  add(dueDate, 'Case due');

  // Stable sort: on one day a check starting reads before the case falling due.
  return dates.sort((a, b) => a.day - b.day).map(({ date, label }) => ({ date, label }));
}
