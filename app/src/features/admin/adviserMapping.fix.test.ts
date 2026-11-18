import { describe, expect, it } from 'vitest';
import { SELF_MAPPING, selfMappingRefusal } from './adviserMappingFailure';
import { managerChoices, offeredManagers, toKnownContacts } from './adviserMappingRows';

const raw = [
  { contactid: 'c1', fullname: 'Ann Manager', emailaddress1: 'ann@example.invalid', statecode: 0 },
  // An email but no full name: listed under the shared rule, as the directory lists them.
  { contactid: 'c2', fullname: '', firstname: 'Bo', lastname: 'Lee', emailaddress1: 'bo@example.invalid', statecode: 0 },
  { contactid: 'c3', fullname: null, emailaddress1: 'nameless@example.invalid', statecode: 0 },
  { contactid: 'c4', fullname: 'No Email', emailaddress1: '', statecode: 0 },
  { contactid: 'c5', fullname: 'Gone', emailaddress1: 'gone@example.invalid', statecode: 1 },
];

describe('the T&C Manager choices', () => {
  it('offer every active contact with an email, named by the shared rule', () => {
    const offered = offeredManagers(toKnownContacts(raw));
    expect(offered.map((c) => [c.id, c.name])).toEqual([
      ['c1', 'Ann Manager'],
      ['c2', 'Bo Lee'],
      ['c3', 'nameless@example.invalid'],
    ]);
    expect(managerChoices(offered, toKnownContacts(raw), null).map((c) => c.id)).toContain('c3');
  });
});

describe('mapping an adviser to themselves', () => {
  const known = toKnownContacts(raw);

  it('is refused when the manager has the adviser’s email, ignoring case and spaces', () => {
    expect(selfMappingRefusal(' ANN@example.invalid ', 'c1', known)).toBe(SELF_MAPPING);
    expect(selfMappingRefusal('ann@example.invalid', 'C1', known)).toBe(SELF_MAPPING);
  });

  it('is allowed for anyone else', () => {
    expect(selfMappingRefusal('ann@example.invalid', 'c2', known)).toBeNull();
    expect(selfMappingRefusal('ann@example.invalid', '', known)).toBeNull();
    expect(selfMappingRefusal('ann@example.invalid', 'unknown', known)).toBeNull();
  });
});
