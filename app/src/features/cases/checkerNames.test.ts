import { describe, expect, it } from 'vitest';
import caseListTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-list/OT-Case-List.webtemplate.source.html?raw';
import caseDetailTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';
import { REVIEW_ROUTES } from '../../types/domain';
import { CHECKER_LABELS, checkerLabel, checkerState, routeRequires } from './checkerNames';

/**
 * The Tax Checker and the AQS Checker on a case header (item 2, 2026-09-19).
 *
 * The server side of this rule — which column each discipline stamps — is mirrored in
 * CheckerNamesTests.cs. What is tested here is the part the server has no opinion about:
 * which of the three states a case is in, and what each one reads as.
 */

describe('routeRequires', () => {
  it('asks for both checks on a Tax then AQS case', () => {
    expect(routeRequires('Tax then AQS', 'Tax')).toBe(true);
    expect(routeRequires('Tax then AQS', 'AQS')).toBe(true);
  });

  it('asks for only the Tax check on a Tax only case', () => {
    expect(routeRequires('Tax only', 'Tax')).toBe(true);
    expect(routeRequires('Tax only', 'AQS')).toBe(false);
  });

  it('asks for only the AQS check on an AQS only case', () => {
    expect(routeRequires('AQS only', 'Tax')).toBe(false);
    expect(routeRequires('AQS only', 'AQS')).toBe(true);
  });

  it('treats an unknown route as requiring both', () => {
    // Every case created before the route seed existed. Showing a check that may not be
    // needed is the safe direction; hiding one that is needed is not.
    expect(routeRequires(null, 'Tax')).toBe(true);
    expect(routeRequires(null, 'AQS')).toBe(true);
  });
});

describe('checkerState', () => {
  it('names the checker where one is allocated', () => {
    expect(checkerState('Ada Checker', 'Tax then AQS', 'Tax')).toEqual({
      kind: 'named',
      name: 'Ada Checker',
    });
  });

  it('trims a stamped name', () => {
    expect(checkerState('  Ada Checker  ', 'Tax then AQS', 'AQS')).toEqual({
      kind: 'named',
      name: 'Ada Checker',
    });
  });

  it('reads an empty column on a required check as not yet allocated', () => {
    expect(checkerState(null, 'Tax then AQS', 'Tax')).toEqual({ kind: 'unallocated' });
    expect(checkerState('', 'Tax then AQS', 'AQS')).toEqual({ kind: 'unallocated' });
    expect(checkerState('   ', 'Tax then AQS', 'Tax')).toEqual({ kind: 'unallocated' });
  });

  it('reads an empty column on a check the case does not take as not required', () => {
    // The distinction the batch asked for. Both are an empty column, and they mean opposite
    // things: a Tax only case has not been overlooked for AQS, it simply owes no AQS check.
    expect(checkerState(null, 'Tax only', 'AQS')).toEqual({ kind: 'not-required' });
    expect(checkerState(null, 'AQS only', 'Tax')).toEqual({ kind: 'not-required' });
  });

  it('names a checker even on a discipline the route does not ask for', () => {
    // The data wins over the route. A name in the column means somebody was allocated, and
    // hiding it because the route disagrees would hide the more surprising fact.
    expect(checkerState('Ada Checker', 'Tax only', 'AQS')).toEqual({
      kind: 'named',
      name: 'Ada Checker',
    });
  });
});

describe('checkerLabel', () => {
  it('reads as the name where there is one', () => {
    expect(checkerLabel('Ada Checker', 'Tax then AQS', 'Tax')).toBe('Ada Checker');
  });

  it('distinguishes the two empty states in words', () => {
    expect(checkerLabel(null, 'Tax then AQS', 'AQS')).toBe(CHECKER_LABELS.unallocated);
    expect(checkerLabel(null, 'Tax only', 'AQS')).toBe(CHECKER_LABELS['not-required']);
    expect(CHECKER_LABELS.unallocated).not.toBe(CHECKER_LABELS['not-required']);
  });
});

/**
 * The portal case list shows the same two columns, and decides the empty states the same
 * way — from the route, because a list holds one flat row per case and has no reviews to
 * count. It is Liquid, so it cannot import CHECKER_LABELS; it spells the two sentences out.
 *
 * This is the drift test for that (AD-041). It reads the template and fails when the
 * wording, the route names or the columns disagree with this module — because the two
 * surfaces saying different things about the same case is the failure nobody would spot
 * until a checker and a manager were reading different screens.
 */
describe('the portal case list agrees with this module', () => {
  it('words both empty states exactly as CHECKER_LABELS does', () => {
    expect(caseListTemplate).toContain(CHECKER_LABELS['not-required']);
    expect(caseListTemplate).toContain(CHECKER_LABELS.unallocated);
  });

  it('selects the two columns it renders', () => {
    // A template that renders a column it never selected shows every case as unallocated,
    // which reads as real data rather than as a missing attribute.
    expect(caseListTemplate).toContain('<attribute name="al_taxcheckername" />');
    expect(caseListTemplate).toContain('<attribute name="al_aqscheckername" />');
  });

  it('suppresses each discipline on exactly the route that does not owe it', () => {
    // The mirror of routeRequires. Tax is not owed on an AQS only case; AQS is not owed on
    // a Tax only case; anything else - including a case with no route - owes both.
    expect(caseListTemplate).toContain("rt_name == 'AQS only'");
    expect(caseListTemplate).toContain("rt_name == 'Tax only'");

    for (const route of REVIEW_ROUTES) {
      const suppressed = caseListTemplate.includes(`rt_name == '${route}'`);
      const owedByBoth = routeRequires(route, 'Tax') && routeRequires(route, 'AQS');
      expect(suppressed).toBe(!owedByBoth);
    }
  });

  it('keeps them as two columns', () => {
    // Merging them would reinstate the defect Fixes 2 removed: one column naming whichever
    // discipline was allocated second.
    expect(caseListTemplate).toContain('<th scope="col">Tax checker</th>');
    expect(caseListTemplate).toContain('<th scope="col">AQS checker</th>');
  });
});

/**
 * The wording being present in the template is not the same as the wording reaching the
 * page, and the tests above only ever proved the first.
 *
 * Verified against DEV on 2026-09-20: this site's Liquid takes the TRUE branch of
 * `{% if <null attribute> != blank %}` and then renders the null as nothing, so every
 * unallocated checker cell came out completely empty - no "Not yet allocated", no "No
 * check of this type", no markup at all. The drift tests passed throughout, because the
 * sentences were sitting in the else branches that were never reached.
 *
 * So the rule is about HOW emptiness is decided, not only about what it renders. Capture
 * coerces a null to the empty string before the comparison, which is the idiom the review
 * template already uses for a choice value, and which does not depend on `blank`.
 */
describe('the checker cells decide emptiness in a way that works on a null', () => {
  const surfaces: Array<[string, string]> = [
    ['the case list', caseListTemplate],
    ['case detail', caseDetailTemplate],
  ];

  for (const [name, template] of surfaces) {
    it(`does not test a checker name against blank in ${name}`, () => {
      // The defect itself. `!= blank` reads as "is set" and behaves as "is not set".
      expect(template).not.toContain('al_taxcheckername != blank');
      expect(template).not.toContain('al_aqscheckername != blank');
    });

    it(`compares a captured checker name against the empty string in ${name}`, () => {
      // The replacement, asserted positively so that deleting the branch does not pass.
      expect(template).toMatch(/\{%\s*capture tax_checker\s*%\}/);
      expect(template).toMatch(/\{%\s*capture aqs_checker\s*%\}/);
      expect(template).toContain("tax_checker != ''");
      expect(template).toContain("aqs_checker != ''");
    });
  }
});

/**
 * Fixing the blank cells exposed what was underneath them: case detail decided the two
 * empty states by counting review instances, so a Tax then AQS case whose AQS leg had not
 * been claimed yet read "No check of this type" on the case header while the case list
 * read "Not yet allocated" about the same case (DEV, 2026-09-20). A review row that does
 * not exist yet is not the same as a check nobody owes.
 *
 * Both surfaces decide from the route, with an existing review as further evidence.
 */
describe('case detail decides the empty states from the route', () => {
  it('reads the route requirement flags when deciding the header states', () => {
    expect(caseDetailTemplate).toContain('owes_tax');
    expect(caseDetailTemplate).toContain('owes_aqs');
    expect(caseDetailTemplate).toMatch(/owes_tax[\s\S]{0,400}al_requirestaxreview/);
    expect(caseDetailTemplate).toMatch(/owes_aqs[\s\S]{0,400}al_requiresaqsreview/);
  });

  it('treats an unknown route as owing both checks, as the case list does', () => {
    // The safe direction: show a check that may not be needed rather than hide one that is.
    expect(caseDetailTemplate).toMatch(/hdr_rt == null[\s\S]{0,200}owes_tax = true/);
  });
});
