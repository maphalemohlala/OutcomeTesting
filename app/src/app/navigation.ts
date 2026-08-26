/** Navigation is grouped by operational task, not by table (09-Application-Screens). */
export interface NavItem {
  to: string;
  label: string;
}

export interface NavGroup {
  heading: string;
  items: NavItem[];
}

export const NAV_GROUPS: NavGroup[] = [
  {
    heading: 'My work',
    items: [
      { to: '/', label: 'Dashboard' },
      { to: '/cases', label: 'Case worklist' },
    ],
  },
  {
    heading: 'Intake',
    items: [{ to: '/imports', label: 'Case intake' }],
  },
  {
    heading: 'Reporting',
    items: [
      { to: '/reports', label: 'Management reporting' },
      { to: '/exports', label: 'Exports' },
    ],
  },
  {
    heading: 'Administration',
    items: [
      { to: '/admin/questions', label: 'Question library' },
      { to: '/admin/security', label: 'Security configuration' },
    ],
  },
];
