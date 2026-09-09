import { describe, expect, it } from 'vitest';
import type { Al_responses } from '../../generated/models/Al_responsesModel';
import { answerOf, noteOf } from './reviewAnswer';

/**
 * The SDK returns an empty column as null, not undefined, so a text answer arrives with
 * al_answerchoice: null. Read as "a choice was made", that showed "Not answered" with the
 * typed answer demoted to a note on the Fail observation of the DEV Tax review.
 */
function response(fields: Record<string, unknown>): Al_responses {
  return {
    al_responseid: 'ea990aca-b7ab-f111-aaac-e4fade069307',
    al_answerchoice: null,
    al_answerchoices: null,
    al_answertext: null,
    al_answerdate: null,
    ...fields,
  } as unknown as Al_responses;
}

describe('answerOf', () => {
  it('reads a free-text answer when the choice columns are null', () => {
    const record = response({ al_answertext: 'Not enough informarmation' });
    expect(answerOf(record)).toBe('Not enough informarmation');
    expect(noteOf(record)).toBeNull();
  });

  it('reads a single choice by its label', () => {
    const record = response({ al_answerchoice: 120910300, al_answerchoicename: 'Pass' });
    expect(answerOf(record)).toBe('Pass');
  });

  it('keeps text beside a choice as a note', () => {
    const record = response({
      al_answerchoice: 120910301,
      al_answerchoicename: 'Fail',
      al_answertext: 'No evidence on file',
    });
    expect(answerOf(record)).toBe('Fail');
    expect(noteOf(record)).toBe('No evidence on file');
  });

  it('reads a multi-select answer as its labels when the single choice is null', () => {
    // The SDK deserialises the wire string "120910340,120910341" to a number array.
    const record = response({ al_answerchoices: [120910340, 120910341] });
    expect(answerOf(record)).toBe('LSA/LSDBA/TTFAC, Trust');
    expect(noteOf(record)).toBeNull();
  });

  it('reads a date answer when everything else is null', () => {
    expect(answerOf(response({ al_answerdate: '2026-09-08T00:00:00Z' }))).toBe('08 Sept 2026');
  });

  it('reports nothing when every answer column is null', () => {
    expect(answerOf(response({}))).toBeNull();
    expect(noteOf(response({}))).toBeNull();
  });
});
