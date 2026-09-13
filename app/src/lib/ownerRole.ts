import type { ReviewType } from '../types/domain';

/**
 * The three teams a section may be owned by (AD-020, AD-123), in one place.
 *
 * Mirrors `ResponseRules.OwnerRoleTaxTeam` / `OwnerRoleAqsChecker` and
 * `SectionRules.OwnerRoleBoth` in `plugins/OutcomeTesting.Plugins`, which are
 * authoritative — the submit gate and `ResponseGuardPlugin` decide what a review owes, and
 * these values only decide what the client asks for and what it draws.
 *
 * `ownerRole.test.ts` reads the C# and fails if the two drift apart, which is the pattern
 * `protectedQuestions.ts` already follows: a hand-copied option value goes stale silently,
 * and a stale one here would filter a whole team's sections off the form.
 */

/** The Tax team. */
export const OWNER_ROLE_TAX = 120910100;

/** The AQS checker. */
export const OWNER_ROLE_AQS = 120910101;

/**
 * A section owed by the Tax review and the AQS review alike. Each answers its own copy,
 * because responses hang off the review instance and not off the section.
 */
export const OWNER_ROLE_BOTH = 120910105;

/**
 * What to call each team. Consulted before the generated choice map, which predates Both
 * and renders it 'Unassigned'.
 */
export const OWNER_ROLE_LABEL: Record<number, string> = {
  [OWNER_ROLE_TAX]: 'Tax',
  [OWNER_ROLE_AQS]: 'AQS',
  [OWNER_ROLE_BOTH]: 'Both',
};

/**
 * The owner role a review of this discipline reads its sections against, or null for a
 * review type that is neither Tax nor AQS. Mirrors
 * `OutcomeRules.TryOwnerRoleForReviewType`; Both is never returned here, because it is a
 * section's owner and not a discipline a review is opened as.
 *
 * Overloaded so the two review types the solution actually opens answer as `number`: every
 * caller then has a role without a fallback that would quietly pick a team.
 */
export function ownerRoleForReviewType(reviewType: ReviewType): number;
export function ownerRoleForReviewType(reviewType: string): number | null;
export function ownerRoleForReviewType(reviewType: string): number | null {
  if (reviewType === 'Tax') return OWNER_ROLE_TAX;
  if (reviewType === 'AQS') return OWNER_ROLE_AQS;
  return null;
}
