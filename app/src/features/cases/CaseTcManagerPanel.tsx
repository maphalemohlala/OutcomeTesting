import { useEffect, useMemo, useState } from 'react';
import { Notice } from '../../components/feedback/Notice';
import { UserPicker } from '../../components/form/UserPicker';
import { PersonCell } from '../../components/people/Avatar';
import { PermissionGate } from '../../app/permissions/PermissionGate';
import { useIntentKeys, type IntentKeys } from '../../hooks/useIntentKey';
import { messageForFailure } from '../../services/errors';
import { reassignTcManager } from '../../services/commands/reassignTcManager';
import { Al_advisermappingsService, Al_outcomecasesService } from '../../generated';
import { useSecurityConfig } from '../admin/useSecurityConfig';
import { lookupLabel } from './lookupLabel';
import { Skeleton } from '../../components/feedback/Skeleton';

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
  /** Every manager mapped to the case's adviser; an adviser may have several (owner, 2026-11-24). */
  managers: string[];
}

/** The OData filter for an adviser's active mappings; quotes doubled, as OData escapes them. */
// eslint-disable-next-line react-refresh/only-export-components -- exported for its test
export function mappingFilter(adviserEmail: string): string {
  return `al_adviseremail eq '${adviserEmail.trim().replace(/'/g, "''")}' and statecode eq 0`;
}

/** The case's mapped managers by name, once each, in the order the mappings came. */
async function mappedManagers(adviserEmail: string): Promise<string[]> {
  if (adviserEmail.trim() === '') return [];
  const result = await Al_advisermappingsService.getAll({ filter: mappingFilter(adviserEmail), orderBy: ['createdon asc'] });
  if (!result.success || !result.data) throw new Error('mappings unavailable');
  const names = result.data.map((row) => lookupLabel(row, 'al_tcmanagerid', undefined) ?? 'A manager on record');
  return [...new Set(names)];
}

/**
 * Hands this one case to another T&C Manager while the mapped ones are away (requirements log
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
    Al_outcomecasesService.get(caseId, { select: ['_al_tcmanageroverrideid_value', 'al_adviseremail'] })
      .then(async (result) => {
        if (cancelled) return;
        if (!result.success || !result.data) {
          setCurrent('unavailable');
          return;
        }
        const row = result.data;
        const managers = await mappedManagers(row.al_adviseremail ?? '');
        if (cancelled) return;
        setCurrent({
          chosen: row._al_tcmanageroverrideid_value
            ? lookupLabel(row, 'al_tcmanageroverrideid', row.al_tcmanageroverrideidname) ?? 'Someone'
            : null,
          managers,
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

  // Who holds the case now: the person chosen for it, else every manager mapped to the adviser.
  const onCase = loaded ? (loaded.chosen ? [loaded.chosen] : loaded.managers) : [];

  return (
    <section className="case-card case-detail__tcmanager" aria-labelledby="panel-tcmanager">
      <h2 id="panel-tcmanager">T&amp;C Manager for this case</h2>
      {current === null ? <Skeleton label="Loading the T&C Supervisor…" rows={1} /> : null}
      {current === 'unavailable' ? (
        <p className="case-card__note">The case&apos;s T&amp;C Manager could not be loaded.</p>
      ) : null}
      {loaded ? (
        onCase.length > 0 ? (
          <ul className="case-detail__tcmanagers">
            {onCase.map((name) => (
              <li key={name}>
                <PersonCell
                  name={name}
                  detail={
                    loaded.chosen
                      ? 'Chosen for this case in place of the mapped managers'
                      : onCase.length > 1
                        ? 'Mapped to its adviser; any of them can sign off'
                        : 'The manager mapped to its adviser'
                  }
                />
              </li>
            ))}
          </ul>
        ) : (
          <p className="case-card__note">
            No T&amp;C Manager is mapped to this case&apos;s adviser yet, so nobody can sign it off.
            Map one on the Adviser mapping page, or hand the case to someone below.
          </p>
        )
      ) : null}
      <div className="case-detail__handover">
        <h3>Hand this case to someone else</h3>
        <p className="case-card__note">
          While its managers are away, the person you choose sees it in the portal, signs it off and gets
          its emails, in place of all of them. If a sign-off is already waiting they are told now. Every
          other case of this adviser stays with the mapped managers.
        </p>
        {error ? <Notice tone="error">{error}</Notice> : null}
        {done ? <Notice tone="success">{done}</Notice> : null}
        <label className="case-edit__field" htmlFor="case-tcmanager-picker">
          <span>New T&amp;C Manager</span>
          <UserPicker
            id="case-tcmanager-picker"
            field="id"
            value={managerId}
            onChange={setManagerId}
            onlyEmails={holders}
            placeholder={security.status === 'loading' ? 'Loading T&C Supervisors…' : 'Choose a manager'}
          />
        </label>
        <label className="case-edit__field" htmlFor="case-tcmanager-reason">
          <span>Reason</span>
          <textarea
            id="case-tcmanager-reason"
            rows={3}
            value={reason}
            placeholder="Why the case is moving"
            onChange={(e) => setReason(e.target.value)}
          />
        </label>
        <div className="case-edit__actions">
          {loaded?.chosen ? (
            <button
              type="button"
              className="button-quiet"
              disabled={saving || reason.trim() === ''}
              onClick={() => hand('')}
            >
              Hand back to the mapped managers
            </button>
          ) : null}
          <button
            type="button"
            className="button-secondary"
            disabled={saving || managerId === '' || reason.trim() === ''}
            onClick={() => hand(managerId)}
          >
            {saving ? 'Saving…' : 'Hand over case'}
          </button>
        </div>
      </div>
    </section>
  );
}
