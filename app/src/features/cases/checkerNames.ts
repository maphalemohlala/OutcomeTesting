/**
 * The Tax Checker and the AQS Checker on a case header (item 2, 2026-09-19).
 *
 * The client-side mirror of `CheckerNames` in the plug-in assembly, which is the authority:
 * the columns are stamped there by `al_AssignCase` and by a portal claim, and neither is
 * editable on either surface. Nothing here writes them — this decides what to SHOW, which is
 * the part the server has no opinion about.
 *
 * The case header used to carry one checker name, and a case taking both a Tax check and an
 * AQS check has two checkers: whichever was allocated last overwrote the other, so the header
 * named one of them and gave no hint it had ever named the other.
 *
 * When one changes, change both — here and `CheckerNames.cs`.
 */

import type { ReviewRoute, ReviewType } from '../../types/domain';

/**
 * Why a discipline has no checker to show, which is not one state but three.
 *
 * `not-required` and `unallocated` look identical in the data — both are an empty column —
 * and mean opposite things to whoever is reading the case. The batch asked for "a sensible
 * empty state where a case has no review of that type", and a case that will never take a Tax
 * check has not been overlooked; a case awaiting allocation has.
 */
export type CheckerState =
  | { kind: 'named'; name: string }
  | { kind: 'not-required' }
  | { kind: 'unallocated' };

/** What each state reads as. Held here so the app and the portal word them identically. */
export const CHECKER_LABELS: Record<Exclude<CheckerState['kind'], 'named'>, string> = {
  'not-required': 'No check of this type',
  unallocated: 'Not yet allocated',
};

/**
 * Whether this case's route calls for a check of this discipline.
 *
 * Read from the route rather than from the review instances, because the worklist holds one
 * flat row per case and has no reviews to count. Where the route is unknown — every case
 * created before the route seed existed — the discipline is treated as required, so an
 * unallocated check reads as work outstanding rather than as work nobody owes. That is the
 * safe direction: it shows something that may not be needed, rather than hiding something
 * that is.
 */
export function routeRequires(route: ReviewRoute | null, type: ReviewType): boolean {
  if (route === null) return true;
  return type === 'Tax' ? route !== 'AQS only' : route !== 'Tax only';
}

/**
 * What to show for one discipline's checker on a case.
 *
 * `name` is the stamped column; `route` decides which empty state an absent one means.
 */
export function checkerState(
  name: string | null | undefined,
  route: ReviewRoute | null,
  type: ReviewType,
): CheckerState {
  const named = (name ?? '').trim();
  if (named.length > 0) return { kind: 'named', name: named };

  return routeRequires(route, type) ? { kind: 'unallocated' } : { kind: 'not-required' };
}

/** The state as one string, for a table cell or a printed header. */
export function checkerLabel(
  name: string | null | undefined,
  route: ReviewRoute | null,
  type: ReviewType,
): string {
  const state = checkerState(name, route, type);
  return state.kind === 'named' ? state.name : CHECKER_LABELS[state.kind];
}
