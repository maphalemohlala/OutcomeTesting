import { describe, expect, it } from 'vitest';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';
import {
  adviceQualityFailedFrom,
  derivedFlags,
  effectiveAccountability,
  isRecorded,
  NOBODY,
  recordedFlags,
} from './failAccountability';

/**
 * The client-side mirror of GenerateExportPlugin.IsAccountable (item 8, 2026-09-19).
 *
 * These four flags become Trail Light columns 11-14 and 17-20, so the page and the export
 * have to agree about what a row means. Where they disagree the screen tells a reader one
 * thing and the file sent to the business says another.
 */

const outcome = (over: Partial<Al_outcomes> = {}) => ({ ...over }) as Al_outcomes;

describe('recordedFlags', () => {
  it('reads an untouched row as nobody', () => {
    expect(recordedFlags(outcome())).toEqual(NOBODY);
  });

  it('treats an absent flag as false rather than unknown', () => {
    // Dataverse omits a false boolean on some reads, so undefined must not read as true.
    expect(recordedFlags(outcome({ al_fqparaplanneraccountable: undefined }))).toEqual(NOBODY);
  });

  it('reads the flags the row holds', () => {
    expect(
      recordedFlags(
        outcome({ al_fqparaplanneraccountable: true, al_aqadviseraccountable: true }),
      ),
    ).toEqual({ fqAdviser: false, fqParaplanner: true, aqAdviser: true, aqParaplanner: false });
  });
});

describe('isRecorded', () => {
  it('is false when nobody has been named', () => {
    expect(isRecorded(NOBODY)).toBe(false);
  });

  it('is true as soon as any one flag is set', () => {
    // Four falses is "nobody has said yet", not "nobody is responsible" - which is why one
    // tick anywhere takes the whole row out of the derived default.
    expect(isRecorded({ ...NOBODY, fqAdviser: true })).toBe(true);
    expect(isRecorded({ ...NOBODY, aqParaplanner: true })).toBe(true);
  });
});

describe('derivedFlags', () => {
  it('gives a File Quality fail to the paraplanner, who compiles the file', () => {
    expect(derivedFlags(true, false)).toEqual({
      fqAdviser: false,
      fqParaplanner: true,
      aqAdviser: false,
      aqParaplanner: false,
    });
  });

  it('gives an Advice Quality fail to the adviser, who gives the advice', () => {
    expect(derivedFlags(false, true)).toEqual({
      fqAdviser: false,
      fqParaplanner: false,
      aqAdviser: true,
      aqParaplanner: false,
    });
  });

  it('never derives the adviser for the file or the paraplanner for the advice', () => {
    // A judgement can still name either; the DEFAULT never does.
    const both = derivedFlags(true, true);

    expect(both.fqAdviser).toBe(false);
    expect(both.aqParaplanner).toBe(false);
  });

  it('names nobody on a clean case', () => {
    expect(derivedFlags(false, false)).toEqual(NOBODY);
  });
});

describe('effectiveAccountability', () => {
  it('lets a recorded judgement win outright', () => {
    // Even one that contradicts the default: naming the adviser for a file quality fail is
    // exactly the override this screen exists to make.
    const recorded = { ...NOBODY, fqAdviser: true };

    const result = effectiveAccountability(recorded, true, true);

    expect(result.source).toBe('recorded');
    expect(result.flags).toEqual(recorded);
  });

  it('derives only an untouched row', () => {
    const result = effectiveAccountability(NOBODY, true, false);

    expect(result.source).toBe('derived');
    expect(result.flags.fqParaplanner).toBe(true);
  });

  it('does not quietly add the derived pair to a recorded one', () => {
    // The recorded row says the adviser carries the file. Deriving on top would put the
    // paraplanner back and attribute the fail to two people.
    const result = effectiveAccountability({ ...NOBODY, fqAdviser: true }, true, false);

    expect(result.flags.fqParaplanner).toBe(false);
  });
});

describe('adviceQualityFailedFrom', () => {
  it('reads a bare Pass as clean', () => {
    expect(adviceQualityFailedFrom('Pass')).toBe(false);
  });

  it('reads every other grade on the scale as a fail', () => {
    // OutcomeRules.RequiresRemediation: anything but a Pass. "Pass with issues" is one of
    // them, which is the case a substring test would get wrong.
    expect(adviceQualityFailedFrom('Pass with issues')).toBe(true);
    expect(adviceQualityFailedFrom('Insufficient evidence')).toBe(true);
    expect(adviceQualityFailedFrom('Potential harm')).toBe(true);
  });

  it('ignores case and surrounding space', () => {
    expect(adviceQualityFailedFrom('  pass  ')).toBe(false);
  });

  it('treats an ungraded check as no fail', () => {
    expect(adviceQualityFailedFrom(null)).toBe(false);
    expect(adviceQualityFailedFrom('')).toBe(false);
    expect(adviceQualityFailedFrom('—')).toBe(false);
  });
});
