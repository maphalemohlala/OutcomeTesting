import type { CaseChecklist } from './checklistForm';
import './ChecklistSection.css';

/**
 * The IO task's checklist items - High Risk Item 1, Enhanced Supervision, Tax Check and
 * the rest - as the paraplanner ticked them on the upload (project owner, 2026-09-19).
 * Not the checklist version's questions: those are the form itself, and a checker reading
 * the form already has them. This says what had put the case in front of them. The change
 * batch originally asked for the version's questions grouped by section; the owner
 * corrected the requirement to what is built here on 2026-09-20 (AD-158), and
 * checklistRender.test.tsx pins it.
 *
 * Read-only, and only the ticked items: the case stores what was selected, not the
 * vocabulary it was selected from, so there is no "not applicable" row to draw.
 *
 * The items and nothing else. It carried an explanatory line and a "completed by" stamp;
 * both were scaffolding around the one thing it is opened for. `CaseChecklist` still
 * parses the stamp, because the hook reads it off a record it already holds and a later
 * screen may want it - it is simply not drawn here.
 *
 * Renders nothing when the case names none. That is a real state - a case imported before
 * the extract carried the columns - and a heading over an empty list reads as a fault.
 *
 * Shared by the review page and the case page rather than copied into each (project owner,
 * 2026-09-19), which is also why the class names are not prefixed `review__`. Mirrors the
 * portal's OT Checklist Items web template, included by both of its pages for the same
 * reason.
 */
export function ChecklistSection({ checklist }: { checklist: CaseChecklist | null }) {
  if (!checklist || checklist.items.length === 0) return null;

  return (
    <section className="checklist-items" aria-labelledby="checklist-items-heading">
      <h2 id="checklist-items-heading" className="checklist-items__heading">
        Checklist Items
      </h2>
      <ul className="checklist-items__list">
        {checklist.items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </section>
  );
}
