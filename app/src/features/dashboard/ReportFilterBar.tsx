import { useMemo } from 'react';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { REVIEW_ROUTES } from '../../types/domain';
import type { CaseSummary } from '../cases/caseWorklistMapping';
import {
  distinctNames,
  isFiltered,
  type ReportFilterKey,
  type ReportFilters,
} from './reportFilters';

interface Props {
  idPrefix: string;
  cases: CaseSummary[];
  inScope: number;
  filters: ReportFilters;
  onChange: (key: ReportFilterKey, value: string) => void;
  onClear: () => void;
}

/** The worklist's filters that make sense for totals: import dates, route, adviser, checker. */
export function ReportFilterBar({ idPrefix, cases, inScope, filters, onChange, onClear }: Props) {
  const advisers = useMemo(() => distinctNames(cases.map((c) => c.adviser)), [cases]);
  const checkers = useMemo(
    () => distinctNames(cases.flatMap((c) => [c.taxChecker, c.aqsChecker])),
    [cases],
  );

  return (
    <FilterBar
      summary={`Counting ${inScope} of ${cases.length} cases`}
      onClear={onClear}
      clearDisabled={!isFiltered(filters)}
    >
      <FilterField label="Imported from" htmlFor={`${idPrefix}-from`}>
        <input
          id={`${idPrefix}-from`}
          type="date"
          value={filters.from}
          onChange={(e) => onChange('from', e.target.value)}
        />
      </FilterField>
      <FilterField label="Imported to" htmlFor={`${idPrefix}-to`}>
        <input
          id={`${idPrefix}-to`}
          type="date"
          value={filters.to}
          onChange={(e) => onChange('to', e.target.value)}
        />
      </FilterField>
      <FilterField label="Route" htmlFor={`${idPrefix}-route`}>
        <select
          id={`${idPrefix}-route`}
          value={filters.route}
          onChange={(e) => onChange('route', e.target.value)}
        >
          <option value="">All routes</option>
          {REVIEW_ROUTES.map((route) => (
            <option key={route} value={route}>
              {route}
            </option>
          ))}
          <option value="none">Not routed</option>
        </select>
      </FilterField>
      <FilterField label="Adviser" htmlFor={`${idPrefix}-adviser`}>
        <select
          id={`${idPrefix}-adviser`}
          value={filters.adviser}
          onChange={(e) => onChange('adviser', e.target.value)}
        >
          <option value="">All advisers</option>
          {advisers.map((name) => (
            <option key={name} value={name}>
              {name}
            </option>
          ))}
        </select>
      </FilterField>
      <FilterField label="Checker" htmlFor={`${idPrefix}-checker`}>
        <select
          id={`${idPrefix}-checker`}
          value={filters.checker}
          onChange={(e) => onChange('checker', e.target.value)}
        >
          <option value="">All checkers</option>
          {checkers.map((name) => (
            <option key={name} value={name}>
              {name}
            </option>
          ))}
        </select>
      </FilterField>
    </FilterBar>
  );
}
