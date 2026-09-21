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
import { ADVICE_DATE_LABEL, ukToday } from './caseHeaderDates';
import { MIGRATED_LISTS, choicesIncludingHeld, toOptionRows, type ManagedList } from '../admin/listOptions';
import { useAllListOptions } from '../admin/useListOptions';
import { useCaseReviews } from './useCaseReviews';
import { useUserDirectory } from '../../hooks/useUserDirectory';
import {
  Al_outcomecasesal_adviserstatus,
  Al_outcomecasesal_casestatus,
  Al_outcomecasesal_priority,
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

/**
 * `listoption` is a dropdown whose choices are ROWS the checking team maintains rather than
 * choice metadata a developer deploys (project owner, 2026-09-21). Its value is a guid, not
 * an option value, which is why it cannot just be another 'choice'.
 */
type FieldKind = 'text' | 'date' | 'choice' | 'user' | 'listoption';

/** MIGRATED_LISTS is the single place that says which lists have a lookup on the case. */
function managedList(key: string): ManagedList {
  const found = MIGRATED_LISTS.find((list) => list.key === key);
  if (!found) throw new Error(`No migrated list '${key}'`);
  return found;
}

interface FieldDef {
  attr: keyof CaseEditValues;
  label: string;
  kind: FieldKind;
  options?: Record<number, string>;
  /** For a `listoption` field, which managed list its choices come from. */
  list?: ManagedList;
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
      {
        attr: 'al_duedate',
        label: 'Due date',
        kind: 'date',
        help: 'Three days after the case was uploaded. Editable by a manager.',
      },
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
      {
        attr: 'al_casetypeid',
        label: 'Case type',
        kind: 'listoption',
        list: managedList('case-type'),
      },
      {
        attr: 'al_producttypeid',
        label: 'Product/solution type',
        kind: 'listoption',
        list: managedList('product-solution-type'),
        help: 'Maintained under Admin → Dropdown options. A new option appears here as soon as it is added.',
      },
      { attr: 'al_products', label: 'Products', kind: 'text' },
      { attr: 'al_advicedate', label: ADVICE_DATE_LABEL, kind: 'date' },
      {
        attr: 'al_samplesourceid',
        label: 'Sample source',
        kind: 'listoption',
        list: managedList('sample-source'),
      },
      {
        attr: 'al_preorpostcheckid',
        label: 'Check point',
        kind: 'listoption',
        list: managedList('pre-or-post-check'),
      },
    ],
  },
  {
    heading: 'Check and tax',
    fields: [
      // Check date is deliberately absent (project owner, 2026-09-21: "the check date has
      // to be uneditable as it is automatically updated on submit"). The submit stamps it -
      // SubmitReviewPlugin.StampCheckDate - and al_UpdateCaseDetails no longer allowlists
      // the column, so offering it here would produce a field that refuses every save.
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

  // Moving a deadline is a manager's act (item 6, 2026-09-19), and page.cases Edit is not
  // the right question: Tax and AQS reviewers hold it so they can complete the header
  // fields the extract does not carry. An affordance, not a boundary - al_UpdateCaseDetails
  // re-checks case.duedate and refuses the write (AD-041).
  const mayMoveDueDate = can('case.duedate', 'Edit');
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

  /*
   * The managed dropdowns' options, read live so an option added a moment ago on Admin ->
   * Dropdown options is offerable here without a deployment - which is the whole point of
   * holding them as rows.
   *
   * ONE read for all four lists. They share a table, the whole catalogue is a handful of
   * rows, and a panel drawing four managed dropdowns should not make four round trips to
   * fill them; the per-list shaping is pure.
   */
  const listOptions = useAllListOptions();
  const listRows = listOptions.status === 'ready' ? listOptions.rows : [];

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
      // A listoption carries a guid, so it is stored as the string it is - only 'choice'
      // fields are numeric option values.
      [attr]: kind === 'choice' ? (raw === '' ? null : Number(raw)) : raw,
    }));
  }

  function changedFields(): Record<string, string> {
    const changed: Record<string, string> = {};
    for (const field of allFields()) {
      // A field this user was never offered cannot be one they changed. Belt and braces:
      // the control is read-only, so `form` holds what the record holds - but the command
      // refuses a payload naming al_duedate whether or not the value moved, and losing a
      // whole save to a field nobody touched is exactly the failure item 6 fixed.
      if (field.attr === 'al_duedate' && !mayMoveDueDate) continue;

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
      // Only when it was actually changed, which is what the command validates: ApplyFields
      // walks the changed fields alone, so a case already carrying a future date - one
      // imported or entered before this rule - stays editable, and fixing that date is one
      // of the edits it stays editable for. Reading it off the form instead would refuse a
      // change to an unrelated field over a value nobody had touched.
      adviceDate: 'al_advicedate' in changed ? changed.al_advicedate : null,
      // The due date the save leaves behind, which is the form's - the value being written
      // where this save moves it, otherwise the one the case already holds. EffectiveDueDate
      // reads the same thing server-side.
      dueDate: typeof form.al_duedate === 'string' ? form.al_duedate : null,
      // The deadline's own end of the same rule (item 6, 2026-09-19), and only when this
      // save moves it. A case whose stored dates already disagree stays editable, which is
      // what makes fixing them possible.
      dueDateChanged: 'al_duedate' in changed ? changed.al_duedate : null,
      // ...compared against the meeting the case will still hold, not only one being typed
      // in the same breath. A due date pulled back under a meeting recorded weeks ago is the
      // ordinary way to break this.
      adviceDateOnRecord: typeof form.al_advicedate === 'string' ? form.al_advicedate : null,
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

    // Shown and explained rather than hidden, which is how this panel treats a check it
    // cannot reallocate too: a deadline somebody cannot move is still a deadline they need
    // to see, and an empty space would read as a case with no due date.
    if (field.attr === 'al_duedate' && !mayMoveDueDate) {
      return (
        <div key={field.attr} className="case-edit__field">
          <span>{field.label}</span>
          <p className="case-edit__only-check">
            {typeof value === 'string' && value.length > 0 ? value : 'Not set'}
          </p>
          <small className="case-edit__help">
            Set to three days after the case was uploaded. Only a manager can move it.
          </small>
        </div>
      );
    }

    return (
      <label key={field.attr} className="case-edit__field" htmlFor={inputId}>
        <span>{field.label}</span>
        {field.kind === 'listoption' ? (
          <select
            id={inputId}
            value={typeof value === 'string' ? value : ''}
            onChange={(e) => setField(field.attr, field.kind, e.target.value)}
          >
            <option value="">Not set</option>
            {/*
              Only the options offered TODAY. A retired one is not listed, and
              al_UpdateCaseDetails refuses it anyway - listing it would produce a choice the
              save rejects. A case already holding a retired option keeps it: the value is
              simply not among the choices, and leaving the field alone leaves it alone.
            */}
            {choicesIncludingHeld(
              toOptionRows(listRows, field.list!, new Date()),
              typeof value === 'string' ? value : null,
            ).map((option) => (
              <option key={option.id} value={option.id}>
                {option.label}
              </option>
            ))}
          </select>
        ) : field.kind === 'choice' ? (
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
            /*
             * The date of meeting cannot be in the future (item 9, 2026-09-19), so the
             * picker will not offer one. An affordance only - the save re-checks it, and so
             * does al_UpdateCaseDetails - because a typed date gets past a max on some
             * browsers.
             */
            max={field.attr === 'al_advicedate' ? ukToday() : undefined}
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

          <form className="case-edit__form" onSubmit={onSubmit} noValidate>
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
