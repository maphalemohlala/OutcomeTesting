import { useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  EMPTY_REPORT_FILTERS,
  REPORT_FILTER_KEYS,
  type ReportFilterKey,
  type ReportFilters,
} from './reportFilters';

/**
 * The report filters, read from the URL the way the worklist reads its own, so a filtered
 * dashboard can be shared and a figure on it can hand the same filters to the worklist.
 */
export function useReportFilters(): [ReportFilters, (key: ReportFilterKey, value: string) => void, () => void] {
  const [params, setParams] = useSearchParams();

  const filters = useMemo(
    () =>
      Object.fromEntries(
        REPORT_FILTER_KEYS.map((key) => [key, params.get(key) ?? EMPTY_REPORT_FILTERS[key]]),
      ) as ReportFilters,
    [params],
  );

  function set(key: ReportFilterKey, value: string) {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next, { replace: true });
  }

  function clear() {
    setParams(new URLSearchParams(), { replace: true });
  }

  return [filters, set, clear];
}
