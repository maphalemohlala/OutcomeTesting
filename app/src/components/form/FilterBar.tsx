import type { ReactNode } from 'react';
import './FilterBar.css';

interface Props {
  children: ReactNode;
  /** e.g. "12 of 40 cases". */
  summary?: string;
  /** Rendered only when there is something to clear. */
  onClear?: () => void;
  clearDisabled?: boolean;
}

/** Filter toolbar shared by the list pages. Presentation only; never a security boundary. */
export function FilterBar({ children, summary, onClear, clearDisabled }: Props) {
  return (
    <div className="filter-bar" role="group" aria-label="Filters">
      <div className="filter-bar__controls">{children}</div>
      <div className="filter-bar__meta">
        {summary ? (
          <span className="filter-bar__summary" role="status">
            {summary}
          </span>
        ) : null}
        {onClear ? (
          <button
            type="button"
            className="filter-bar__clear"
            onClick={onClear}
            disabled={clearDisabled}
          >
            Clear filters
          </button>
        ) : null}
      </div>
    </div>
  );
}

interface FieldProps {
  label: string;
  htmlFor: string;
  children: ReactNode;
}

/** A single labelled filter control. */
export function FilterField({ label, htmlFor, children }: FieldProps) {
  return (
    <div className="filter-bar__field">
      <label htmlFor={htmlFor}>{label}</label>
      {children}
    </div>
  );
}
