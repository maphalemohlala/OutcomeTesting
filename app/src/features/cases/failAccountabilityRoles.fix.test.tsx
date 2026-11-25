import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { accountableEmails, namedPersonNote } from './failAccountability';

/**
 * Only someone holding the Adviser or Planner role can be named as carrying a fail (project
 * owner, 2026-11-17). al_SetFailAccountability refuses anyone else; the picker offers only
 * them, and says so plainly when nobody holds either role.
 */

type Security =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; mappings: { id: string; email: string; role: string; active: boolean }[]; permissions: [] };

let security: Security = { status: 'loading' };
const securityCalls: number[] = [];
const allPickers: { onlyEmails?: ReadonlySet<string>; includeInactive?: boolean }[] = [];
// The role-limited named-person pickers. The File Quality administrator picker (requirements
// log row 10) is open to any contact, so it is counted apart.
const pickers = {
  get length() {
    return named().length;
  },
  [Symbol.iterator]() {
    return named()[Symbol.iterator]();
  },
};
const named = () => allPickers.filter((p) => p.onlyEmails !== undefined);
const administratorPickers = () => allPickers.filter((p) => p.onlyEmails === undefined);

vi.mock('../../app/permissions/PermissionGate', () => ({
  PermissionGate: ({ children }: { children: unknown }) => children,
}));
vi.mock('../admin/useSecurityConfig', () => ({
  useSecurityConfig: (key: number) => {
    securityCalls.push(key);
    return security;
  },
}));
vi.mock('../../components/form/UserPicker', () => ({
  UserPicker: (props: { onlyEmails?: ReadonlySet<string>; includeInactive?: boolean }) => {
    allPickers.push(props);
    return <input data-picker="yes" />;
  },
}));
vi.mock('../../hooks/useIntentKey', () => ({
  useIntentKeys: () => ({ keyFor: () => 'k', release: () => {} }),
}));
vi.mock('../../services/commands/setFailAccountability', () => ({ setFailAccountability: vi.fn() }));

const { FailAccountabilityPanel } = await import('./FailAccountabilityPanel');

function outcome(id: string) {
  return {
    id,
    reference: 'OUT-' + id,
    reviewInstance: 'Check ' + id,
    initialOutcome: 'Insufficient evidence',
    finalOutcome: null,
    accountability: { fqAdviser: false, fqParaplanner: false, aqAdviser: false, aqParaplanner: false },
    fqAccountable: null,
    aqAccountable: null,
    rowVersion: '1',
  };
}

function render(outcomes = [outcome('a'), outcome('b')]) {
  securityCalls.length = 0;
  allPickers.length = 0;
  return renderToStaticMarkup(
    <FailAccountabilityPanel
      state={{ status: 'ready', outcomes } as never}
      people={{ adviser: 'Ann Adviser', paraplanner: 'Pat Planner' }}
      onSaved={() => {}}
    />,
  );
}

const mapping = (email: string, role: string, active = true) => ({ id: email + role, email, role, active });

describe('who can be named as carrying a fail', () => {
  it('is everyone actively holding the Adviser or Planner role, and nobody else', () => {
    const emails = accountableEmails([
      mapping('Adam@Example.com ', 'AL Portal - Adviser Remediation'),
      mapping('pia@example.com', ' al portal - planner '),
      mapping('gone@example.com', 'AL Portal - Planner', false),
      mapping('tax@example.com', 'AL Portal - Tax Reviewer'),
      mapping('', 'AL Portal - Planner'),
    ]);
    expect([...emails].sort()).toEqual(['adam@example.com', 'pia@example.com']);
  });

  it('offers only the holders in both named-person pickers, from one read for the whole case', () => {
    security = {
      status: 'ready',
      mappings: [mapping('adam@example.com', 'AL Portal - Adviser Remediation'), mapping('t@example.com', 'AL Portal - Tax Reviewer')],
      permissions: [],
    };
    const html = render();

    expect(securityCalls).toHaveLength(1);
    expect(pickers).toHaveLength(4);
    for (const picker of pickers) expect([...(picker.onlyEmails ?? [])]).toEqual(['adam@example.com']);
    // Someone who has left still carries a past fail, so the server accepts them and so must the picker.
    for (const picker of pickers) expect(picker.includeInactive).toBe(true);
    expect(html).not.toContain('Nobody is assigned');
    // The administrator is a tick, not a picker (2026-11-25).
    expect(administratorPickers()).toHaveLength(0);
  });

  it('offers the administrator as a tick, with no names, even when nobody holds either role', () => {
    security = { status: 'ready', mappings: [], permissions: [] };
    const html = render([outcome('a')]);

    expect(administratorPickers()).toHaveLength(0);
    expect(html).toContain('File Quality: an administrator');
    expect(html).toMatch(/<input type="checkbox"[^>]*\/?><span>File Quality: an administrator<\/span>/);
    expect(html).toContain('not written to the export');
  });

  it('says plainly that nobody can be named when nobody holds either role', () => {
    security = { status: 'ready', mappings: [mapping('t@example.com', 'AL Portal - Tax Reviewer')], permissions: [] };
    const html = render([outcome('a')]);

    expect(pickers).toHaveLength(0);
    expect(html).toContain(
      'Nobody is assigned the Adviser or Planner role on the People page, so nobody can be named. An administrator can assign these roles there.',
    );
    // The flags can still be recorded: the case's own adviser or paraplanner carries them.
    expect(html).toContain('Record accountability');
  });

  it('waits, and does not claim nobody holds the role, while the holders are loading', () => {
    security = { status: 'loading' };
    const html = render([outcome('a')]);

    expect(pickers).toHaveLength(0);
    expect(html).toContain('Loading who holds the Adviser or Planner role…');
    expect(html).not.toContain('Nobody is assigned');
  });

  it('says the holders could not be read when that read fails', () => {
    security = { status: 'unavailable', reason: 'x' };
    const html = render([outcome('a')]);

    expect(pickers).toHaveLength(0);
    expect(html).toContain('could not be read, so nobody can be named right now');
    expect(html).not.toContain('Nobody is assigned');
  });

  it('has no note once someone holds a role', () => {
    expect(namedPersonNote('ready', new Set(['a@example.com']))).toBeNull();
  });
});
