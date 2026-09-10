import type { ReviewRoute } from '../../types/domain';

/**
 * Which checks a case owes, and which of them can be allocated right now (BR-004).
 *
 * These rules exist server-side — UpdateCaseDetailsPlugin.DeriveRoute derives the route
 * from the Tax check required answer, and ClaimCasePlugin.NextDiscipline decides which
 * discipline a check may be opened for. The edit modal has to answer the same questions
 * before it saves, so the Tax Checker and AQS Checker fields respond to the dropdown
 * rather than only to what is already stored. They are mirrored here, with tests, so the
 * mirror cannot drift from the plug-ins without a test failing — the reason
 * remediationForm.ts exists for the remediation block.
 *
 * The commands remain the authority: al_UpdateCaseDetails re-derives the route from the
 * same answer, and al_AssignCase re-resolves which review it is opening.
 */

export type Discipline = 'Tax' | 'AQS';

/** Tax check required, as the deployed option set labels it. */
export type TaxAnswer = 'Yes' | 'No' | null;

/** Tax team disposition, as the deployed option set labels it. */
export type Disposition = 'Submit to AQS' | 'Return to paraplanner' | null;

/**
 * The route BR-004 derives from the Tax check required answer, or null where the answer
 * does not decide one. Mirrors UpdateCaseDetailsPlugin.DeriveRoute.
 */
export function routeForTaxAnswer(answer: TaxAnswer): ReviewRoute | null {
  if (answer === 'Yes') return 'Tax then AQS';
  if (answer === 'No') return 'AQS only';
  return null;
}

/**
 * The route the answers derive between them.
 *
 * The Tax team's disposition decides whether the case goes on to AQS at all: "Return to
 * paraplanner" means the Tax check is the whole of it and no AQS check is owed (project
 * owner, 2026-09-10), so it overrides the tax-check answer — that answer says a Tax check
 * was needed, not where the case goes afterwards.
 */
export function routeForAnswers(answer: TaxAnswer, disposition: Disposition): ReviewRoute | null {
  if (disposition === 'Return to paraplanner') return 'Tax only';
  return routeForTaxAnswer(answer);
}

/**
 * The route the modal should reason from: the derived one when the tax answer has been
 * changed or the case has no route yet, and the stored one otherwise.
 *
 * The condition is DeriveRoute's own: it re-derives only when the answer changed, or when
 * the case carries no route — so a case already routed Tax only is not silently rewritten
 * to Tax then AQS just because its tax answer reads Yes.
 */
export function effectiveRoute(
  storedRoute: ReviewRoute | null,
  answer: TaxAnswer,
  answerChanged: boolean,
  disposition: Disposition = null,
  dispositionChanged = false,
): ReviewRoute | null {
  if (!answerChanged && !dispositionChanged && storedRoute) return storedRoute;
  return routeForAnswers(answer, disposition) ?? storedRoute;
}

/** The checks a route requires, Tax first (BR-004 orders the legs). */
export function owedDisciplines(route: ReviewRoute | null): Discipline[] {
  switch (route) {
    case 'Tax only':
      return ['Tax'];
    case 'AQS only':
      return ['AQS'];
    case 'Tax then AQS':
      return ['Tax', 'AQS'];
    default:
      return [];
  }
}

/** One check as the modal needs to know it: does it exist, and is it finished. */
export interface CheckState {
  exists: boolean;
  submitted: boolean;
}

/**
 * The one discipline a check may be opened for now: the first the route owes whose review
 * has not been submitted. Mirrors ClaimCasePlugin.NextDiscipline.
 *
 * This is what stops the modal offering to allocate a discipline whose review does not
 * exist yet and is not next — al_AssignCase would open the earlier leg instead and put
 * that choice of person on the wrong check.
 */
export function nextOwedDiscipline(
  owed: Discipline[],
  stateOf: (discipline: Discipline) => CheckState,
): Discipline | null {
  return owed.find((discipline) => !stateOf(discipline).submitted) ?? null;
}

/**
 * Whether the modal may offer a checker control for this discipline.
 *
 * The route's ordering decides it, and nothing overrides that. Whether the review instance
 * already exists is deliberately not consulted: it used to be an exception - an open check
 * was offered wherever it sat in the route - and that is what made the AQS Checker control
 * appear on some Tax-then-AQS cases and not others (project owner, 2026-09-10). A case
 * routed AQS only, given an AQS review, and then answered Yes to Tax check required carries
 * an AQS instance while BR-004 still owes the Tax check first, so existence tracked how the
 * case had been edited rather than what it owed next.
 *
 * Reallocating a started check still works, which is the direction that exception was added
 * for: the check a case owes next is offered whether or not it has been opened, so an open
 * Tax check on a Tax-then-AQS case stays reallocatable. Only a check the route has not
 * reached is now withheld, and allocating one early is what would put the case on that
 * person's queue before it is theirs to do.
 */
export function isAllocatable(
  discipline: Discipline,
  state: CheckState,
  nextOwed: Discipline | null,
): boolean {
  // A submitted check is finished; al_AssignCase refuses it.
  if (state.submitted) return false;

  return discipline === nextOwed;
}
