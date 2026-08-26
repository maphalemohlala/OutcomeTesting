import { PageIntro } from '../layout/PageIntro';
import './NotBuiltYet.css';

interface NotBuiltYetProps {
  title: string;
  purpose: string;
  /** Requirement or open-decision IDs that must be settled before this screen can be built. */
  blockedBy: string[];
}

export function NotBuiltYet({ title, purpose, blockedBy }: NotBuiltYetProps) {
  return (
    <>
      <PageIntro title={title} purpose={purpose} />
      <section className="not-built" aria-labelledby="not-built-heading">
        <h2 id="not-built-heading">Not built yet</h2>
        <p>
          This screen has no data source. The Dataverse tables it depends on have not been
          created, so nothing is shown rather than showing placeholder records.
        </p>
        <p className="not-built__blockers">
          Waiting on: <span>{blockedBy.join(', ')}</span>
        </p>
      </section>
    </>
  );
}
