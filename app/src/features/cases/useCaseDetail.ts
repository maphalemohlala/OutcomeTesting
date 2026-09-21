import { useEffect, useState } from 'react';
import {
  Al_listoption_al_outcomecase_productssetService,
  Al_outcomecasesService,
} from '../../generated';
import { logTechnical } from '../../services/errors';
import { toDetail } from './caseDetailMapping';
import type { CaseDetail } from './caseDetailMapping';

export type { CaseDetail, CaseEditValues } from './caseDetailMapping';

export type CaseDetailState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | { status: 'ready'; detail: CaseDetail };

/**
 * Reads a single Outcome Case by id from Dataverse. Row-level visibility is enforced
 * by Dataverse security (BR-012): a case the signed-in user may not see returns as
 * unavailable rather than showing partial data.
 */
export function useCaseDetail(caseId: string | undefined, reloadKey = 0): CaseDetailState {
  const [state, setState] = useState<CaseDetailState>({ status: 'loading' });
  const [loadedFor, setLoadedFor] = useState<string | undefined>(caseId);

  // Reset to loading when the route's case changes, without a setState-in-effect.
  if (caseId !== loadedFor) {
    setLoadedFor(caseId);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    if (!caseId) return;
    let cancelled = false;

    /*
     * The products a case covers are a many-to-many through
     * al_listoption_al_outcomecase_products, so they cannot be read off the case row. The
     * generated client has no $expand - IGetAllOptions is select/filter/orderBy/top/skip -
     * so the intersect is queried directly and joined here, which is what useReviewDetail
     * already does for fail reasons and what ListOptionRules does server-side.
     *
     * A failure to read the products is NOT a failure to read the case: the case still
     * renders, with no products shown. Losing the whole page over one join would be a worse
     * trade than showing a header field empty.
     */
    Promise.all([
      Al_outcomecasesService.get(caseId),
      Al_listoption_al_outcomecase_productssetService.getAll({
        filter: `al_outcomecaseid eq ${caseId}`,
        top: 200,
      }).catch((error) => {
        logTechnical('case products load', error);
        return { success: false as const, data: undefined };
      }),
    ])
      .then(([result, productResult]) => {
        if (cancelled) return;
        if (!result.success || !result.data) {
          if (!result.success) {
            logTechnical('case detail load', result.error);
          }
          setState({
            status: 'unavailable',
            reason:
              'The selected case could not be found. It may have been removed, or you may not have access to it.',
          });
          return;
        }
        const productIds =
          productResult.success && productResult.data
            ? productResult.data.map((link) => link.al_listoptionid)
            : [];

        setState({ status: 'ready', detail: toDetail(result.data, productIds) });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('case detail load', error);
        setState({
          status: 'unavailable',
          reason: 'This case could not be loaded right now. Please try again later.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [caseId, reloadKey]);

  if (!caseId) {
    return { status: 'unavailable', reason: 'No case was requested.' };
  }

  return state;
}
