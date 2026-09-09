import {
  Al_responsesal_answerchoice,
  Al_responsesal_answerchoices,
  type Al_responses,
} from '../../generated/models/Al_responsesModel';

export function text(value: string | null | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

export function date(value: string | null | undefined): string | null {
  if (!value) return null;
  const time = new Date(value).getTime();
  if (Number.isNaN(time)) return null;
  return new Date(time).toLocaleDateString('en-GB', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

/**
 * The reviewer's answer, whichever response field the question type populated. The SDK
 * returns an empty column as null, not undefined, so "no choice" is tested with != null: a
 * null choice read as a choice hid every text and multi-select answer behind "Not answered".
 */
export function answerOf(record: Al_responses): string | null {
  if (record.al_answerchoice != null) {
    return (
      record.al_answerchoicename ?? Al_responsesal_answerchoice[record.al_answerchoice] ?? null
    );
  }
  if (record.al_answerchoices?.length) {
    return record.al_answerchoices
      .map((value) => Al_responsesal_answerchoices[value] ?? String(value))
      .join(', ');
  }
  if (text(record.al_answertext)) return text(record.al_answertext);
  return date(record.al_answerdate);
}

/** A free-text note kept alongside a structured answer (e.g. evidence for a Fail). */
export function noteOf(record: Al_responses): string | null {
  const structured = record.al_answerchoice != null || !!record.al_answerchoices?.length;
  return structured ? text(record.al_answertext) : null;
}
