import { describe, expect, it } from 'vitest';
import {
  effectiveRoute,
  isAllocatable,
  routeForAnswers,
  nextOwedDiscipline,
  owedDisciplines,
  routeForTaxAnswer,
  type CheckState,
  type Discipline,
} from './caseCheckers';

const none: CheckState = { exists: false, submitted: false };
const open: CheckState = { exists: true, submitted: false };
const done: CheckState = { exists: true, submitted: true };

const states = (map: Partial<Record<Discipline, CheckState>>) => (d: Discipline) => map[d] ?? none;

describe('route derived from the Tax check answer (BR-004)', () => {
  it('routes a Tax-required case through Tax then AQS', () => {
    expect(routeForTaxAnswer('Yes')).toBe('Tax then AQS');
  });

  it('routes a case that needs no Tax check to AQS only', () => {
    expect(routeForTaxAnswer('No')).toBe('AQS only');
  });

  it('derives nothing from an unanswered question', () => {
    expect(routeForTaxAnswer(null)).toBeNull();
  });
});

describe('which route the modal reasons from', () => {
  it('follows the answer as soon as it is changed, before the save', () => {
    // The complaint this exists for: changing Tax check required left the checker fields
    // showing the checks the stored route owed.
    expect(effectiveRoute('AQS only', 'Yes', true)).toBe('Tax then AQS');
  });

  it('keeps the stored route when the answer was not touched', () => {
    // DeriveRoute re-derives only on a change, so a case routed Tax only is not rewritten
    // to Tax then AQS just because its stored answer reads Yes.
    expect(effectiveRoute('Tax only', 'Yes', false)).toBe('Tax only');
  });

  it('derives a route for a case that has none yet', () => {
    expect(effectiveRoute(null, 'Yes', false)).toBe('Tax then AQS');
  });

  it('keeps the stored route when the answer decides nothing', () => {
    expect(effectiveRoute('AQS only', null, true)).toBe('AQS only');
  });
});

describe('the Tax team disposition (project owner, 2026-09-10)', () => {
  it('makes a returned case Tax only, so it owes no AQS check', () => {
    expect(routeForAnswers('Yes', 'Return to paraplanner')).toBe('Tax only');
    expect(owedDisciplines(routeForAnswers('Yes', 'Return to paraplanner'))).toEqual(['Tax']);
  });

  it('overrides the tax-check answer, which only says a Tax check was needed', () => {
    expect(routeForAnswers('No', 'Return to paraplanner')).toBe('Tax only');
  });

  it('leaves the tax-check answer deciding when the case goes on to AQS', () => {
    expect(routeForAnswers('Yes', 'Submit to AQS')).toBe('Tax then AQS');
    expect(routeForAnswers('No', 'Submit to AQS')).toBe('AQS only');
  });

  it('drops the AQS field as soon as the disposition is changed, before the save', () => {
    // The complaint this exists for: the AQS Checker field stayed after selecting
    // Return to paraplanner.
    const route = effectiveRoute('Tax then AQS', 'Yes', false, 'Return to paraplanner', true);
    expect(owedDisciplines(route)).toEqual(['Tax']);
  });

  it('brings the AQS leg back when the disposition is changed back', () => {
    const route = effectiveRoute('Tax only', 'Yes', false, 'Submit to AQS', true);
    expect(owedDisciplines(route)).toEqual(['Tax', 'AQS']);
  });
});

describe('the checks a route owes', () => {
  it('owes both legs on Tax then AQS, Tax first', () => {
    expect(owedDisciplines('Tax then AQS')).toEqual(['Tax', 'AQS']);
  });

  it('owes one leg on a single-discipline route', () => {
    expect(owedDisciplines('AQS only')).toEqual(['AQS']);
    expect(owedDisciplines('Tax only')).toEqual(['Tax']);
  });

  it('owes nothing without a route', () => {
    expect(owedDisciplines(null)).toEqual([]);
  });
});

describe('which check may be opened next (mirrors NextDiscipline)', () => {
  it('owes Tax first on a two-leg route', () => {
    expect(nextOwedDiscipline(['Tax', 'AQS'], states({}))).toBe('Tax');
  });

  it('moves to AQS once Tax is submitted', () => {
    expect(nextOwedDiscipline(['Tax', 'AQS'], states({ Tax: done }))).toBe('AQS');
  });

  it('still owes Tax while its check is merely open', () => {
    expect(nextOwedDiscipline(['Tax', 'AQS'], states({ Tax: open }))).toBe('Tax');
  });

  it('owes nothing once every leg is submitted', () => {
    expect(nextOwedDiscipline(['Tax', 'AQS'], states({ Tax: done, AQS: done }))).toBeNull();
  });
});

describe('what the modal may offer', () => {
  it('offers the owed check that is open, so a started check can be reallocated', () => {
    expect(isAllocatable('Tax', open, 'Tax')).toBe(true);
  });

  it('refuses a submitted check', () => {
    expect(isAllocatable('Tax', done, 'AQS')).toBe(false);
  });

  it('offers an unopened check only when it is the one owed next', () => {
    expect(isAllocatable('Tax', none, 'Tax')).toBe(true);
  });

  it('withholds an unopened check that is not next', () => {
    // al_AssignCase would open the Tax leg instead and put the AQS choice on it.
    expect(isAllocatable('AQS', none, 'Tax')).toBe(false);
  });

  it('withholds a check that is not next even once its review exists', () => {
    // The complaint this exists for (project owner, 2026-09-10): the AQS Checker control
    // appeared on some Tax-then-AQS cases and not others. Whether the AQS review instance
    // happened to exist yet was the whole of the difference - a case routed AQS only, given
    // an AQS review, and then answered Yes to Tax check required carries one, and the
    // control appeared while BR-004 still owed the Tax check first. Existence is no longer
    // an exception to the ordering, so the answer is the same either way.
    expect(isAllocatable('AQS', open, 'Tax')).toBe(false);
    expect(isAllocatable('AQS', none, 'Tax')).toBe(false);
  });

  it('offers AQS once the Tax check is submitted, opened or not', () => {
    expect(isAllocatable('AQS', none, 'AQS')).toBe(true);
    expect(isAllocatable('AQS', open, 'AQS')).toBe(true);
  });
});
