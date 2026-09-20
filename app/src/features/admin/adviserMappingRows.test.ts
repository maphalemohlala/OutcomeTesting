import { describe, expect, it } from 'vitest';
import { managerNameFor, toMappingRows, type ContactOption } from './adviserMappingRows';

/**
 * F16, found in DEV on 2026-09-20. A mapping was saved with a T&C Manager, Dataverse held
 * the lookup, the edit dialog opened with that manager correctly preselected -- and the
 * table read "No manager chosen". The page binds the manager's name, and it bound
 * `al_tcmanageridname`, which is how FetchXML names a lookup label, not how the Web API
 * does. It was undefined on every row, so every mapping looked unmapped.
 *
 * The page exists to say who is told when a sign-off falls due, so reading it wrong is the
 * whole of what it is for.
 */

const contacts: ContactOption[] = [
  { id: 'bea63626-cbad-f111-aaac-000000000001', name: 'A Manager', email: 'a@example.invalid' },
  { id: 'bea63626-cbad-f111-aaac-000000000002', name: 'Another Manager', email: 'b@example.invalid' },
];

describe('managerNameFor', () => {
  it('resolves the name from the lookup id the Web API does return', () => {
    expect(
      managerNameFor(
        {
          al_advisermappingid: 'm1',
          _al_tcmanagerid_value: 'bea63626-cbad-f111-aaac-000000000001',
        },
        contacts,
      ),
    ).toBe('A Manager');
  });

  it('does not depend on al_tcmanageridname, which this app never receives', () => {
    const row = {
      al_advisermappingid: 'm1',
      _al_tcmanagerid_value: 'bea63626-cbad-f111-aaac-000000000002',
      al_tcmanageridname: undefined,
    };

    expect(managerNameFor(row, contacts)).toBe('Another Manager');
    expect(managerNameFor(row, contacts)).not.toBeNull();
  });

  it('matches the id whatever case it comes back in', () => {
    expect(
      managerNameFor(
        {
          al_advisermappingid: 'm1',
          _al_tcmanagerid_value: 'BEA63626-CBAD-F111-AAAC-000000000001',
        },
        contacts,
      ),
    ).toBe('A Manager');
  });

  it('prefers a label the read did supply', () => {
    expect(
      managerNameFor(
        {
          al_advisermappingid: 'm1',
          _al_tcmanagerid_value: 'bea63626-cbad-f111-aaac-000000000001',
          '_al_tcmanagerid_value@OData.Community.Display.V1.FormattedValue': 'Name From Dataverse',
        },
        contacts,
      ),
    ).toBe('Name From Dataverse');
  });

  it('is null when no manager is mapped, so the page can say so', () => {
    expect(managerNameFor({ al_advisermappingid: 'm1' }, contacts)).toBeNull();
    expect(managerNameFor({ al_advisermappingid: 'm1', _al_tcmanagerid_value: '' }, contacts)).toBeNull();
  });

  it('is null when the manager is not among the contacts offered', () => {
    // A contact without a work email is not offered as a manager, so it is not in the list.
    // Reporting no name is honest; inventing one would not be.
    expect(
      managerNameFor(
        { al_advisermappingid: 'm1', _al_tcmanagerid_value: 'bea63626-cbad-f111-aaac-999999999999' },
        contacts,
      ),
    ).toBeNull();
  });
});

describe('toMappingRows', () => {
  it('keeps the id and email, and fills the manager name', () => {
    expect(
      toMappingRows(
        [
          {
            al_advisermappingid: 'm1',
            al_adviseremail: '  adviser@example.invalid  ',
            _al_tcmanagerid_value: 'bea63626-cbad-f111-aaac-000000000001',
          },
          { al_advisermappingid: 'm2', al_adviseremail: 'other@example.invalid' },
        ],
        contacts,
      ),
    ).toEqual([
      {
        id: 'm1',
        adviserEmail: 'adviser@example.invalid',
        managerId: 'bea63626-cbad-f111-aaac-000000000001',
        managerName: 'A Manager',
      },
      { id: 'm2', adviserEmail: 'other@example.invalid', managerId: null, managerName: null },
    ]);
  });
});
