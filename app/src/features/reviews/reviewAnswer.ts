import {
  Al_responsesal_answerchoice,
  Al_responsesal_answerchoices,
  type Al_responses,
} from '../../generated/models/Al_responsesModel';
import { date, text } from '../../lib/format';



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
  const words = plainTextOf(record);
  if (words !== null) return words;
  return date(record.al_answerdate);
}

/**
 * The markup a Rich text question was answered with (item 7, 2026-09-19), or null.
 *
 * Safe to render as markup. It cannot reach the column unsanitised: ResponseGuardPlugin
 * cleans al_answerrichtext pre-operation on al_response itself, so every write is reduced
 * to HtmlSanitiser's allow-list whatever wrote it. This app never writes one - checkers
 * answer on the portal - so here it is only ever read back.
 */
export function richTextOf(record: Al_responses): string | null {
  const markup = record.al_answerrichtext;
  return typeof markup === 'string' && markup.trim() !== '' ? markup : null;
}

/**
 * The same answer as words, for the places that show text rather than markup - the full
 * extract, and anything counting an answer as present. Without this a rich-text answer
 * read as "Not answered" everywhere that asks answerOf for a string.
 */
export function plainTextOf(record: Al_responses): string | null {
  const markup = richTextOf(record);
  if (markup === null) return null;

  const words = markup
    .replace(/<[^>]*>/g, ' ')
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/\s+/g, ' ')
    .trim();

  return words === '' ? null : words;
}
