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
      { to: '/admin/users', label: 'Users', resource: 'page.admin.users' },
      { to: '/admin/security', label: 'Security configuration', resource: 'page.admin.security' },
    ],
  },
];
