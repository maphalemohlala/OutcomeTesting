import { Modal } from '../../components/feedback/Modal';
import { UserPicker } from '../../components/form/UserPicker';
import { ACCESS_LEVELS, RESOURCE_KEYS } from '../../types/permissions';
import type { RoleRow } from './useRoles';

export type Notice = { tone: 'ok' | 'error'; message: string } | null;

export interface RoleOption {
  value: string;
  label: string;
  custom: boolean;
}

function NoticeLine({ notice }: { notice: Notice }) {
  if (!notice) return null;
  return (
    <p className={`security__notice security__notice--${notice.tone}`} role="status">
      {notice.message}
    </p>
  );
}

/**
 * The role select the assign and permission forms share.
 *
 * A caller with one role to offer — the role detail screen, where the role is the subject
 * of the page — passes a single option, and the control states the role rather than
 * presenting a choice of one.
 */
function RoleField({
  options,
  value,
  onChange,
}: {
  options: RoleOption[];
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="security__field">
      <span>Role</span>
      {options.length === 1 ? (
        <input type="text" value={options[0].label} readOnly />
      ) : (
        <select value={value} onChange={(e) => onChange(e.target.value)}>
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      )}
    </label>
  );
}

export interface AssignRoleModalProps {
  title: string;
  notice: Notice;
  email: string;
  onEmailChange: (value: string) => void;
  /** True when changing an existing assignment: the person is fixed, the role is not. */
  emailReadOnly: boolean;
  roleOptions: RoleOption[];
  selectedRole: string;
  onRoleChange: (value: string) => void;
  /** The role being replaced, when this form is changing someone's existing assignment. */
  replacingRole: string | null;
  busy: boolean;
  submitLabel: string;
  onSubmit: (event: React.FormEvent) => void;
  onClose: () => void;
}

export function AssignRoleModal(props: AssignRoleModalProps) {
  return (
    <Modal title={props.title} onClose={props.onClose}>
      <NoticeLine notice={props.notice} />
      <form className="security__form" onSubmit={props.onSubmit}>
        <label className="security__field">
          <span>Person</span>
          {/*
            Chosen from the registry rather than typed. An assignment is keyed on work email
            (AD-010), and a typo in a free-text box produced a row that matched nobody: the
            grant looked made, the person still had no access, and the mistake was only
            visible by reading the assignments table character by character. The same picker
            the case person fields use, so the registry is the one source for both.

            Changing someone's role keeps the read-only line: the person is fixed there and
            the role is what the form is for.
          */}
          {props.emailReadOnly ? (
            <input type="email" value={props.email} readOnly />
          ) : (
            <UserPicker
              field="email"
              value={props.email}
              onChange={props.onEmailChange}
              placeholder="Select a person"
            />
          )}
        </label>
        <RoleField
          options={props.roleOptions}
          value={props.selectedRole}
          onChange={props.onRoleChange}
        />
        {props.replacingRole ? (
          <p className="security__hint">
            The {props.replacingRole} assignment is withdrawn once the new role is granted. The
            withdrawn record is kept for the audit trail.
          </p>
        ) : null}
        <div className="security__form-actions">
          <button
            type="button"
            className="security__btn security__btn--ghost"
            onClick={props.onClose}
            disabled={props.busy}
          >
            Cancel
          </button>
          <button type="submit" className="security__btn" disabled={props.busy}>
            {props.busy ? 'Saving…' : props.submitLabel}
          </button>
        </div>
      </form>
    </Modal>
  );
}

export interface PermissionModalProps {
  notice: Notice;
  roleOptions: RoleOption[];
  selectedRole: string;
  onRoleChange: (value: string) => void;
  resource: string;
  onResourceChange: (value: string) => void;
  level: string;
  onLevelChange: (value: string) => void;
  busy: boolean;
  onSubmit: (event: React.FormEvent) => void;
  onClose: () => void;
}

export function PermissionModal(props: PermissionModalProps) {
  return (
    <Modal title="Set a page or capability permission" onClose={props.onClose}>
      <NoticeLine notice={props.notice} />
      <form className="security__form" onSubmit={props.onSubmit}>
        <RoleField
          options={props.roleOptions}
          value={props.selectedRole}
          onChange={props.onRoleChange}
        />
        <label className="security__field">
          <span>Resource</span>
          <select value={props.resource} onChange={(e) => props.onResourceChange(e.target.value)}>
            {RESOURCE_KEYS.map((key) => (
              <option key={key} value={key}>
                {key}
              </option>
            ))}
          </select>
        </label>
        <label className="security__field">
          <span>Access level</span>
          <select value={props.level} onChange={(e) => props.onLevelChange(e.target.value)}>
            {ACCESS_LEVELS.map((lvl) => (
              <option key={lvl} value={lvl}>
                {lvl}
              </option>
            ))}
          </select>
        </label>
        <p className="security__hint">
          Setting a level for a role and resource replaces any existing rule for that pair.
        </p>
        <div className="security__form-actions">
          <button
            type="button"
            className="security__btn security__btn--ghost"
            onClick={props.onClose}
            disabled={props.busy}
          >
            Cancel
          </button>
          <button type="submit" className="security__btn" disabled={props.busy}>
            {props.busy ? 'Saving…' : 'Set permission'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

export interface RoleFormModalProps {
  /** The role being edited, or null when creating one. */
  editing: RoleRow | null;
  notice: Notice;
  name: string;
  onNameChange: (value: string) => void;
  description: string;
  onDescriptionChange: (value: string) => void;
  busy: boolean;
  onSubmit: (event: React.FormEvent) => void;
  onClose: () => void;
}

export function RoleFormModal(props: RoleFormModalProps) {
  const { editing } = props;
  return (
    <Modal title={editing ? `Edit ${editing.name}` : 'Create a role'} onClose={props.onClose}>
      <NoticeLine notice={props.notice} />
      <form className="security__form" onSubmit={props.onSubmit}>
        <label className="security__field">
          <span>Role name</span>
          <input
            type="text"
            value={props.name}
            onChange={(e) => props.onNameChange(e.target.value)}
            placeholder="e.g. Senior Checker"
            autoComplete="off"
            // A web role's name IS its code (AD-087): every assignment and permission rule
            // references it by name, so al_UpdateRole refuses a rename. Read-only here so
            // the form does not offer an edit the server will refuse.
            readOnly={Boolean(editing)}
            aria-readonly={editing ? true : undefined}
          />
        </label>
        <label className="security__field">
          <span>Description</span>
          <textarea
            value={props.description}
            onChange={(e) => props.onDescriptionChange(e.target.value)}
            rows={2}
            placeholder="What the role is for"
          />
        </label>
        {editing ? (
          <p className="security__hint">
            The role name <strong>{editing.code}</strong> is its code and cannot be changed: existing
            assignments and permission rules reference it by name. To rename, create a new role and
            move the assignments across.
          </p>
        ) : null}
        <div className="security__form-actions">
          <button
            type="button"
            className="security__btn security__btn--ghost"
            onClick={props.onClose}
            disabled={props.busy}
          >
            Cancel
          </button>
          <button type="submit" className="security__btn" disabled={props.busy}>
            {props.busy ? 'Saving…' : editing ? 'Save changes' : 'Create role'}
          </button>
        </div>
      </form>
    </Modal>
  );
}
