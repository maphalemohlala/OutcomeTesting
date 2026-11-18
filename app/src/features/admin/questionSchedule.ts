import { date } from '../../lib/format';
import { hasEnded, notStarted } from '../../lib/effectiveWindow';

/** The columns the rule reads, so a test needs no generated model. */
export interface VersionDates {
  _al_questionid_value?: string;
  al_versionnumber: number;
  al_effectivefrom?: string | null;
  al_effectiveto?: string | null;
}

export interface VersionAsOf<T> {
  /** What a reviewer answers on the day: the newest version already started. */
  version: T;
  /** A dated change or start still to come, in words, or null. Hides Edit until it lands. */
  scheduled: string | null;
  /** A planned retirement, in words, or null. Edit and Retire stay offered: the server carries it over an earlier edit, and Retire can move it. */
  retires: string | null;
}

/**
 * Each question's version as of a day. The newest version is not the answer: an edit dated
 * forward writes it now, and until that day reviewers still answer the one before.
 */
export function versionsAsOf<T extends VersionDates>(
  versions: readonly T[],
  asOf: Date,
): Map<string, VersionAsOf<T>> {
  const byQuestion = new Map<string, T[]>();
  for (const version of versions) {
    const questionId = version._al_questionid_value;
    if (!questionId) continue;
    byQuestion.set(questionId, [...(byQuestion.get(questionId) ?? []), version]);
  }

  const shown = new Map<string, VersionAsOf<T>>();
  for (const [questionId, held] of byQuestion) {
    const ordered = [...held].sort((a, b) => a.al_versionnumber - b.al_versionnumber);
    // Shown so an administrator sees a planned retirement before editing over it.
    const end = ordered[ordered.length - 1].al_effectiveto;
    const retires = end && !hasEnded(end, asOf) ? `Retires on ${date(end)}` : null;

    const started = ordered.filter((version) => !notStarted(version.al_effectivefrom, asOf));
    const version = started[started.length - 1];

    if (!version) {
      shown.set(questionId, {
        version: ordered[0],
        scheduled: `Starts on ${date(ordered[0].al_effectivefrom)}`,
        retires,
      });
      continue;
    }

    const next = ordered.find((other) => other.al_versionnumber > version.al_versionnumber);
    shown.set(questionId, {
      version,
      scheduled: next ? `Changes on ${date(next.al_effectivefrom)}` : null,
      retires,
    });
  }

  return shown;
}
