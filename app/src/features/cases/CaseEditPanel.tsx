import { useMemo, useState } from 'react';
import { usePermissions } from '../../app/permissions/permissionContext';
import { Notice } from '../../components/feedback/Notice';
import { Modal } from '../../components/feedback/Modal';
import { ValidationSummary } from '../../components/feedback/ValidationSummary';
import { messageForFailure } from '../../services/errors';
import { classify } from '../../services/commands/failures';
import { UserPicker } from '../../components/form/UserPicker';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { updateCaseDetails } from '../../services/commands/updateCaseDetails';
import { assignCase } from '../../services/commands/assignCase';
import {
  describeSubmit,
  validateSubmit,
  type AllocationOutcome,
  type CommandOutcome,
} from './caseEditSubmit';
import { useCaseReviews } from './useCaseReviews';
import { useUserDirectory } from '../../hooks/useUserDirectory';
import {
  Al_outcomecasesal_adviserstatus,
  Al_outcomecasesal_casestatus,
  Al_outcomecasesal_casetype,
  Al_outcomecasesal_preorpostcheck,
  Al_outcomecasesal_priority,
  Al_outcomecasesal_productsolutiontype,
  Al_outcomecasesal_samplesource,
  Al_outcomecasesal_taxcheckrequired,
  Al_outcomecasesal_taxteamdisposition,
  Al_outcomecasesal_vulnerableclient,
} from '../../generated/models/Al_outcomecasesModel';
import { nextStatuses, type CaseStatus } from '../../types/domain';
import {
  effectiveRoute,
  isAllocatable,
  nextOwedDiscipline,
  owedDisciplines,
  type Discipline,
  type Disposition,
  type TaxAnswer,
} from './caseCheckers';
import type { CaseDetail, CaseEditValues } from './useCaseDetail';
import './CaseEditPanel.css';

type FieldKind = 'text' | 'date' | 'choice' | 'user';

interface FieldDef {
  attr: keyof CaseEditValues;
  label: string;
  kind: FieldKind;
  options?: Record<number, string>;
  /** A line under the control for a field whose name reads as more than it does. */
  help?: string;
}

interface Section {
  heading: string;
  fields: FieldDef[];
}

// Every field a manager may amend, grouped to mirror the read-only case panels. Choice
// fields map to their deployed option sets; the server allowlists and audits each change.
const SECTIONS: Section[] = [
  {
    heading: 'Case management',
    fields: [
      { attr: 'al_casestatus', label: 'Status', kind: 'choice', options: Al_outcomecasesal_casestatus },
      { attr: 'al_priority', label: 'Priority', kind: 'choice', options: Al_outcomecasesal_priority },
      { attr: 'al_duedate', label: 'Due date', kind: 'date' },
    ],
  },
  {
    heading: 'Client',
    fields: [
      { attr: 'al_clientname', label: 'Client', kind: 'text' },
      { attr: 'al_vulnerableclient', label: 'Vulnerable client', kind: 'choice', options: Al_outcomecasesal_vulnerableclient },
    ],
  },
  {
    heading: 'Adviser and paraplanner',
    fields: [
      { attr: 'al_advisername', label: 'Adviser', kind: 'user' },
      { attr: 'al_advisercode', label: 'Adviser code', kind: 'text' },
      { attr: 'al_adviserstatus', label: 'Adviser status', kind: 'choice', options: Al_outcomecasesal_adviserstatus },
      { attr: 'al_paraplanner', label: 'Paraplanner', kind: 'user' },
      { attr: 'al_paraplannercode', label: 'Paraplanner code', kind: 'text' },
    ],
  },
  {
    heading: 'Advice and product',
    fields: [
      { attr: 'al_casetype', label: 'Case type', kind: 'choice', options: Al_outcomecasesal_casetype },
      { attr: 'al_productsolutiontype', label: 'Product/solution type', kind: 'choice', options: Al_outcomecasesal_productsolutiontype },
      { attr: 'al_products', label: 'Products', kind: 'text' },
      { attr: 'al_advicedate', label: 'Advice date', kind: 'date' },
      { attr: 'al_samplesource', label: 'Sample source', kind: 'choice', options: Al_outcomecasesal_samplesource },
      { attr: 'al_preorpostcheck', label: 'Check point', kind: 'choice', options: Al_outcomecasesal_preorpostcheck },
    ],
  },
  {
    heading: 'Check and tax',
    fields: [
      { attr: 'al_checkdate', label: 'Check date', kind: 'date' },
      { attr: 'al_taxcheckrequired', label: 'Tax check required', kind: 'choice', options: Al_outcomecasesal_taxcheckrequired },
      { attr: 'al_taxteamdisposition', label: 'Tax team disposition', kind: 'choice', options: Al_outcomecasesal_taxteamdisposition },
    ],
  },
];

/** Every field the panel edits. Change detection walks this. */
function allFields(): FieldDef[] {
  const fields: FieldDef[] = [];
  for (const section of SECTIONS) {
    fields.push(...section.fields);
  }
  return fields;
}

function toOptions(map: Record<number, string>): { value: number; label: string }[] {
  return Object.entries(map).map(([value, label]) => ({ value: Number(value), label: String(label) }));
}

interface Props {
  detail: CaseDetail;
  onSaved: () => void;
}

/**
 * Manager edit affordance for the whole case (AD-036, AD-041). Every editable attribute is
 * prefilled from the current record; only the fields the user actually changes are sent to
 * the al_UpdateCaseDetails command, which enforces page.cases Edit, optimistic concurrency
 * and idempotency, and writes the before/after Audit Event (BR-012). The panel renders only
 * when the caller holds Edit; the server-side command is the real authorization gate.
 */
export function CaseEditPanel({ detail, onSaved }: Props) {
  const { can } = usePermissions();
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<CaseEditValues>(detail.edit);
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [panelNotice, setPanelNotice] = useState<string | null>(null);
  const [modalError, setModalError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const intent = useIntentKeys();

  // Allocation moved into this modal on 2026-09-10 (project owner direction), replacing
  // the standalone allocation screen. It is a second command, not a second set of fields.
  // One chosen checker per discipline - not per review instance, because a discipline the
  // route requires may not have had its review opened yet. Empty means "leave this check
  // as it is", so a save that only touches case fields allocates nothing.
  const [checkerBy, setCheckerBy] = useState<Record<Discipline, string>>({ Tax: '', AQS: '' });

  const mayAssign = can('command.assign', 'Edit');
  const reviews = useCaseReviews(detail.id);
  const directory = useUserDirectory();

  const allReviews = reviews.status === 'ready' ? reviews.reviews : [];

  // Which checks this case owes, from the route - and the route follows the Tax check
  // required answer (BR-004). Derived from the form rather than from the saved case so the
  // checker fields answer that dropdown as soon as it is changed, instead of only after
  // the save that writes the route. The rules live in caseCheckers, mirrored from
  // UpdateCaseDetailsPlugin.DeriveRoute and ClaimCasePlugin.NextDiscipline and tested
  // there so they cannot drift from the plug-ins.
  const taxAnswer = (value: CaseEditValues['al_taxcheckrequired']): TaxAnswer => {
    if (value == null) return null;
    const label = (Al_outcomecasesal_taxcheckrequired as Record<number, string>)[Number(value)];
    return label === 'Yes' || label === 'No' ? label : null;
  };

  const disposition = (value: CaseEditValues['al_taxteamdisposition']): Disposition => {
    if (value == null) return null;
    const label = (Al_outcomecasesal_taxteamdisposition as Record<number, string>)[Number(value)];
    return label === 'Submit to AQS' || label === 'Return to paraplanner' ? label : null;
  };

  const owed = owedDisciplines(
    effectiveRoute(
      detail.route,
      taxAnswer(form.al_taxcheckrequired),
      form.al_taxcheckrequired !== detail.edit.al_taxcheckrequired,
      disposition(form.al_taxteamdisposition),
      form.al_taxteamdisposition !== detail.edit.al_taxteamdisposition,
    ),
  );

  const reviewFor = (discipline: Discipline) =>
    allReviews.find((review) => review.type === discipline) ?? null;

  const stateOf = (discipline: Discipline) => {
    const review = reviewFor(discipline);
    return { exists: review !== null, submitted: Boolean(review?.submittedOn) };
  };

  const nextOwed = nextOwedDiscipline(owed, stateOf);

  const chosen = (Object.entries(checkerBy) as [Discipline, string][]).filter(
    ([discipline, email]) => email && owed.includes(discipline),
  );

  const candidates = directory.status === 'ready' ? directory.users.filter((u) => u.active) : [];

  const optionLists = useMemo(() => {
    const lists = new Map<keyof CaseEditValues, { value: number; label: string }[]>();
    for (const field of allFields()) {
      if (field.kind === 'choice' && field.options) {
        lists.set(field.attr, toOptions(field.options));
      }
    }

    // Status is the one choice that is not a free list: offering all thirteen values lets
    // a manager pick a move the al_UpdateCaseDetails command will refuse, and previously
    // let a case jump straight to Closed. The command is still the authoritative gate
    // (AD-003); this only stops the UI proposing what it would reject.
    const reachable = nextStatuses(detail.status);
    lists.set(
      'al_casestatus',
      toOptions(Al_outcomecasesal_casestatus).filter((option) =>
        reachable.includes(option.label as CaseStatus),
      ),
    );

    return lists;
  }, [detail.status]);

  if (!can('page.cases', 'Edit')) {
    return null;
  }

  function setField(attr: keyof CaseEditValues, kind: FieldKind, raw: string) {
    setForm((prev) => ({
      ...prev,
      [attr]: kind === 'choice' ? (raw === '' ? null : Number(raw)) : raw,
    }));
  }

  function changedFields(): Record<string, string> {
    const changed: Record<string, string> = {};
    for (const field of allFields()) {
      const current = form[field.attr];
      const original = detail.edit[field.attr];
      if (current === original) continue;
      changed[field.attr] = current == null ? '' : String(current);
    }
    return changed;
  }

  /** al_UpdateCaseDetails, or 'skipped' when the user changed no fields. */
  async function saveFields(changed: Record<string, string>): Promise<CommandOutcome> {
    if (Object.keys(changed).length === 0) return { kind: 'skipped' };

    const result = await updateCaseDetails({
      caseId: detail.id,
      fields: changed,
      reason: reason.trim(),
      expectedRowVersion: detail.rowVersion,
      idempotencyKey: intent.keyFor(detail.id),
    });

    if (!result.ok) return { kind: 'failed', message: messageForFailure(result) };

    intent.release(detail.id);
    return { kind: 'ok' };
  }

  /** al_AssignCase once per check whose checker was changed. */
  async function allocateAll(): Promise<AllocationOutcome[]> {
    const results: AllocationOutcome[] = [];

    for (const [discipline, email] of chosen) {
      const review = reviewFor(discipline);

      // Named explicitly where the review exists. Where it does not, the id is omitted and
      // al_AssignCase opens the discipline the route owes next - which the form only ever
      // offers when that is this one.
      const token = `assign:${detail.id}:${discipline}:${email}`;
      try {
        const result = await assignCase({
          caseId: detail.id,
          assigneeEmail: email,
          reviewInstanceId: review ? review.id : null,
          idempotencyKey: intent.keyFor(token),
        });

        if (!result.ok) {
          results.push({
            label: discipline,
            outcome: { kind: 'failed', message: messageForFailure(result) },
          });
          continue;
        }

        intent.release(token);
        results.push({
          label: discipline,
          outcome: { kind: 'ok', alreadyDone: result.data.Status === 'AlreadyAssigned' },
        });
      } catch (error) {
        results.push({
          label: discipline,
          outcome: { kind: 'failed', message: messageForFailure(classify(error)) },
        });
      }
    }

    return results;
  }

  function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    setModalError(null);

    const changed = changedFields();
    const found = validateSubmit({
      changedCount: Object.keys(changed).length,
      allocationCount: chosen.length,
    });
    setErrors(found);
    if (found.length > 0) return;

    setSaving(true);

    // Fields first, allocation second. A successful allocation moves the case to Assigned
    // and bumps its row version, so the other order would make the field save fail its own
    // concurrency check. A refused field save stops the sequence before anything is
    // allocated — see caseEditSubmit for what each combination reports.
    saveFields(changed)
      .then(async (fields) => {
        const allocations = fields.kind === 'failed' ? [] : await allocateAll();
        return describeSubmit(fields, allocations);
      })
      .then((outcome) => {
        setSaving(false);
        setModalError(outcome.error);
        if (outcome.notice) setPanelNotice(outcome.notice);
        if (outcome.close) {
          setReason('');
          setErrors([]);
          setCheckerBy({ Tax: '', AQS: '' });
          setOpen(false);
        }
        if (outcome.reload) onSaved();
      })
      .catch((error) => {
        setSaving(false);
        setModalError(messageForFailure(classify(error)));
      });
  }

  function openForm() {
    setForm(detail.edit);
    setReason('');
    setCheckerBy({ Tax: '', AQS: '' });
    setModalError(null);
    setErrors([]);
    setPanelNotice(null);
    setOpen(true);
  }

  /** One editable field, wherever it is drawn. */
  function renderField(field: FieldDef) {
    const value = form[field.attr];
    const inputId = `case-edit-${field.attr}`;
    return (
      <label key={field.attr} className="case-edit__field" htmlFor={inputId}>
        <span>{field.label}</span>
        {field.kind === 'choice' ? (
          <select
            id={inputId}
            value={value == null ? '' : String(value)}
            onChange={(e) => setField(field.attr, field.kind, e.target.value)}
          >
            {/* Status is mandatory and always set, so "Not set" is not one of its
                choices; the server refuses a cleared status too. */}
            {field.attr === 'al_casestatus' ? null : <option value="">Not set</option>}
            {(optionLists.get(field.attr) ?? []).map((option) => (
              <option key={option.value} value={String(option.value)}>
                {option.label}
              </option>
            ))}
          </select>
        ) : field.kind === 'user' ? (
          <UserPicker
            id={inputId}
            value={typeof value === 'string' ? value : ''}
            onChange={(next) => setField(field.attr, field.kind, next)}
            placeholder="Not set"
          />
        ) : (
          <input
            id={inputId}
            type={field.kind === 'date' ? 'date' : 'text'}
            value={typeof value === 'string' ? value : ''}
            onChange={(e) => setField(field.attr, field.kind, e.target.value)}
          />
        )}
        {field.help ? <small className="case-edit__help">{field.help}</small> : null}
      </label>
    );
  }

  return (
    <section className="case-edit" aria-labelledby="case-edit-heading">
      <div className="case-edit__bar">
        <h2 id="case-edit-heading">Case management</h2>
        <button type="button" className="case-edit__save" onClick={openForm}>
          Edit case details
        </button>
      </div>
      {panelNotice ? <Notice tone="success">{panelNotice}</Notice> : null}

      {open ? (
        <Modal title="Edit case details" onClose={() => setOpen(false)}>
          <p className="case-edit__intro">
            Amend any case field, and allocate a check to a named checker. Only the fields
            you change are saved, each recorded in the audit history with the change itself
            and your reason where you give one (BR-012). Adviser, paraplanner and checker
            can be changed at any point, including after a check has started.
          </p>

          <ValidationSummary errors={errors} />
          {modalError ? <Notice tone="error">{modalError}</Notice> : null}

          <form className="case-edit__form" onSubmit={onSubmit}>
            {SECTIONS.map((section) => (
              <fieldset key={section.heading} className="case-edit__section">
                <legend>{section.heading}</legend>
                <div className="case-edit__grid">{section.fields.map(renderField)}</div>
              </fieldset>
            ))}

            {/* Allocation (FR-005, FR-006, BR-003, AD-040, OD-029). Its own command, its
                own permission, run by the same Save button. An affordance, not a boundary:
                al_AssignCase re-checks command.assign server-side (NFR-SEC-01).

                No team filter, deliberately. OD-029 records that per-team scoping is
                unresolved, so the queue name is free text and nothing stops a Tax lead
                allocating an AQS check; a filter would imply an enforcement that does not
                exist. */}
            {mayAssign ? (
              <fieldset className="case-edit__section">
                <legend>Checkers</legend>

                {reviews.status === 'loading' ? (
                  <p className="case-edit__help" role="status">
                    Loading this case&apos;s checks…
                  </p>
                ) : null}

                {owed.length === 0 ? (
                  <p className="case-edit__help">
                    This case has no review route yet, so it owes no checks. Answer
                    <strong> Tax check required</strong> above and the checks it owes appear
                    here (BR-004).
                  </p>
                ) : null}

                <div className="case-edit__grid">
                  {owed.map((discipline) => {
                    const review = reviewFor(discipline);
                    const inputId = `case-edit-checker-${discipline}`;
                    const held = review?.owner ?? 'Unassigned';

                    // Submitted: al_AssignCase refuses it, so it is reported not offered.
                    if (review?.submittedOn) {
                      return (
                        <div key={discipline} className="case-edit__field">
                          <span>{discipline} Checker</span>
                          <p className="case-edit__only-check">{held}</p>
                          <small className="case-edit__help">
                            Submitted {review.submittedOn}, so it can no longer be
                            reallocated.
                          </small>
                        </div>
                      );
                    }

                    // Not the discipline the case owes next. Allocating it would put this
                    // person on a check the route has not reached - and where the review
                    // does not exist, al_AssignCase would open the earlier leg instead and
                    // put the choice on that. Stated rather than offered.
                    //
                    // The review can exist here: a case routed AQS only, given an AQS
                    // review, and then answered Yes to Tax check required owes Tax first
                    // again. Saying "not open yet" of a check somebody is already sitting
                    // on would be untrue, so who holds it is named instead.
                    if (!isAllocatable(discipline, stateOf(discipline), nextOwed)) {
                      const blocker = owed.find((d) => d !== discipline);
                      return (
                        <div key={discipline} className="case-edit__field">
                          <span>{discipline} Checker</span>
                          <p className="case-edit__only-check">{review ? held : 'Not open yet'}</p>
                          <small className="case-edit__help">
                            {review
                              ? `This check cannot be reallocated until the ${blocker} check has been submitted (BR-004).`
                              : `This check opens once the ${blocker} check has been submitted (BR-004).`}
                          </small>
                        </div>
                      );
                    }

                    return (
                      <label key={discipline} className="case-edit__field" htmlFor={inputId}>
                        <span>{discipline} Checker</span>
                        <select
                          id={inputId}
                          value={checkerBy[discipline]}
                          onChange={(e) =>
                            setCheckerBy((prev) => ({ ...prev, [discipline]: e.target.value }))
                          }
                        >
                          {/* The empty option carries who holds it now, so the control
                              answers "who has this check?" without a second line, and
                              choosing a person is unambiguously a change. */}
                          <option value="">
                            Leave as it is — {review ? held : 'not opened yet'}
                          </option>
                          {candidates.map((person) => (
                            <option key={person.id} value={person.email}>
                              {person.name} — {person.email}
                            </option>
                          ))}
                        </select>
                        {!review ? (
                          <small className="case-edit__help">
                            Allocating opens this check.
                          </small>
                        ) : null}
                        {directory.status === 'unavailable' ? (
                          <small className="case-edit__help">
                            The user registry is not available to you, so there is no one to
                            choose from.
                          </small>
                        ) : null}
                      </label>
                    );
                  })}
                </div>

                {owed.length > 0 ? (
                  <p className="case-edit__help">
                    Allocating puts the check on that person&apos;s portal worklist and
                    writes their name onto the checklist header. A check can be reallocated
                    after the checker has started it.
                  </p>
                ) : null}
              </fieldset>
            ) : null}

            <label className="case-edit__field" htmlFor="case-edit-reason">
              <span>Reason <span className="case-edit__optional">(optional)</span></span>
              <textarea
                id="case-edit-reason"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                rows={2}
                placeholder="Why are you making this change?"
              />
            </label>

            <div className="case-edit__actions">
              <button
                type="button"
                className="case-edit__cancel"
                onClick={() => setOpen(false)}
                disabled={saving}
              >
                Cancel
              </button>
              <button type="submit" className="case-edit__save" disabled={saving}>
                {saving ? 'Saving…' : 'Save changes'}
              </button>
            </div>
          </form>
        </Modal>
      ) : null}
    </section>
  );
}
