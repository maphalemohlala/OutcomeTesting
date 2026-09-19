import { useState } from 'react';
import { PermissionGate } from '../../app/permissions/PermissionGate';
import { setFailAccountability } from '../../services/commands/setFailAccountability';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { useCaseOutcome, type CaseOutcomeRow } from './useCaseOutcome';
import {
  adviceQualityFailedFrom,
  effectiveAccountability,
  type AccountabilityFlags,
} from './failAccountability';
import './FailAccountabilityPanel.css';

/**
 * Who carries a fail on this case (item 8, 2026-09-19).
 *
 * The four flags feed Trail Light columns 11-14 and 17-20 (AD-039), and until now nothing
 * in either front end wrote them: al_SetFailAccountability has existed since 2026-09-05
 * with no caller, so every outcome row holds four falses and the export has been deriving
 * the pair from the people the case names. This is the screen that overrides that.
 *
 * What it draws is the EFFECTIVE pair - what the extract will actually carry - not the raw
 * row. Four empty boxes over a row that exports the paraplanner would tell the reader the
 * opposite of what the file says.
 *
 * It does not predict whether the file quality failed. The server answers that with
 * FileQuality.Resolve, reading whichever of Q-FQ-01 and Q-FQTAX-01 was answered; a second
 * implementation here could disagree, and the shared helper's own comment records why that
 * matters. So a case with no fail is refused by the command and the refusal is shown.
 */

const LABELS: { key: keyof AccountabilityFlags; discipline: string; who: 'adviser' | 'paraplanner' }[] = [
  { key: 'fqParaplanner', discipline: 'File Quality', who: 'paraplanner' },
  { key: 'fqAdviser', discipline: 'File Quality', who: 'adviser' },
  { key: 'aqAdviser', discipline: 'Advice Quality', who: 'adviser' },
  { key: 'aqParaplanner', discipline: 'Advice Quality', who: 'paraplanner' },
];

interface PeopleNames {
  adviser: string | null;
  paraplanner: string | null;
}

function OutcomeAccountability({
  outcome,
  people,
  onSaved,
}: {
  outcome: CaseOutcomeRow;
  people: PeopleNames;
  onSaved: () => void;
}) {
  const adviceQualityFailed = adviceQualityFailedFrom(outcome.finalOutcome ?? outcome.initialOutcome);

  /*
   * The file quality answer is NOT read here, so the derived view shows only what this page
   * can know: the advice quality pairing. The server reads Q-FQ-01 / Q-FQTAX-01 through
   * FileQuality.Resolve, and a second implementation could disagree with it - the shared
   * helper's own comment warns that is how the command ends up refusing a fail the export
   * has already attributed. So this page under-claims rather than guesses, and the note
   * below says so.
   */
  const { flags: effective, source } = effectiveAccountability(
    outcome.accountability,
    false,
    adviceQualityFailed,
  );

  const intent = useIntentKeys();
  const [draft, setDraft] = useState<AccountabilityFlags>(effective);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ text: string; kind: 'error' | 'success' } | null>(null);

  const changed =
    draft.fqAdviser !== effective.fqAdviser ||
    draft.fqParaplanner !== effective.fqParaplanner ||
    draft.aqAdviser !== effective.aqAdviser ||
    draft.aqParaplanner !== effective.aqParaplanner;

  function nameFor(who: 'adviser' | 'paraplanner'): string {
    const name = who === 'adviser' ? people.adviser : people.paraplanner;
    return name ?? `the ${who}`;
  }

  async function save() {
    setBusy(true);
    setMessage(null);

    const result = await setFailAccountability({
      outcomeId: outcome.id,
      fqAdviser: draft.fqAdviser,
      fqParaplanner: draft.fqParaplanner,
      aqAdviser: draft.aqAdviser,
      aqParaplanner: draft.aqParaplanner,
      /*
       * One key per intent, not per attempt. The token names the outcome AND what is being
       * recorded, so a retry after a timeout replays rather than writing twice, while
       * changing a tick and saving again is a different intent with its own key.
       */
      idempotencyKey: intent.keyFor(
        'accountability-' +
          outcome.id +
          '-' +
          [draft.fqAdviser, draft.fqParaplanner, draft.aqAdviser, draft.aqParaplanner].join('-'),
      ),
    });

    setBusy(false);

    if (!result.ok) {
      setMessage({ text: result.message, kind: 'error' });
      return;
    }

    setMessage({ text: 'Accountability recorded.', kind: 'success' });
    onSaved();
  }

  return (
    <li className="accountability__outcome">
      <div className="accountability__head">
        <span className="accountability__check">{outcome.reviewInstance ?? outcome.reference}</span>
        <span className="accountability__source" data-source={source}>
          {source === 'recorded' ? 'Recorded' : 'Derived by default'}
        </span>
      </div>

      {source === 'derived' ? (
        <p className="accountability__note">
          Nobody has been named on this check, so the extract uses the default: the
          paraplanner carries a File Quality fail, and the adviser carries an Advice Quality
          fail. The File Quality half is not shown ticked here because this page does not
          read the file quality answer &mdash; the extract does. Ticking anything below
          records a judgement instead, and the default stops applying to this check.
        </p>
      ) : null}

      <ul className="accountability__flags">
        {LABELS.map(({ key, discipline, who }) => (
          <li key={key}>
            <label>
              <input
                type="checkbox"
                checked={draft[key]}
                disabled={busy}
                onChange={(event) =>
                  setDraft((current) => ({ ...current, [key]: event.target.checked }))
                }
              />
              <span>
                <strong>{discipline}</strong> — {nameFor(who)}
              </span>
            </label>
          </li>
        ))}
      </ul>

      <div className="accountability__actions">
        <button
          type="button"
          className="accountability__save"
          disabled={!changed || busy}
          onClick={save}
        >
          {busy ? 'Recording…' : 'Record accountability'}
        </button>
        {message ? (
          <span className="accountability__message" data-kind={message.kind} role="status">
            {message.text}
          </span>
        ) : null}
      </div>
    </li>
  );
}

export function FailAccountabilityPanel({
  caseId,
  people,
}: {
  caseId: string;
  people: PeopleNames;
}) {
  // Re-read after a save, so the row flips from "Derived by default" to "Recorded" and
   // the ticks shown are the ones the extract will now use.
  const [reloadKey, setReloadKey] = useState(0);
  const state = useCaseOutcome(caseId, reloadKey);

  return (
    <PermissionGate resource="page.cases" need="Edit">
      <section className="accountability" aria-labelledby="panel-accountability">
        <h2 id="panel-accountability">Who carries a fail</h2>

        {state.status === 'loading' ? <p role="status">Loading outcomes…</p> : null}

        {state.status === 'unavailable' ? (
          <p className="accountability__note">The outcomes for this case could not be loaded.</p>
        ) : null}

        {state.status === 'ready' && state.outcomes.length === 0 ? (
          <p className="accountability__note">
            No outcome has been recorded for this case yet, so there is no fail to attribute.
          </p>
        ) : null}

        {state.status === 'ready' && state.outcomes.length > 0 ? (
          <ul className="accountability__list">
            {state.outcomes.map((outcome) => (
              <OutcomeAccountability
                key={outcome.id + '-' + reloadKey}
                outcome={outcome}
                people={people}
                onSaved={() => setReloadKey((current) => current + 1)}
              />
            ))}
          </ul>
        ) : null}
      </section>
    </PermissionGate>
  );
}
