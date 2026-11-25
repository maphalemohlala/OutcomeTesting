import './Pager.css';

interface PagerProps {
  from: number;
  to: number;
  total: number;
  page: number;
  pages: number;
  setPage: (page: number) => void;
  /** e.g. "cases"; the summary reads "Showing 1 to 25 of 103". */
  label: string;
}

export function Pager({ from, to, total, page, pages, setPage, label }: PagerProps) {
  return (
    <nav className="pager" aria-label={`Pages of ${label}`}>
      <span className="pager__summary" role="status">
        {total === 0 ? `No ${label}` : `Showing ${from} to ${to} of ${total}`}
      </span>
      {pages > 1 ? (
        <span className="pager__buttons">
          <button type="button" className="button-secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>
            Previous
          </button>
          <button type="button" className="button-secondary" disabled={page >= pages} onClick={() => setPage(page + 1)}>
            Next
          </button>
        </span>
      ) : null}
    </nav>
  );
}
