import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';

let directory: { status: string; reason?: string } = { status: 'loading' };
vi.mock('../../hooks/useUserDirectory', () => ({ useUserDirectory: () => directory }));

const { UserPicker } = await import('./UserPicker');
const GUID = '6f1c2a0e-0000-4000-8000-000000000001';

describe('the person picker before the directory is ready', () => {
  it('does not offer a raw id box for an id field while loading', () => {
    directory = { status: 'loading' };
    const html = renderToStaticMarkup(<UserPicker field="id" value={GUID} onChange={() => {}} />);
    expect(html).toContain('disabled');
    expect(html).not.toContain(GUID);
    expect(html).toContain('Loading people…');
  });

  it('says the people could not be loaded for an id field when the directory is unavailable', () => {
    directory = { status: 'unavailable', reason: 'x' };
    const html = renderToStaticMarkup(<UserPicker field="id" value="" onChange={() => {}} />);
    expect(html).toContain('disabled');
    expect(html).toContain('could not be loaded');
  });

  it('is disabled too where only certain people may be chosen', () => {
    directory = { status: 'loading' };
    const html = renderToStaticMarkup(
      <UserPicker field="email" value="" onChange={() => {}} onlyEmails={new Set(['a@example.invalid'])} />,
    );
    expect(html).toContain('disabled');
  });

  it('keeps a free-text name box, where an unregistered name must survive', () => {
    directory = { status: 'loading' };
    const html = renderToStaticMarkup(<UserPicker value="Ann Adviser" onChange={() => {}} />);
    expect(html).not.toContain('disabled');
    expect(html).toContain('Ann Adviser');
  });
});

describe('the person picker once the directory is ready', () => {
  const person = (id: string, name: string, active: boolean) => ({
    id, name, email: `${id}@example.invalid`, active, createdOn: null, rowVersion: null, staffCode: null,
  });
  const ready = () => {
    directory = { status: 'ready', users: [person('stays', 'Sam Stays', true), person('left', 'Lee Left', false)] } as never;
  };

  it('leaves out someone who has left by default', () => {
    ready();
    const html = renderToStaticMarkup(<UserPicker field="id" value="" onChange={() => {}} />);
    expect(html).toContain('Sam Stays');
    expect(html).not.toContain('Lee Left');
  });

  it('offers and names someone who has left when asked to, still limited to the emails given', () => {
    ready();
    const html = renderToStaticMarkup(
      <UserPicker
        field="id"
        value="left"
        onChange={() => {}}
        includeInactive
        onlyEmails={new Set(['left@example.invalid'])}
      />,
    );
    expect(html).toContain('<option value="Lee Left — left@example.invalid"');
    expect(html).toContain('value="Lee Left — left@example.invalid"');
    expect(html).not.toContain('Sam Stays');
  });
});
