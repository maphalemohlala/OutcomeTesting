import { useMemo, useState, type ReactNode } from 'react';
import { PermissionGate } from '../../app/permissions/PermissionGate';
import { setFailAccountability } from '../../services/commands/setFailAccountability';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { UserPicker } from '../../components/form/UserPicker';
import { Icon } from '../../components/icons/Icon';
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
import { Skeleton } from '../../components/feedback/Skeleton';

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
  heading,
}: {
  outcome: CaseOutcomeRow;
  people: PeopleNames;
  holders: NameableHolders;
  onSaved: () => void;
  /** The card's title, drawn in this row's head when the case has only the one outcome. */
  heading?: ReactNode;
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
  // Whether an administrator carries the File Quality fail (requirements log row 10). Never exported.
  const [fqAdministrator, setFqAdministrator] = useState(Boolean(outcome.fqAdministrator));

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
    setFqAdministrator(Boolean(outcome.fqAdministrator));
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
    fqAdministrator !== Boolean(outcome.fqAdministrator);

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
      fqAdministrator,
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

  const anyTicked = LABELS.some(({ key }) => draft[key]);
  const status = message?.text ?? (changed ? 'Not recorded yet' : anyTicked ? null : 'Nothing ticked yet');

  /** One tick: which part of the check, and whose name goes in its slot. */
  function tick(key: keyof AccountabilityFlags, discipline: string, who: 'adviser' | 'paraplanner') {
    // The default the extract falls back to while nobody has been named on this check.
    const isDefault =
      source === 'derived' &&
      ((key === 'fqParaplanner' && who === 'paraplanner') || (key === 'aqAdviser' && who === 'adviser'));
    return (
      <td>
        <span className="accountability__tick">
          <input
            type="checkbox"
            aria-label={`${discipline} — ${nameFor(who)}`}
            checked={draft[key]}
            disabled={busy}
            onChange={(event) => setDraft((current) => ({ ...current, [key]: event.target.checked }))}
          />
          {isDefault ? <span className="accountability__default">Default</span> : null}
        </span>
      </td>
    );
  }

  return (
    <li className="accountability__outcome">
      <div className="case-card__head">
        <span className="accountability__title">
          {heading}
          <span className="accountability__source" data-source={source}>
            {source === 'recorded' ? 'Recorded' : 'Derived by default'}
          </span>
        </span>
        <span className="case-card__aside">{outcome.reviewInstance ?? outcome.reference}</span>
      </div>

      {source === 'derived' ? (
        <p className="accountability__note">
          Nobody has been named on this check, so the extract uses the default. Tick anyone below
          to record a judgement instead; the default then stops applying to this check.
        </p>
      ) : null}

      <div className="accountability__scroll">
        <table className="accountability__table">
          <thead>
            <tr>
              <th scope="col">Part of the check</th>
              <th scope="col">
                Paraplanner
                <span className="accountability__who">{people.paraplanner ?? 'Not recorded'}</span>
              </th>
              <th scope="col">
                Adviser
                <span className="accountability__who">{people.adviser ?? 'Not recorded'}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <th scope="row">
                File Quality
                <span className="accountability__who">Exported for a File Quality fail</span>
              </th>
              {tick('fqParaplanner', 'File Quality', 'paraplanner')}
              {tick('fqAdviser', 'File Quality', 'adviser')}
            </tr>
            <tr>
              <th scope="row">
                Advice Quality
                <span className="accountability__who">Exported for Insufficient evidence or Potential harm</span>
              </th>
              {tick('aqParaplanner', 'Advice Quality', 'paraplanner')}
              {tick('aqAdviser', 'Advice Quality', 'adviser')}
            </tr>
          </tbody>
        </table>
      </div>

      <p className="accountability__note">
        The File Quality default is not shown ticked because this page does not read the file
        quality answer; the extract does. Ticks against any other grade are kept here but not
        exported.
      </p>

      {/*
        Naming someone the case does not (project owner, 2026-09-19). The extract has only
        an "adviser" slot and a "paraplanner" slot, so the ticks above still say WHICH of
        them this person fills; this says whose name goes in it. Left empty, the extract
        uses the case's own adviser or paraplanner.
      */}
      {personNote ? (
        <p
          className="accountability__banner"
          data-kind={holders.status === 'loading' ? 'info' : 'warn'}
          role="status"
        >
          {holders.status === 'loading' ? null : <Icon name="warning" />}
          {personNote}
        </p>
      ) : (
        <div className="accountability__people">
          <label className="accountability__person">
            <span>File Quality: named person</span>
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
            <span>Advice Quality: named person</span>
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
        say). An option, not a person, since 2026-11-25 (project owner), and never exported.
      */}
      <div className="accountability__admin">
        <label className="accountability__admin-tick">
          <input
            type="checkbox"
            checked={fqAdministrator}
            disabled={busy}
            onChange={(event) => setFqAdministrator(event.target.checked)}
          />
          <span>File Quality: an administrator</span>
        </label>
        <p className="accountability__note">
          Recorded for reporting only and not written to the export. Ticking it stops the
          paraplanner being named by default for the File Quality fail.
        </p>
      </div>

      <div className="accountability__actions">
        <span
          className="accountability__message"
          data-kind={message?.kind ?? 'idle'}
          role="status"
        >
          {status}
        </span>
        <button
          type="button"
          className="button-primary"
          disabled={recordDisabled({ changed, busy, awaitingReread })}
          onClick={save}
        >
          {busy ? 'Recording…' : 'Record accountability'}
        </button>
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

  const title = <h2 id="panel-accountability">Who carries a fail</h2>;
  // One outcome: the title shares its row with the source chip and the check, as one card head.
  const single = state.status === 'ready' && state.outcomes.length === 1;

  return (
    <section className="accountability" aria-labelledby="panel-accountability">
      {single ? null : title}

      {state.status === 'loading' ? <Skeleton label="Loading outcomes…" /> : null}

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
              heading={single ? title : null}
            />
          ))}
        </ul>
      ) : null}
    </section>
  );
}
