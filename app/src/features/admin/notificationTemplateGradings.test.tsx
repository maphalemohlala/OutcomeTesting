import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';

const recipients = { kinds: [120910810], contactIds: [] };

vi.mock('../../generated', () => ({}));
vi.mock('../../app/permissions/permissionContext', () => ({
  usePermissions: () => ({ can: () => true, ready: true }),
}));
vi.mock('./useNotificationTemplates', () => ({
  FAILED_LETTER_DAYS: 30,
  useFailedLetters: () => ({ status: 'ready', letters: [] }),
  saveTemplate: vi.fn(),
  createTemplate: vi.fn(),
  removeTemplate: vi.fn(),
  useNotificationTemplates: () => ({
    status: 'ready',
    contacts: [],
    contactsKnown: true,
    templates: [
      {
        id: null, code: 'REMEDIATION-INSUFFICIENT', name: 'Remedial needed - insufficient evidence',
        subject: '', body: '', tokens: [], isHtml: true, stored: false, custom: false,
        eventValue: null, recipients: { kinds: [], contactIds: [] }, gradings: [],
      },
      {
        id: null, code: 'REMEDIATION-HARM', name: 'Remedial needed - potential harm',
        subject: '', body: '', tokens: [], isHtml: true, stored: false, custom: false,
        eventValue: null, recipients: { kinds: [], contactIds: [] }, gradings: [],
      },
      {
        id: 'a1', code: 'TELL-THE-MANAGER', name: 'Tell the manager',
        subject: 'Heads up', body: '<p>x</p>', tokens: [], isHtml: true, stored: true, custom: true,
        eventValue: 120910802, recipients, gradings: [120910701, 120910702],
      },
      {
        id: 'a2', code: 'TELL-EVERYONE', name: 'Tell everyone',
        subject: 'Heads up', body: '<p>x</p>', tokens: [], isHtml: true, stored: true, custom: true,
        eventValue: 120910802, recipients, gradings: [],
      },
    ],
  }),
}));

const { NotificationTemplatePage, GradingFields } = await import('./NotificationTemplatePage');

describe('letters limited to gradings, as the email templates page shows them', () => {
  const html = renderToStaticMarkup(<NotificationTemplatePage />);

  it('lists the insufficient evidence and potential harm letters separately', () => {
    expect(html).toContain('Remedial needed - insufficient evidence');
    expect(html).toContain('Remedial needed - potential harm');
  });

  it('says which gradings a limited letter goes out for, and nothing for an unlimited one', () => {
    expect(html).toContain('Only for: Pass with issues, Insufficient evidence');
    expect(html.match(/Only for:/g)).toHaveLength(1);
  });

  it('offers the four gradings as tick boxes, with the chosen ones ticked', () => {
    const form = renderToStaticMarkup(
      <GradingFields gradings={[120910703]} setGradings={() => {}} disabled={false} />,
    );
    for (const label of ['Pass', 'Pass with issues', 'Insufficient evidence', 'Potential harm']) {
      expect(form).toContain(`/>${label}</label>`);
    }
    expect(form.match(/checked=""/g)).toHaveLength(1);
    expect(form).toMatch(/checked=""\/>Potential harm/);
    expect(form).toContain('Leave all unticked to send the letter whatever the grading.');
  });
});
