import { PageIntro } from '../layout/PageIntro';
import './PageUnavailable.css';

interface PageUnavailableProps {
  title: string;
  purpose: string;
  /** The heading over the explanation, e.g. "No access". */
  heading: string;
  /** Why this particular person is seeing this particular page, in their terms. */
  detail: string;
  /** What would have to change for the page to open. */
  blockedBy: string[];
}

/**
 * A page somebody reached but cannot use, and the reason why.
 *
 * This was `NotBuiltYet`, and its explanation was one fixed sentence, hard-coded in the
 * body: the screen had no data source because the tables behind it did not exist yet. True
 * of the screens it was written for. By 2026-09-21 it had no such callers left — the only
 * two were the permission refusal and the not-found page — and it was telling both of them
 * something false.
 *
 * F42, seen while working the role-separation rows. A Tax Reviewer who opened Exports was
 * told, in the same panel and one line apart, to ask an administrator for the role AND that
 * the deployment was unfinished. The second half sends them, and whoever they ask, hunting
 * a broken environment instead of a missing role. Somebody who cannot get in should not have
 * to work out which half of the answer is the real one.
 *
 * So the explanation is a required prop. There is no default, deliberately: a default is how
 * the wrong sentence reached two callers that never asked for it.
 */
export function PageUnavailable({
  title,
  purpose,
  heading,
  detail,
  blockedBy,
}: PageUnavailableProps) {
  return (
    <>
      <PageIntro title={title} purpose={purpose} />
      <section className="page-unavailable" aria-labelledby="page-unavailable-heading">
        <h2 id="page-unavailable-heading">{heading}</h2>
        <p>{detail}</p>
        <p className="page-unavailable__blockers">
          Waiting on: <span>{blockedBy.join(', ')}</span>
        </p>
      </section>
    </>
  );
}
