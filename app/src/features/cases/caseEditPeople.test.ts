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
