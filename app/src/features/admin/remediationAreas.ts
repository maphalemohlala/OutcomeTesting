/** The shape the library's question rows already have; only what pairing reads. */
interface PairableQuestion {
  wording: string;
  scheduled: string | null;
  retires: string | null;
  movedTo: string | null;
}

export interface RemediationArea<Q> {
  area: string;
  required: Q;
  comments: Q;
}

const REQUIRED = /^(.+?):\s*remediation required\??\s*$/i;
const COMMENTS = /^(.+?):\s*remediation comments?\s*$/i;

/**
 * Splits a section's questions into ordinary rows and remediation areas: an "<Area>: remediation
 * required?" question and its "<Area>: remediation comments" partner become one area row.
 *
 * A half pair stays an ordinary row, and so does a pair where either question has a dated change,
 * a planned retirement or a pending move: those states are shown on the ordinary row only.
 */
export function splitRemediationAreas<Q extends PairableQuestion>(
  questions: readonly Q[],
): { rows: Q[]; areas: RemediationArea<Q>[] } {
  const key = (area: string) => area.trim().toLowerCase();
  const required = new Map<string, Q>();
  const comments = new Map<string, Q>();
  for (const q of questions) {
    const r = REQUIRED.exec(q.wording);
    const c = COMMENTS.exec(q.wording);
    if (r && !required.has(key(r[1]))) required.set(key(r[1]), q);
    else if (c && !comments.has(key(c[1]))) comments.set(key(c[1]), q);
  }

  const settled = (q: Q) => !q.scheduled && !q.retires && !q.movedTo;
  const areas: RemediationArea<Q>[] = [];
  const paired = new Set<Q>();
  for (const [k, req] of required) {
    const com = comments.get(k);
    if (!com || !settled(req) || !settled(com)) continue;
    areas.push({ area: REQUIRED.exec(req.wording)![1].trim(), required: req, comments: com });
    paired.add(req);
    paired.add(com);
  }

  return { rows: questions.filter((q) => !paired.has(q)), areas };
}
