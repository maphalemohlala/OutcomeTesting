import { date } from '../../lib/format';
import { notStarted } from '../../lib/effectiveWindow';

/** The columns the rule reads, so a test needs no generated model. */
export interface VersionDates {
  _al_questionid_value?: string;
  al_versionnumber: number;
  al_effectivefrom?: string | null;
}

export interface VersionAsOf<T> {
  /** What a reviewer answers on the day: the newest version already started. */
  version: T;
  /** A dated change still to come, in words, or null. */
  scheduled: string | null;
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
    const started = ordered.filter((version) => !notStarted(version.al_effectivefrom, asOf));
    const version = started[started.length - 1];

    if (!version) {
      shown.set(questionId, {
        version: ordered[0],
        scheduled: `Starts on ${date(ordered[0].al_effectivefrom)}`,
      });
      continue;
    }

    const next = ordered.find((other) => other.al_versionnumber > version.al_versionnumber);
    shown.set(questionId, {
      version,
      scheduled: next ? `Changes on ${date(next.al_effectivefrom)}` : null,
    });
  }

  return shown;
}
