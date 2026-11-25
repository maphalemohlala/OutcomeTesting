import './Skeleton.css';

/**
 * Placeholder shape shown while something loads. The label is read out to a screen reader in
 * place of the old "Loading…" text; sighted users see the grey bars. `page` adds a title bar and
 * more rows, for whole-screen waits (access check, route load, a page's first read).
 */
export function Skeleton({ label, rows, page = false }: { label: string; rows?: number; page?: boolean }) {
  const count = rows ?? (page ? 6 : 3);
  return (
    <div className={page ? 'skeleton skeleton--page' : 'skeleton'} role="status" aria-busy="true">
      <span className="visually-hidden">{label}</span>
      {page ? <span className="skeleton__bar skeleton__bar--title" aria-hidden="true" /> : null}
      {Array.from({ length: count }, (_, i) => (
        <span key={i} className="skeleton__bar" aria-hidden="true" />
      ))}
    </div>
  );
}
