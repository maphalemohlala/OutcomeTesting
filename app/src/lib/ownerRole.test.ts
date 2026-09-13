import { describe, expect, it } from 'vitest';
// Imported as text rather than read with node:fs, for the reason protectedQuestions.test.ts
// gives: tsconfig.app.json restricts ambient types to vite/client on purpose.
import responseRulesSource from '../../../plugins/OutcomeTesting.Plugins/ResponseRules.cs?raw';
import sectionRulesSource from '../../../plugins/OutcomeTesting.Plugins/SectionRules.cs?raw';
import outcomeRulesSource from '../../../plugins/OutcomeTesting.Plugins/OutcomeRules.cs?raw';
import {
  OWNER_ROLE_AQS,
  OWNER_ROLE_BOTH,
  OWNER_ROLE_LABEL,
  OWNER_ROLE_TAX,
  ownerRoleForReviewType,
} from './ownerRole';

/** The int a `public const int <name> = <value>;` declaration carries, or null. */
function constantIn(source: string, name: string): number | null {
  const match = source.match(new RegExp(`const\\s+int\\s+${name}\\s*=\\s*(\\d+)\\s*;`));
  return match ? Number(match[1]) : null;
}

describe('ownerRole', () => {
  it('names each team', () => {
    expect(OWNER_ROLE_LABEL[OWNER_ROLE_TAX]).toBe('Tax');
    expect(OWNER_ROLE_LABEL[OWNER_ROLE_AQS]).toBe('AQS');
    expect(OWNER_ROLE_LABEL[OWNER_ROLE_BOTH]).toBe('Both');
  });

  it('reads a review type to the discipline that owns its sections', () => {
    expect(ownerRoleForReviewType('Tax')).toBe(OWNER_ROLE_TAX);
    expect(ownerRoleForReviewType('AQS')).toBe(OWNER_ROLE_AQS);
  });

  it('returns null for a review type no review is opened as', () => {
    // Both owns sections; it is not a discipline, so no review reads its sections "as" Both.
    expect(ownerRoleForReviewType('Both')).toBeNull();
    expect(ownerRoleForReviewType('')).toBeNull();
  });
});

describe('the mirror matches the plug-in assembly', () => {
  it('carries the same option values the C# declares', () => {
    // Read from the C# rather than restated, because a hand-copied option value goes stale
    // silently: a wrong one here filters a whole team's sections off the form while the
    // submit gate still demands their answers.
    expect(constantIn(responseRulesSource, 'OwnerRoleTaxTeam')).toBe(OWNER_ROLE_TAX);
    expect(constantIn(responseRulesSource, 'OwnerRoleAqsChecker')).toBe(OWNER_ROLE_AQS);
    expect(constantIn(sectionRulesSource, 'OwnerRoleBoth')).toBe(OWNER_ROLE_BOTH);
  });

  it('maps review type to owner role the way OutcomeRules does', () => {
    // TryOwnerRoleForReviewType answers for Tax and AQS and refuses everything else; the
    // client mirror must refuse the same set, or a review type with no discipline would be
    // read against whichever role happened to be first.
    const body = outcomeRulesSource.slice(
      outcomeRulesSource.indexOf('TryOwnerRoleForReviewType'),
    );
    expect(body).toContain('ReviewTypeTax');
    expect(body).toContain('ReviewTypeAqs');
    expect(body.slice(0, body.indexOf('}\n'))).toContain('ownerRole = 0');
  });
});
