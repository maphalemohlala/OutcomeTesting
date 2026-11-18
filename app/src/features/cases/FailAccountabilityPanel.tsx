import { useMemo, useState } from 'react';
import { PermissionGate } from '../../app/permissions/PermissionGate';
import { setFailAccountability } from '../../services/commands/setFailAccountability';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { UserPicker } from '../../components/form/UserPicker';
import type { CaseOutcomeRow, CaseOutcomeState } from './useCaseOutcome';
import { messageForFailure } from '../../services/errors';
import { useSecurityConfig } from '../admin/useSecurityConfig';
import {
  accountableEmails,
  adviceQualityFailedFrom,
  namedPersonNote,
  effectiveAccountability,
  recordDisabled,
  type AccountabilityFlags,
  NOBODY,
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

/** Who can be named: everyone holding the Adviser or Planner role, once that is known. */
interface NameableHolders {
  status: 'loading' | 'unavailable' | 'ready';
  emails: ReadonlySet<string>;
}

function OutcomeAccountability({
  outcome,
  people,
  holders,
  onSaved,
}: {
  outcome: CaseOutcomeRow;
  people: PeopleNames;
  holders: NameableHolders;
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
    Boolean(outcome.fqAdministrator),
  );

  const intent = useIntentKeys();
  const [draft, setDraft] = useState<AccountabilityFlags>(effective);

  // Who carries it, where that is someone the case does not name. Empty means the case's
  // own adviser or paraplanner, which is what the extract falls back to.
  const [fqPerson, setFqPerson] = useState(outcome.fqAccountable?.id ?? '');
  const [aqPerson, setAqPerson] = useState(outcome.aqAccountable?.id ?? '');
  // The administrator carrying the File Quality fail (requirements log row 10). Never exported.
  const [fqAdministrator, setFqAdministrator] = useState(outcome.fqAdministrator?.id ?? '');

  /*
   * Take the record's values when the record itself changes, and not before (F29).
   *
   * The version number moves when the row does, so this fires once the re-read after a save
   * has actually landed. Until then the editor keeps showing what the person typed, which is
   * both what they expect and, after a successful save, what the row now says.
   *
   * React's documented way of adjusting state to a prop, done during render rather than in an
   * effect so there is no frame where the old values are painted.
   */
  // Set by a successful save, cleared below when the re-read's row version lands.
  const [awaitingReread, setAwaitingReread] = useState(false);
  const [seenVersion, setSeenVersion] = useState(outcome.rowVersion);
  if (outcome.rowVersion !== seenVersion) {
    setSeenVersion(outcome.rowVersion);
    setDraft(effective);
    setFqPerson(outcome.fqAccountable?.id ?? '');
    setAqPerson(outcome.aqAccountable?.id ?? '');
    setFqAdministrator(outcome.fqAdministrator?.id ?? '');
    setAwaitingReread(false);
  }
  const personNote = namedPersonNote(holders.status, holders.emails);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ text: string; kind: 'error' | 'success' } | null>(null);

  const changed =
    draft.fqAdviser !== effective.fqAdviser ||
    draft.fqParaplanner !== effective.fqParaplanner ||
    draft.aqAdviser !== effective.aqAdviser ||
    draft.aqParaplanner !== effective.aqParaplanner ||
    fqPerson !== (outcome.fqAccountable?.id ?? '') ||
    aqPerson !== (outcome.aqAccountable?.id ?? '') ||
    fqAdministrator !== (outcome.fqAdministrator?.id ?? '');

  function nameFor(who: 'adviser' | 'paraplanner'): string {
    const name = who === 'adviser' ? people.adviser : people.paraplanner;
    return name ?? `the ${who}`;
  }

  async function save() {
    setBusy(true);
    setMessage(null);

    /*
     * Naming a person on an untouched row must not record the pre-ticked default as a
     * judgement: that stops the server deriving, and a File Quality fail this page cannot see
     * would drop out of the extract. Four falses is "nobody has said yet", so it still derives.
     */
    const ticksMoved = LABELS.some(({ key }) => draft[key] !== effective[key]);
    const flags = source === 'derived' && !ticksMoved ? NOBODY : draft;

    /*
     * One key per intent, not per attempt. The token names the outcome AND what is being
     * recorded, so a retry after a timeout replays rather than writing twice, while
     * changing a tick and saving again is a different intent with its own key.
     */
    const token =
      'accountability-' +
      outcome.id +
      '-' +
      [flags.fqAdviser, flags.fqParaplanner, flags.aqAdviser, flags.aqParaplanner].join('-') +
      '-' + fqPerson + '-' + aqPerson + '-' + fqAdministrator;

    const result = await setFailAccountability({
      outcomeId: outcome.id,
      fqAdviser: flags.fqAdviser,
      fqParaplanner: flags.fqParaplanner,
      aqAdviser: flags.aqAdviser,
      aqParaplanner: flags.aqParaplanner,
      fqContactId: fqPerson,
      aqContactId: aqPerson,
      fqAdministratorId: fqAdministrator,
      idempotencyKey: intent.keyFor(token),
    });

    setBusy(false);

    if (!result.ok) {
      setMessage({ text: messageForFailure(result), kind: 'error' });
      return;
    }

    intent.release(token);
    setAwaitingReread(true);
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
          paraplanner carries a File Quality fail, and the adviser carries Insufficient
          evidence or Potential harm. The File Quality half is not shown ticked here because this page does not
          read the file quality answer &mdash; the extract does. Ticking anything below
          records a judgement instead, and the default stops applying to this check.
        </p>
      ) : null}

      <p className="accountability__note">
        The extract names someone only for a File Quality Fail, Insufficient evidence or
        Potential harm. Ticks against any other grade are kept here but not exported.
      </p>

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

      {/*
        Naming someone the case does not (project owner, 2026-09-19). The extract has only
        an "adviser" slot and a "paraplanner" slot, so the ticks above still say WHICH of
        them this person fills; this says whose name goes in it. Left empty, the extract
        uses the case's own adviser or paraplanner.
      */}
      {personNote ? (
        <p className="accountability__note" role="status">
          {personNote}
        </p>
      ) : (
        <div className="accountability__people">
          <label className="accountability__person">
            <span>File Quality &mdash; named person</span>
            <UserPicker
              field="id"
              value={fqPerson}
              onChange={setFqPerson}
              onlyEmails={holders.emails}
              includeInactive
              placeholder={outcome.fqAccountable?.name ?? "The case's own adviser or paraplanner"}
            />
          </label>
          <label className="accountability__person">
            <span>Advice Quality &mdash; named person</span>
            <UserPicker
              field="id"
              value={aqPerson}
              onChange={setAqPerson}
              onlyEmails={holders.emails}
              includeInactive
              placeholder={outcome.aqAccountable?.name ?? "The case's own adviser or paraplanner"}
            />
          </label>
        </div>
      )}

      {/*
        Requirements log row 10: a File Quality fail can be the administrator's (an AML point,
        say). Any contact can be chosen - administrators hold neither the Adviser nor the
        Planner role - and the export never carries this person.
      */}
      <div className="accountability__people">
        <label className="accountability__person">
          <span>File Quality &mdash; administrator at fault</span>
          <UserPicker
            field="id"
            value={fqAdministrator}
            onChange={setFqAdministrator}
            includeInactive
            placeholder={outcome.fqAdministrator?.name ?? 'No administrator named'}
          />
        </label>
      </div>
      <p className="accountability__note">
        The administrator is recorded for reporting only and is not written to the export. Naming
        one stops the paraplanner being named by default for the File Quality fail.
      </p>

      <div className="accountability__actions">
        <button
          type="button"
          className="accountability__save"
          disabled={recordDisabled({ changed, busy, awaitingReread })}
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
  state,
  people,
  onSaved,
}: {
  /** The page's read of the case's outcomes, shared with the outcome summary. */
  state: CaseOutcomeState;
  people: PeopleNames;
  // The page re-reads after a save, so the row flips from "Derived by default" to "Recorded"
  // and the ticks shown are the ones the extract will now use.
  onSaved: () => void;
}) {
  return (
    <PermissionGate resource="page.cases" need="Edit">
      <AccountabilitySection state={state} people={people} onSaved={onSaved} />
    </PermissionGate>
  );
}

/** Inside the gate, so the role holders are read only for someone who can record. */
function AccountabilitySection({
  state,
  people,
  onSaved,
}: {
  state: CaseOutcomeState;
  people: PeopleNames;
  onSaved: () => void;
}) {
  // One read of the role holders for every outcome on the case (owner, 2026-11-17: a
  // contact is an adviser or paraplanner by the role assigned to them).
  const security = useSecurityConfig(0);
  const holders = useMemo<NameableHolders>(
    () => ({
      status: security.status,
      emails: accountableEmails(security.status === 'ready' ? security.mappings : []),
    }),
    [security],
  );

  return (
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
              /*
               * Keyed on the outcome alone. It used to carry reloadKey, which onSaved bumps
               * the instant the write returns - so React tore the editor down and built it
               * again while the re-read was still in flight, seeding the new one from the
               * outcome it already had. The ticks and the named people sprang back to what
               * they were before the save, the success message went with the old instance,
               * and both stayed wrong until the page was left and returned to (F29).
               *
               * The draft now follows the record instead, below, which happens when the
               * data actually lands rather than when the request is sent.
               */
              key={outcome.id}
              outcome={outcome}
              people={people}
              holders={holders}
              onSaved={onSaved}
            />
          ))}
        </ul>
      ) : null}
    </section>
  );
}
