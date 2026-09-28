import { useEffect, useState } from 'react';
import { Al_auditeventsService, Al_sectionsService } from '../../generated';
import { logTechnical } from '../../services/errors';
import { isRecordId } from '../../services/odata';
import { sectionHistory, type HistorySectionSource } from './sectionHistory';
import type { HistoryState } from './useQuestionHistory';

const UNAVAILABLE =
  'The history for this section could not be loaded. You may not have access to the audit trail.';

/**
 * Reads one section and the audit events written against it, and hands both to
 * sectionHistory, which holds the rules and is tested there. The row is read as well as the
 * events because a seeded section has no AddSection event to say who created it.
 */
export function useSectionHistory(sectionId: string): HistoryState {
  const [state, setState] = useState<HistoryState>({ status: 'loading' });

  useEffect(() => {
    // Interpolated into two filters, one inside quotes (services/odata.ts).
    if (!isRecordId(sectionId)) return;
    let cancelled = false;

    Promise.all([
      Al_sectionsService.getAll({ filter: `al_sectionid eq ${sectionId}`, top: 1 }),
      Al_auditeventsService.getAll({ filter: `al_targetid eq '${sectionId}'`, top: 500 }),
    ])
      .then(([sections, events]) => {
        if (cancelled) return;
        if (!sections.success || !events.success || sections.data.length === 0) {
          logTechnical('section history load', sections.success ? events.error : sections.error);
          setState({ status: 'unavailable', reason: UNAVAILABLE });
          return;
        }
        // al_effectiveto postdates the generated Al_sections model (AD-123); the library
        // declares the same columns for the same reason.
        const section = sections.data[0] as unknown as HistorySectionSource;
        setState({ status: 'ready', entries: sectionHistory(section, events.data) });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('section history load', error);
        setState({ status: 'unavailable', reason: UNAVAILABLE });
      });

    return () => {
      cancelled = true;
    };
  }, [sectionId]);

  return isRecordId(sectionId) ? state : { status: 'unavailable', reason: UNAVAILABLE };
}
