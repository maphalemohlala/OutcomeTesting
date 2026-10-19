import { useEffect, useState } from 'react';
import {
  Al_notificationsService,
  Al_notificationtemplatesService,
  ContactsService,
} from '../../generated';
// The generated index re-exports each model as a NAMESPACE, so option-set types come from
// the model module itself - the same import useQuestionLibrary.ts makes.
import type { Al_notificationtemplatesal_event } from '../../generated/models/Al_notificationtemplatesModel';
import { logTechnical } from '../../services/errors';
import { plainMessage } from '../../services/commands/failures';
import { TEMPLATE_CODES, templateHint } from './notificationTemplates';
import {
  CUSTOM_TOKENS,
  readRecipients,
  recipientColumns,
  type Recipients,
} from './notificationRouting';
import { describeRemoveFailure } from './notificationTemplateRemoval';

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
  /**
   * True for a letter an administrator created (AD-168). It has no compiled copy behind it,
   * so it is sent only because its own row says when and to whom.
   */
  custom: boolean;
  /** The event that sends it. Only a custom letter carries one; the twelve are raised in code. */
  eventValue: number | null;
  /** Who receives it, where somebody has chosen. None leaves the built-in routing alone. */
  recipients: Recipients;
}

export interface ContactOption {
  id: string;
  name: string;
  email: string;
}

export type TemplatesState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; templates: TemplateRow[]; contacts: ContactOption[] };

export interface TemplateDraft {
  code: string;
  name: string;
  subject: string;
  body: string;
  eventValue: number | null;
  recipients: Recipients;
}

/**
 * Every letter the solution sends: the twelve built in, and any an administrator has added.
 *
 * <p>
 * The built-in list is driven by the CODE CATALOGUE, not by the table. A letter with no stored
 * row is still sent — on the wording compiled into the plug-in assembly — so listing only the
 * rows would hide most of the letters and make the page look like the whole set when it was
 * not. Each row says which it is.
 * </p>
 * <p>
 * A custom letter is the other way round: it exists only as a row, so it is listed from the
 * table. Anything in the table whose code is not one of the twelve is one of these.
 * </p>
 */
export function useNotificationTemplates(reloadKey = 0): TemplatesState {
  const [state, setState] = useState<TemplatesState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      Al_notificationtemplatesService.getAll({ orderBy: ['al_templatecode asc'] }),
      ContactsService.getAll({ orderBy: ['fullname asc'] }),
    ])
      .then(([result, contactResult]) => {
        if (cancelled) return;

        if (!result.success || !result.data) {
          logTechnical('notification templates load', result.error);
          setState({
            status: 'unavailable',
            reason: 'The email templates could not be loaded right now.',
          });
          return;
        }

        const active = result.data.filter((r) => Number(r.statecode) === 0);
        const byCode = new Map(active.map((r) => [(r.al_templatecode ?? '').trim(), r]));

        const builtIn: TemplateRow[] = TEMPLATE_CODES.map((code) => {
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
            custom: false,
            eventValue: row?.al_event != null ? Number(row.al_event) : null,
            recipients: readRecipients(row ?? {}),
          };
        });

        const custom: TemplateRow[] = active
          .filter((r) => !templateHint((r.al_templatecode ?? '').trim()))
          .map((r) => ({
            id: r.al_notificationtemplateid,
            code: (r.al_templatecode ?? '').trim(),
            name: (r.al_name ?? '').trim(),
            subject: r.al_subject ?? '',
            body: r.al_body ?? '',
            // A letter of your own may use only what a case can always answer, whatever
            // raised it. The plug-in refuses anything else.
            tokens: CUSTOM_TOKENS,
            isHtml: true,
            stored: true,
            custom: true,
            eventValue: r.al_event != null ? Number(r.al_event) : null,
            recipients: readRecipients(r),
          }));

        // Contacts are for the "a named contact" recipient. A failure here is not fatal: the
        // four role recipients still work, and the picker says why it is empty.
        const contacts: ContactOption[] =
          contactResult.success && contactResult.data
            ? contactResult.data
                .filter((c) => Number(c.statecode) === 0 && (c.emailaddress1 ?? '').trim() !== '')
                .map((c) => ({
                  id: c.contactid.toLowerCase(),
                  name: (c.fullname ?? '').trim(),
                  email: (c.emailaddress1 ?? '').trim(),
                }))
                .filter((c) => c.name.length > 0)
            : [];

        if (!contactResult.success) {
          logTechnical('notification templates contacts load', contactResult.error);
        }

        setState({ status: 'ready', templates: [...builtIn, ...custom], contacts });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('notification templates load', error);
        setState({
          status: 'unavailable',
          reason: 'The email templates could not be loaded right now.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}

/**
 * Saves one letter: its wording, and who it goes to.
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
  recipients: Recipients = row.recipients,
): Promise<{ ok: true } | { ok: false; reason: string }> {
  try {
    const fields = {
      al_subject: subject,
      al_body: body,
      ...recipientColumns(recipients),
    };

    const result = row.id
      ? await Al_notificationtemplatesService.update(row.id, fields)
      : await Al_notificationtemplatesService.create({
          al_name: row.name,
          al_templatecode: row.code,
          statecode: 0,
          ...fields,
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
 * Takes a stored letter off the table (F24).
 *
 * <p>
 * One call covers both things this page could not undo. For a letter of an administrator's
 * own, the row IS the letter, so removing it stops the letter existing. For one of the
 * twelve, the row is only the stored wording over the top of the copy compiled into the
 * assembly, so removing it puts the built-in copy back.
 * </p>
 * <p>
 * Deleted rather than deactivated. Every server-side read of this table already filters to
 * the active row for a code — so deactivating would work — but `al_templatecode` is the
 * table's alternate key and an inactive row keeps it, which would leave the code held by a
 * row this page no longer lists. That is F18's trap on the adviser mapping table, and this
 * is the same table shape.
 * </p>
 */
export async function removeTemplate(
  id: string,
): Promise<{ ok: true } | { ok: false; reason: string }> {
  try {
    // The generated delete resolves to void and REJECTS on failure, unlike create and
    // update which resolve to a result object. So there is nothing to inspect here: an
    // arrival back from the await is the success.
    await Al_notificationtemplatesService.delete(id);
    return { ok: true };
  } catch (error) {
    logTechnical('notification template remove', error);
    return { ok: false, reason: describeRemoveFailure(error) };
  }
}

/** Creates a letter of an administrator's own (AD-168). */
export async function createTemplate(
  draft: TemplateDraft,
): Promise<{ ok: true } | { ok: false; reason: string }> {
  try {
    const result = await Al_notificationtemplatesService.create({
      al_name: draft.name,
      al_templatecode: draft.code,
      al_subject: draft.subject,
      al_body: draft.body,
      statecode: 0,
      ...(draft.eventValue === null
        ? {}
        : { al_event: draft.eventValue as Al_notificationtemplatesal_event }),
      ...recipientColumns(draft.recipients),
    });

    if (!result.success) {
      logTechnical('notification template create', result.error);
      return { ok: false, reason: refusalFrom(result.error) };
    }

    return { ok: true };
  } catch (error) {
    logTechnical('notification template create', error);
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
/**
 * This read `error.message` and showed it. For a guard that raises a bare sentence the SDK
 * makes `message` the whole OData body, so the administrator got the plug-in class, the
 * table name, the plug-in trace and their own user GUID in the dialog, with the sentence
 * buried in the middle of it (F12, 2026-09-20). plainMessage unwraps the body; anything it
 * cannot read falls back to the default rather than to the body.
 */
function refusalFrom(error: unknown): string {
  const message = plainMessage(error);
  return message !== '' ? message : 'That wording could not be saved.';
}

/** A letter the outbox could not send, as the page lists it. */
export interface FailedLetter {
  id: string;
  queuedOn: string | null;
  subject: string;
  recipient: string | null;
  reason: string;
}

export type FailedLettersState =
  | { status: 'loading' }
  | { status: 'unavailable' }
  | { status: 'ready'; letters: FailedLetter[] };

/** How far back the page looks for letters that were not sent. */
export const FAILED_LETTER_DAYS = 30;

/**
 * The letters that could not be sent in the last {@link FAILED_LETTER_DAYS} days, newest first.
 *
 * <p>
 * A letter used to fail with nothing anywhere a person would look: the outbox records why, but
 * nobody opens the outbox. A load failure is reported as such rather than as an empty list,
 * which would read as "nothing failed".
 * </p>
 */
export function useFailedLetters(reloadKey = 0): FailedLettersState {
  const [state, setState] = useState<FailedLettersState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;
    const since = new Date(Date.now() - FAILED_LETTER_DAYS * 24 * 60 * 60 * 1000).toISOString();

    Al_notificationsService.getAll({
      select: ['al_notificationid', 'al_queuedon', 'al_subject', 'al_recipientemail', 'al_failurereason'],
      filter: `al_status eq 120910812 and al_queuedon ge ${since}`,
      orderBy: ['al_queuedon desc'],
      top: 100,
    })
      .then((result) => {
        if (cancelled) return;

        if (!result.success || !result.data) {
          logTechnical('failed letters load', result.error);
          setState({ status: 'unavailable' });
          return;
        }

        setState({
          status: 'ready',
          letters: result.data.map((n) => ({
            id: n.al_notificationid,
            queuedOn: n.al_queuedon ?? null,
            subject: (n.al_subject ?? '').trim() || '(no subject)',
            recipient: (n.al_recipientemail ?? '').trim() || null,
            reason: (n.al_failurereason ?? '').trim() || 'No reason was recorded.',
          })),
        });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('failed letters load', error);
        setState({ status: 'unavailable' });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}
