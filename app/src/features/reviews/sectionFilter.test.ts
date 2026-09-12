import { describe, expect, it } from 'vitest';
import { buildSectionFilter, OWNER_ROLE_BOTH } from './sectionFilter';

/**
 * Mirrors the section half of the portal's review FetchXML and SectionRules in
 * plugins/OutcomeTesting.Plugins (AD-123). A section is rendered when it is owned by this
 * discipline or by Both, belongs to the review's checklist version, and is in force on the
 * reference day.
 */
describe('buildSectionFilter', () => {
  it('accepts the discipline’s own role or Both', () => {
    const filter = buildSectionFilter(120910100, null, '2026-09-12');

    expect(filter).toContain('al_ownerrole eq 120910100');
    expect(filter).toContain(`al_ownerrole eq ${OWNER_ROLE_BOTH}`);
  });

  it('excludes a section dated out on or before the reference day', () => {
    expect(buildSectionFilter(120910101, null, '2026-09-12')).toContain(
      'al_effectiveto eq null or al_effectiveto gt 2026-09-12',
    );
  });

  it('excludes a section not yet in force', () => {
    expect(buildSectionFilter(120910101, null, '2026-09-12')).toContain(
      'al_effectivefrom eq null or al_effectivefrom le 2026-09-12',
    );
  });

  it('scopes to the checklist version when the review carries one', () => {
    expect(buildSectionFilter(120910101, 'abc-123', '2026-09-12')).toContain(
      '_al_checklistversionid_value eq abc-123',
    );
  });

  it('omits the version clause when the review carries none', () => {
    expect(buildSectionFilter(120910101, null, '2026-09-12')).not.toContain('checklistversionid');
  });

  it('keeps the owner-role alternation grouped, so it cannot swallow the other clauses', () => {
    // Without the parentheses this reads as
    //   (own role) or (Both and version and dates)
    // which returns every section this discipline owns, retired ones included.
    const filter = buildSectionFilter(120910101, 'abc-123', '2026-09-12');

    expect(filter).toContain('(al_ownerrole eq 120910101 or al_ownerrole eq 120910105)');
  });
});
