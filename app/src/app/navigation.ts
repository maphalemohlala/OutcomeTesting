import type { ResourceKey } from '../types/permissions';

/** Navigation is grouped by operational task, not by table (09-Application-Screens). */
export interface NavItem {
  to: string;
  label: string;
  /** Page resource that gates visibility (AD-041). */
  resource: ResourceKey;
}

export interface NavGroup {
  heading: string;
  items: NavItem[];
}

export const NAV_GROUPS: NavGroup[] = [
  {
    heading: 'My work',
    items: [
      { to: '/', label: 'Dashboard', resource: 'page.dashboard' },
      { to: '/cases', label: 'Case worklist', resource: 'page.cases' },
      { to: '/workload', label: 'Team workload', resource: 'page.workload' },
    ],
  },
  {
    heading: 'Intake',
    items: [{ to: '/imports', label: 'Case intake', resource: 'page.imports' }],
  },
  {
    heading: 'Reporting',
    items: [
      { to: '/reports', label: 'Management reporting', resource: 'page.reports' },
      { to: '/exports', label: 'Exports', resource: 'page.exports' },
    ],
  },
  {
    heading: 'Administration',
    items: [
      { to: '/admin/questions', label: 'Question library', resource: 'page.admin.questions' },
      { to: '/admin/advisers', label: 'Adviser mapping', resource: 'page.admin.advisers' },
      { to: '/admin/templates', label: 'Notification wording', resource: 'page.admin.templates' },
      { to: '/admin/lists', label: 'Dropdown options', resource: 'page.admin.lists' },
      { to: '/admin/security', label: 'Security configuration', resource: 'page.admin.security' },
      // Moved from "My work" (Task 9, 2026-09-22): the Role column writes
      // al_userrolemapping, an authorisation source, so the directory is gated as
      // administration rather than as a case view.
      { to: '/admin/people', label: 'People', resource: 'page.admin.users' },
    ],
  },
];
