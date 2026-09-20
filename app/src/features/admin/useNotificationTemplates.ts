import { useEffect, useState } from 'react';
import { Al_notificationtemplatesService } from '../../generated';
import { logTechnical } from '../../services/errors';
import { TEMPLATE_CODES, templateHint } from './notificationTemplates';

export interface TemplateRow {
  /** Null for a letter that has no stored row and is running on the built-in wording. */
  id: string | null;
  code: string;
  name: string;
  subject: string;
  body: string;
  tokens: readonly string[];
  isHtml: boolean;
  /** True when the wording comes from a stored row rather than the assembly. */
  stored: boolean;
}

export type TemplatesState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; templates: TemplateRow[] };

/**
 * Every letter the solution sends, whether or not anyone has edited it.
 *
 * <p>
 * The list is driven by the CODE CATALOGUE, not by the table. A letter with no stored row is
 * still sent — on the wording compiled into the plug-in assembly — so listing only the rows
 * would hide most of the letters and make the page look like the whole set when it was not.
 * Each row says which it is.
 * </p>
 */
export function useNotificationTemplates(reloadKey = 0): TemplatesState {
  const [state, setState] = useState<TemplatesState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Al_notificationtemplatesService.getAll({ orderBy: ['al_templatecode asc'] })
      .then((result) => {
        if (cancelled) return;

        if (!result.success || !result.data) {
          logTechnical('notification templates load', result.error);
          setState({
            status: 'unavailable',
            reason: 'The notification wording could not be loaded right now.',
          });
          return;
        }

        const byCode = new Map(
          result.data
            .filter((r) => Number(r.statecode) === 0)
            .map((r) => [(r.al_templatecode ?? '').trim(), r]),
        );

        setState({
          status: 'ready',
          templates: TEMPLATE_CODES.map((code) => {
            const hint = templateHint(code)!;
            const row = byCode.get(code);
            return {
              id: row?.al_notificationtemplateid ?? null,
              code,
              name: hint.name,
              subject: row?.al_subject ?? '',
              body: row?.al_body ?? '',
              tokens: hint.tokens,
              isHtml: hint.isHtml,
              stored: row !== undefined,
            };
          }),
        });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('notification templates load', error);
        setState({
          status: 'unavailable',
          reason: 'The notification wording could not be loaded right now.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}

/**
 * Saves one letter's wording.
 *
 * <p>
 * The server refuses a template that would send a broken letter, and its refusal is the one
 * that counts — this returns whatever it said rather than paraphrasing it, so an
 * administrator sees the same sentence however they reached the table.
 * </p>
 */
export async function saveTemplate(
  row: TemplateRow,
  subject: string,
  body: string,
): Promise<{ ok: true } | { ok: false; reason: string }> {
  try {
    const result = row.id
      ? await Al_notificationtemplatesService.update(row.id, {
          al_subject: subject,
          al_body: body,
        })
      : await Al_notificationtemplatesService.create({
          al_name: row.name,
          al_templatecode: row.code,
          al_subject: subject,
          al_body: body,
          statecode: 0,
        });

    if (!result.success) {
      logTechnical('notification template save', result.error);
      return { ok: false, reason: refusalFrom(result.error) };
    }

    return { ok: true };
  } catch (error) {
    logTechnical('notification template save', error);
    return { ok: false, reason: refusalFrom(error) };
  }
}

/**
 * The plug-in's own sentence where there is one, and a plain fallback otherwise.
 *
 * A guard refusal is written for the person reading it and names the tokens the letter
 * supplies; replacing that with "could not save" would throw away the only part that tells
 * them what to do next.
 */
function refusalFrom(error: unknown): string {
  const message =
    typeof error === 'string'
      ? error
      : ((error as { message?: string } | null)?.message ?? '');

  return message.trim() !== '' ? message : 'That wording could not be saved.';
}
