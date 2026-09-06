import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { Notice } from '../../components/feedback/Notice';
import { useIntentKeys } from '../../hooks/useIntentKey';
import type { DirectoryUser } from '../../hooks/useUserDirectory';
import { messageForFailure } from '../../services/errors';
import { createUser, updateUser } from '../../services/commands/users';
import './PeopleAdmin.css';

/**
 * Add and edit dialogs for the people directory, lifted out of the old Users admin page
 * when that page merged into People. They keep the `users__*` class names, and their
 * stylesheet moved alongside them, so the merge changed which screen hosts these
 * controls without restyling them.
 */

export function CreatePersonModal({
  onClose,
  onDone,
}: {
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intent = useIntentKeys();

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (!name.trim()) {
      setError('Enter the person’s full name.');
      return;
    }
    if (!email.trim()) {
      setError('Enter a work email.');
      return;
    }
    setBusy(true);
    setError(null);
    const result = await createUser({
      fullName: name.trim(),
      workEmail: email.trim(),
      idempotencyKey: intent.keyFor('create'),
    });
    setBusy(false);
    if (result.ok) {
      intent.release('create');
      onDone(`${name.trim()} added to the people directory.`);
    } else {
      setError(messageForFailure(result));
    }
  }

  return (
    <Modal title="Add person" onClose={onClose}>
      {error ? <Notice tone="error">{error}</Notice> : null}
      <form className="users__form" onSubmit={onSubmit}>
        <label className="users__field">
          <span>Full name</span>
          <input type="text" value={name} onChange={(e) => setName(e.target.value)} autoComplete="off" />
        </label>
        <label className="users__field">
          <span>Work email</span>
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="person@ascotlloyd.co.uk"
            autoComplete="off"
          />
        </label>
        <div className="users__form-actions">
          <button
            type="button"
            className="users__btn users__btn--ghost"
            onClick={onClose}
            disabled={busy}
          >
            Cancel
          </button>
          <button type="submit" className="users__btn" disabled={busy}>
            {busy ? 'Adding…' : 'Add person'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

export function EditPersonModal({
  user,
  onClose,
  onDone,
}: {
  user: DirectoryUser;
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const [name, setName] = useState(user.name);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intent = useIntentKeys();

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (!name.trim()) {
      setError('Enter the person’s full name.');
      return;
    }
    setBusy(true);
    setError(null);
    const result = await updateUser({
      userId: user.id,
      fullName: name.trim(),
      expectedRowVersion: user.rowVersion,
      idempotencyKey: intent.keyFor(user.id),
    });
    setBusy(false);
    if (result.ok) {
      intent.release(user.id);
      onDone(`${name.trim()} updated.`);
    } else {
      setError(messageForFailure(result));
    }
  }

  return (
    <Modal title="Edit person" onClose={onClose}>
      {error ? <Notice tone="error">{error}</Notice> : null}
      <form className="users__form" onSubmit={onSubmit}>
        <label className="users__field">
          <span>Full name</span>
          <input type="text" value={name} onChange={(e) => setName(e.target.value)} autoComplete="off" />
        </label>
        <label className="users__field">
          <span>Work email</span>
          <input type="email" value={user.email} readOnly disabled />
          <small className="users__hint">Work email is the stable identifier and cannot be changed here.</small>
        </label>
        <div className="users__form-actions">
          <button
            type="button"
            className="users__btn users__btn--ghost"
            onClick={onClose}
            disabled={busy}
          >
            Cancel
          </button>
          <button type="submit" className="users__btn" disabled={busy}>
            {busy ? 'Saving…' : 'Save changes'}
          </button>
        </div>
      </form>
    </Modal>
  );
}
