import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { ExportMenu } from '../../components/export/ExportMenu';
import { OUTCOMES } from '../../types/domain';
import { useCaseWorklist } from '../cases/useCaseWorklist';
import { buildDirectory, PERSON_ROLES, type PersonRole } from './peopleDirectory';
import './PeoplePage.css';

const EXPORT_HEADERS = [
  'Name',
  'Position',
  'Code',
  'Cases',
  'Open',
  'Closed',
  ...OUTCOMES,
  'Not yet graded',
  'Oldest open (days)',
];

export function PeoplePage() {
  const state = useCaseWorklist();
  const [search, setSearch] = useState('');
  const [role, setRole] = useState<PersonRole | ''>('');

  const directory = useMemo(
    () => buildDirectory(state.status === 'ready' ? state.cases : []),
    [state],
  );

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return directory.filter(
      (person) =>
        (!role || person.role === role) &&
        (!term || `${person.name} ${person.code ?? ''}`.toLowerCase().includes(term)),
    );
  }, [directory, search, role]);

  const isFiltered = search.trim() !== '' || role !== '';

  return (
    <>
      <PageIntro
        title="People"
        purpose="See who is named on cases, how much they carry and how their checks were graded. Select a person to open every case they appear on."
        actions={
          filtered.length > 0 ? (
            <ExportMenu
              label="Export people"
              stem="outcome-people"
              sheetName="People"
              headers={EXPORT_HEADERS}
              rows={filtered.map((person) => [
                person.name,
                person.role,
                person.code ?? '',
                person.totalCases,
                person.openCases,
                person.closedCases,
                ...OUTCOMES.map((outcome) => person.outcomes[outcome]),
                person.notGraded,
                person.oldestOpenDays,
              ])}
              caption={`Exports the ${filtered.length} person row${filtered.length === 1 ? '' : 's'} currently listed`}
            />
          ) : null
        }
      />

      {state.status === 'loading' ? <p role="status">Loading people…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="people__unavailable" aria-labelledby="people-unavailable">
          <h2 id="people-unavailable">People cannot be listed</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <>
          <p className="people__note">
            Built from the names recorded on cases — adviser, paraplanner and checker come from
            the intake extract, owner is the person the case is allocated to (BR-003). This is not
            the application user registry; manage sign-in accounts under Users.
          </p>

          <FilterBar
            summary={`${filtered.length} of ${directory.length} people`}
            onClear={() => {
              setSearch('');
              setRole('');
            }}
            clearDisabled={!isFiltered}
          >
            <FilterField label="Search" htmlFor="people-search">
              <input
                id="people-search"
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Name or code"
              />
            </FilterField>
            <FilterField label="Position" htmlFor="people-role">
              <select
                id="people-role"
                value={role}
                onChange={(e) => setRole(e.target.value as PersonRole | '')}
              >
                <option value="">All positions</option>
                {PERSON_ROLES.map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </select>
            </FilterField>
          </FilterBar>

          <div className="people__scroll">
            <table className="people">
              <caption className="visually-hidden">People named on cases, busiest first</caption>
              <thead>
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col">Position</th>
                  <th scope="col">Code</th>
                  <th scope="col" className="people__numeric">
                    Cases
                  </th>
                  <th scope="col" className="people__numeric">
                    Open
                  </th>
                  {OUTCOMES.map((outcome) => (
                    <th key={outcome} scope="col" className="people__numeric">
                      {outcome}
                    </th>
                  ))}
                  <th scope="col" className="people__numeric">
                    Not yet graded
                  </th>
                </tr>
              </thead>
              <tbody>
                {filtered.length === 0 ? (
                  <tr>
                    <td colSpan={6 + OUTCOMES.length} className="people__empty">
                      No people match your current filters.
                    </td>
                  </tr>
                ) : (
                  filtered.map((person) => (
                    <tr key={person.key}>
                      <th scope="row">
                        <Link to={`/people/${person.role}/${encodeURIComponent(person.name)}`}>
                          {person.name}
                        </Link>
                      </th>
                      <td>{person.role}</td>
                      <td>{person.code ?? '—'}</td>
                      <td className="people__numeric">{person.totalCases}</td>
                      <td className="people__numeric">{person.openCases}</td>
                      {OUTCOMES.map((outcome) => (
                        <td key={outcome} className="people__numeric">
                          {person.outcomes[outcome]}
                        </td>
                      ))}
                      <td className="people__numeric">{person.notGraded}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </>
      ) : null}
    </>
  );
}
