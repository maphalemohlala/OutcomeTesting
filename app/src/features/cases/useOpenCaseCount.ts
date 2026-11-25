import { useEffect, useState } from 'react';
import { Al_outcomecasesService } from '../../generated';
import { readAll } from '../../services/readAll';

// Closed and No Check Required: the two statuses the dashboard does not count as open.
const CLOSED = 120910591;
const NO_CHECK = 120910592;

/**
 * How many cases the caller can see that are still open, for the menu's badge. Ids only, read
 * once when the shell mounts; null until it answers or when the read fails, and the badge then
 * stays away rather than showing a wrong number.
 * shortcut: not re-read after a case changes status; re-read on navigation if the badge must stay live.
 */
export function useOpenCaseCount(enabled: boolean): number | null {
  const [count, setCount] = useState<number | null>(null);

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    readAll((o) => Al_outcomecasesService.getAll(o), {
      select: ['al_outcomecaseid'],
      filter: `al_casestatus ne ${CLOSED} and al_casestatus ne ${NO_CHECK}`,
      orderBy: ['al_outcomecaseid asc'],
    })
      .then((result) => {
        if (!cancelled && result.success) setCount(result.data.length);
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [enabled]);

  return enabled ? count : null;
}
