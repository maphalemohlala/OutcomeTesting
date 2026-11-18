import { useEffect, useMemo, useState } from 'react';
import { Notice } from '../../components/feedback/Notice';
import { UserPicker } from '../../components/form/UserPicker';
import { PermissionGate } from '../../app/permissions/PermissionGate';
import { useIntentKeys, type IntentKeys } from '../../hooks/useIntentKey';
import { messageForFailure } from '../../services/errors';
import { reassignTcManager } from '../../services/commands/reassignTcManager';
import { Al_outcomecasesService } from '../../generated';
import { useSecurityConfig } from '../admin/useSecurityConfig';
import { lookupLabel } from './lookupLabel';

const SUPERVISOR_ROLE = 'al portal - t&c supervisor';

/** The lower-cased emails of everyone holding the T&C Supervisor role: the only people who can sign off. */
// eslint-disable-next-line react-refresh/only-export-components -- exported for its test
export function supervisorEmails(
  mappings: readonly { email: string; role: string; active: boolean }[],
): Set<string> {
  return new Set(
    mappings
      .filter((row) => row.active && row.role.trim().toLowerCase() === SUPERVISOR_ROLE)
      .map((row) => row.email.trim().toLowerCase())
      .filter((email) => email !== ''),
  );
}

export type ReassignResult = { kind: 'moved'; summary: string } | { kind: 'refused'; message: string };

/** One hand-over attempt; '' as the manager hands the case back to the adviser mapping. */
// eslint-disable-next-line react-refresh/only-export-components -- exported for its test
export async function runReassignment(
  caseId: string,
  managerId: string,
  reason: string,
  intent: IntentKeys,
): Promise<ReassignResult> {
  const token = `reassign:${caseId}:${managerId}:${reason.trim()}`;
  const result = await reassignTcManager({
    caseId,
    managerId,
    reason: reason.trim(),
    idempotencyKey: intent.keyFor(token),
  });

  if (!result.ok) return { kind: 'refused', message: messageForFailure(result) };

  intent.release(token);
  return { kind: 'moved', summary: result.data?.Summary ?? 'The case was handed over.' };
}

interface Current {
  chosen: string | null;
  manager: string | null;
}

/**
 * Hands this one case to another T&C Manager while the mapped one is away (requirements log
 * row 4). Changing the adviser mapping would move every case of that adviser.
 */
export function CaseTcManagerPanel({ caseId, onSaved }: { caseId: string; onSaved: () => void }) {
  return (
    <PermissionGate resource="case.reassignmanager" need="Edit">
      <Panel caseId={caseId} onSaved={onSaved} />
    </PermissionGate>
  );
}

function Panel({ caseId, onSaved }: { caseId: string; onSaved: () => void }) {
  const [current, setCurrent] = useState<Current | null | 'unavailable'>(null);
  const [loadKey, setLoadKey] = useState(0);
  const [managerId, setManagerId] = useState('');
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const intent = useIntentKeys();
  const security = useSecurityConfig(0);
  const holders = useMemo(
    () => supervisorEmails(security.status === 'ready' ? security.mappings : []),
    [security],
  );

  useEffect(() => {
    let cancelled = false;
    void loadKey;
    Al_outcomecasesService.get(caseId, { select: ['_al_tcmanageroverrideid_value', '_al_tcsupervisorcontactid_value'] })
      .then((result) => {
        if (cancelled) return;
        if (!result.success || !result.data) {
          setCurrent('unavailable');
          return;
        }
        const row = result.data;
        setCurrent({
          chosen: row._al_tcmanageroverrideid_value
            ? lookupLabel(row, 'al_tcmanageroverrideid', row.al_tcmanageroverrideidname) ?? 'Someone'
            : null,
          manager: lookupLabel(row, 'al_tcsupervisorcontactid', row.al_tcsupervisorcontactidname),
        });
      })
      .catch(() => {
        if (!cancelled) setCurrent('unavailable');
      });
    return () => {
      cancelled = true;
    };
  }, [caseId, loadKey]);

  async function hand(to: string) {
    setSaving(true);
    setError(null);
    setDone(null);
    const outcome = await runReassignment(caseId, to, reason, intent);
    setSaving(false);
    if (outcome.kind === 'refused') {
      setError(outcome.message);
      return;
    }
    setManagerId('');
    setReason('');
    setDone(outcome.summary);
    setLoadKey((key) => key + 1);
    onSaved();
  }

  const loaded = current !== null && current !== 'unavailable' ? current : null;

  return (
    <section className="case-detail__panel case-detail__tcmanager" aria-labelledby="panel-tcmanager">
      <h2 id="panel-tcmanager">T&amp;C Manager for this case</h2>
      {current === null ? <p role="status">Loading…</p> : null}
      {current === 'unavailable' ? (
        <p className="case-detail__checks-note">The case&apos;s T&amp;C Manager could not be loaded.</p>
      ) : null}
      {loaded ? (
        <p className="case-detail__checks-note">
          {loaded.chosen
            ? `${loaded.chosen}, chosen for this case in place of the manager mapped to its adviser.`
            : loaded.manager
              ? `${loaded.manager}, the manager mapped to its adviser.`
              : 'The manager mapped to its adviser. The case shows a name here once it reaches remediation.'}
        </p>
      ) : null}
      <p className="case-detail__checks-note">
        Hand this case to someone else while its manager is away. They see it in the portal, sign it
        off and get its emails; if a sign-off is already waiting they are told now. Every other case
        of this adviser stays with the mapped manager.
      </p>
      {error ? <Notice tone="error">{error}</Notice> : null}
      {done ? <Notice tone="success">{done}</Notice> : null}
      <div className="case-edit__grid">
        <label className="case-edit__field" htmlFor="case-tcmanager-picker">
          <span>New T&amp;C Manager</span>
          <UserPicker
            id="case-tcmanager-picker"
            field="id"
            value={managerId}
            onChange={setManagerId}
            onlyEmails={holders}
            placeholder={security.status === 'loading' ? 'Loading T&C Supervisors…' : 'Choose a T&C Supervisor'}
          />
        </label>
        <label className="case-edit__field" htmlFor="case-tcmanager-reason">
          <span>Reason</span>
          <textarea id="case-tcmanager-reason" value={reason} onChange={(e) => setReason(e.target.value)} />
        </label>
      </div>
      <div className="case-edit__actions">
        {loaded?.chosen ? (
          <button
            type="button"
            className="case-edit__cancel"
            disabled={saving || reason.trim() === ''}
            onClick={() => hand('')}
          >
            Hand back to the mapped manager
          </button>
        ) : null}
        <button
          type="button"
          className="case-edit__save"
          disabled={saving || managerId === '' || reason.trim() === ''}
          onClick={() => hand(managerId)}
        >
          {saving ? 'Saving…' : 'Hand to this manager'}
        </button>
      </div>
    </section>
  );
}
