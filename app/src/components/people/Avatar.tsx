import { initials } from '../layout/shellLabels';
import { avatarTone } from './avatarTone';
import './Avatar.css';

/** Initials in a tinted circle. Decorative: the name always sits beside it. */
export function Avatar({ name, size = 'md' }: { name: string; size?: 'sm' | 'md' }) {
  const label = name.trim() || '?';
  return (
    <span className={`avatar avatar--${size}`} data-tone={avatarTone(label)} aria-hidden="true">
      {label === '?' ? '?' : initials(label)}
    </span>
  );
}

/** Avatar, name and an optional second line (an email, a role). */
export function PersonCell({ name, detail }: { name: string; detail?: string | null }) {
  return (
    <span className="person-cell">
      <Avatar name={name} />
      <span className="person-cell__text">
        <span className="person-cell__name">{name}</span>
        {detail ? <span className="person-cell__detail">{detail}</span> : null}
      </span>
    </span>
  );
}
