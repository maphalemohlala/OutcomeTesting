import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { Icon, type IconName } from '../../components/icons/Icon';
import { REVIEW_ROUTES } from '../../types/domain';
import type { CaseSummary } from '../cases/caseWorklistMapping';
import type { ContactEmails } from '../people/peopleDirectory';
import { dateFilterLabel, scopeFilterLabel } from './filterLabels';
import {
  adviserOptions,
  checkerOptions,
  isFiltered,
  type ReportFilterKey,
  type ReportFilters,
} from './reportFilters';

/** A button that opens a small panel below it; Escape or a press outside closes it. */
function FilterPopover({ icon, label, active, children }: { icon: IconName; label: string; active: boolean; children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const panelId = useId();

  useEffect(() => {
    if (!open) return;
    const onPointer = (event: PointerEvent) => {
      if (!box.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      setOpen(false);
      button.current?.focus();
    };
    document.addEventListener('pointerdown', onPointer);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('pointerdown', onPointer);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  return (
    <div className="dashboard__pop" ref={box}>
      <button
        ref={button}
        type="button"
        className="button-secondary"
        data-active={active || undefined}
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((value) => !value)}
      >
        <Icon name={icon} />
        {label}
      </button>
      <div id={panelId} className="dashboard__pop-panel" hidden={!open}>
        {children}
      </div>
    </div>
  );
}

interface Props {
  cases: CaseSummary[];
  inScope: number;
  filters: ReportFilters;
  onChange: (key: ReportFilterKey, value: string) => void;
  onClear: () => void;
  contactEmails: ContactEmails;
}

/** The dashboard's filters as two buttons: the import dates, and who or which route. */
export function DashboardFilters({ cases, inScope, filters, onChange, onClear, contactEmails }: Props) {
  const advisers = useMemo(() => adviserOptions(cases), [cases]);
  const checkers = useMemo(() => checkerOptions(cases, contactEmails), [cases, contactEmails]);
  const summary = `Counting ${inScope} of ${cases.length} cases`;
  const datesSet = Boolean(filters.from || filters.to);
  const scopeSet = Boolean(filters.route || filters.adviser || filters.checker);

  return (
    <div className="dashboard__filters">
      <FilterPopover icon="calendar" label={dateFilterLabel(filters)} active={datesSet}>
        <FilterBar summary={summary} onClear={onClear} clearDisabled={!isFiltered(filters)}>
          <FilterField label="Imported from" htmlFor="dashboard-from">
            <input id="dashboard-from" type="date" value={filters.from} onChange={(e) => onChange('from', e.target.value)} />
          </FilterField>
          <FilterField label="Imported to" htmlFor="dashboard-to">
            <input id="dashboard-to" type="date" value={filters.to} onChange={(e) => onChange('to', e.target.value)} />
          </FilterField>
        </FilterBar>
      </FilterPopover>
      <FilterPopover icon="sliders" label={scopeFilterLabel(filters)} active={scopeSet}>
        <FilterBar summary={summary} onClear={onClear} clearDisabled={!isFiltered(filters)}>
          <FilterField label="Route" htmlFor="dashboard-route">
            <select id="dashboard-route" value={filters.route} onChange={(e) => onChange('route', e.target.value)}>
              <option value="">All routes</option>
              {REVIEW_ROUTES.map((route) => (
                <option key={route} value={route}>
                  {route}
                </option>
              ))}
              <option value="none">Not routed</option>
            </select>
          </FilterField>
          <FilterField label="Adviser" htmlFor="dashboard-adviser">
            <select id="dashboard-adviser" value={filters.adviser} onChange={(e) => onChange('adviser', e.target.value)}>
              <option value="">All advisers</option>
              {advisers.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </FilterField>
          <FilterField label="Checker" htmlFor="dashboard-checker">
            <select id="dashboard-checker" value={filters.checker} onChange={(e) => onChange('checker', e.target.value)}>
              <option value="">All checkers</option>
              {checkers.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </FilterField>
        </FilterBar>
      </FilterPopover>
    </div>
  );
}
