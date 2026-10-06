import { describe, expect, it, vi } from 'vitest';
import type { DirectoryUser } from '../../hooks/useUserDirectory';

// UserPicker.tsx imports useUserDirectory, which imports the generated ContactsService,
// which imports the Power Apps data SDK - a package that cannot be resolved under vitest
// (see caseDetailReload.test.ts). Stubbed so the module can load at all; pickerOptionText
// and resolvePicker never call the hook themselves.
vi.mock('../../hooks/useUserDirectory', () => ({
  useUserDirectory: () => ({ status: 'loading' }),
}));

const { pickerOptionText, resolvePicker } = await import('../../components/form/UserPicker');

const adam1 = { id: 'c1', name: 'Adam Smith', email: 'adam.smith@example.com', active: true } as DirectoryUser;
const adam2 = { id: 'c2', name: 'Adam Smith', email: 'adam.smith2@example.com', active: true } as DirectoryUser;

describe('the case person picker', () => {
  it('shows a name with its email when the field writes an email too', () => {
    expect(pickerOptionText(adam2, 'name', true)).toBe('Adam Smith — adam.smith2@example.com');
    expect(pickerOptionText(adam2, 'name', false)).toBe('Adam Smith');
  });

  it('tells two people of one name apart by the email in the option', () => {
    expect(resolvePicker([adam1, adam2], 'name', true, 'adam smith — ADAM.SMITH2@example.com ')).toBe(adam2);
  });

  it('resolves nobody for text that matches no option', () => {
    expect(resolvePicker([adam1, adam2], 'name', true, 'Not Onboarded')).toBeNull();
  });
});

const { keepsTypedText } = await import('../../components/form/UserPicker');

describe('a person picker being typed into', () => {
  const users = [adam1, adam2];

  it('keeps half-typed text in an id field, which holds nothing for it', () => {
    // Deleting one character from a chosen "Adam Smith — adam.smith@example.com" left the
    // value '' and the re-sync drew '' over the whole box.
    expect(keepsTypedText(users, 'id', false, 'Adam Smith — adam.smith@example.co', '')).toBe(true);
  });

  it('keeps half-typed text in a name field, which holds it as typed', () => {
    expect(keepsTypedText(users, 'name', true, 'Ada', 'Ada')).toBe(true);
  });

  it('re-syncs when the value moved under the text, as after a save', () => {
    expect(keepsTypedText(users, 'id', false, 'Ada', 'c2')).toBe(false);
  });

  it('re-syncs a chosen option, so a name field shows the name it stores', () => {
    expect(keepsTypedText(users, 'name', true, 'Adam Smith — adam.smith2@example.com', 'Adam Smith')).toBe(false);
  });

  it('re-syncs an empty box', () => {
    expect(keepsTypedText(users, 'id', false, '', '')).toBe(false);
  });
});
