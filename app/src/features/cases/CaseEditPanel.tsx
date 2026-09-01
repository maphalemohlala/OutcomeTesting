import { useMemo, useState } from 'react';
import { usePermissions } from '../../app/permissions/permissionContext';
import { Notice } from '../../components/feedback/Notice';
import { Modal } from '../../components/feedback/Modal';
import { ValidationSummary } from '../../components/feedback/ValidationSummary';
import { messageForFailure } from '../../services/errors';
import { UserPicker } from '../../components/form/UserPicker';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { updateCaseDetails } from '../../services/commands/updateCaseDetails';
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
import type { CaseDetail, CaseEditValues } from './useCaseDetail';
import './CaseEditPanel.css';

type FieldKind = 'text' | 'date' | 'choice' | 'user';

interface FieldDef {
  attr: keyof CaseEditValues;
  label: string;
  kind: FieldKind;
  options?: Record<number, string>;
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
      { attr: 'al_checkername', label: 'Checker', kind: 'user' },
      { attr: 'al_checkdate', label: 'Check date', kind: 'date' },
      { attr: 'al_taxcheckrequired', label: 'Tax check required', kind: 'choice', options: Al_outcomecasesal_taxcheckrequired },
      { attr: 'al_taxteamdisposition', label: 'Tax team disposition', kind: 'choice', options: Al_outcomecasesal_taxteamdisposition },
    ],
  },
];

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

  const optionLists = useMemo(() => {
    const lists = new Map<keyof CaseEditValues, { value: number; label: string }[]>();
    for (const section of SECTIONS) {
      for (const field of section.fields) {
        if (field.kind === 'choice' && field.options) {
          lists.set(field.attr, toOptions(field.options));
        }
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
    for (const section of SECTIONS) {
      for (const field of section.fields) {
        const current = form[field.attr];
        const original = detail.edit[field.attr];
        if (current === original) continue;
        changed[field.attr] = current == null ? '' : String(current);
      }
    }
    return changed;
  }

  function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    setModalError(null);

    const found: string[] = [];
    if (!reason.trim()) {
      found.push('Enter a reason for the change.');
    }
    const changed = changedFields();
    if (Object.keys(changed).length === 0) {
      found.push('Change at least one field.');
    }
    setErrors(found);
    if (found.length > 0) return;

    setSaving(true);
    updateCaseDetails({
      caseId: detail.id,
      fields: changed,
      reason: reason.trim(),
      expectedRowVersion: detail.rowVersion,
      idempotencyKey: intent.keyFor(detail.id),
    })
      .then((result) => {
        setSaving(false);
        if (result.ok) {
          intent.release(detail.id);
          setPanelNotice('Case details updated.');
          setReason('');
          setErrors([]);
          setOpen(false);
          onSaved();
        } else {
          setModalError(messageForFailure(result));
        }
      })
      .catch(() => {
        setSaving(false);
        setModalError('Something went wrong while processing your request. Please try again later.');
      });
  }

  function openForm() {
    setForm(detail.edit);
    setReason('');
    setModalError(null);
    setErrors([]);
    setPanelNotice(null);
    setOpen(true);
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
            Amend any case field. Only the fields you change are saved, each recorded with your
            reason in the audit history (BR-012).
          </p>

          <ValidationSummary errors={errors} />
          {modalError ? <Notice tone="error">{modalError}</Notice> : null}

          <form className="case-edit__form" onSubmit={onSubmit}>
            {SECTIONS.map((section) => (
              <fieldset key={section.heading} className="case-edit__section">
                <legend>{section.heading}</legend>
                <div className="case-edit__grid">
                  {section.fields.map((field) => {
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
                            {/* Status is mandatory and always set, so "Not set" is not one
                                of its choices; the server refuses a cleared status too. */}
                            {field.attr === 'al_casestatus' ? null : (
                              <option value="">Not set</option>
                            )}
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
                      </label>
                    );
                  })}
                </div>
              </fieldset>
            ))}

            <label className="case-edit__field" htmlFor="case-edit-reason">
              <span>Reason</span>
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
