import { describe, expect, it, vi } from 'vitest';

const templatesGetAll = vi.fn();
const contactsGetAll = vi.fn();

vi.mock('../../generated', () => ({
  Al_notificationsService: { getAll: vi.fn() },
  Al_notificationtemplatesService: { getAll: (o: unknown) => templatesGetAll(o) },
  ContactsService: { getAll: (o: unknown) => contactsGetAll(o) },
}));

const { loadNotificationTemplates } = await import('./useNotificationTemplates');

describe('loading the letters', () => {
  it('still shows the letters when the contacts read throws', async () => {
    templatesGetAll.mockResolvedValue({ success: true, data: [] });
    contactsGetAll.mockRejectedValue(new Error('network down'));
    vi.spyOn(console, 'error').mockImplementation(() => {});

    const state = await loadNotificationTemplates();

    expect(state.status).toBe('ready');
    if (state.status !== 'ready') return;
    expect(state.templates.length).toBeGreaterThan(0);
    expect(state.contacts).toEqual([]);
    expect(state.contactsKnown).toBe(false);
  });

  it('names a contact with no full name by the shared rule', async () => {
    templatesGetAll.mockResolvedValue({ success: true, data: [] });
    contactsGetAll.mockResolvedValue({
      success: true,
      data: [
        { contactid: 'C1', fullname: '', firstname: 'Bo', lastname: 'Lee', emailaddress1: 'bo@example.invalid', statecode: 0 },
      ],
    });

    const state = await loadNotificationTemplates();

    expect(state.status === 'ready' && state.contacts).toEqual([
      { id: 'c1', name: 'Bo Lee', email: 'bo@example.invalid' },
    ]);
  });
});
