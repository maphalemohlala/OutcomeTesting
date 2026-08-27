import { useEffect, useId, useMemo, useState } from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import { NAV_GROUPS } from '../../app/navigation';
import type { NavGroup } from '../../app/navigation';
import { readCollapsedGroups, writeCollapsedGroups } from '../../app/navigationState';
import { useCurrentUser } from '../../services/auth/useCurrentUser';
import { usePermissions } from '../../app/permissions/permissionContext';
import { useMediaQuery } from '../../hooks/useMediaQuery';
import './AppShell.css';

function isItemActive(pathname: string, to: string): boolean {
  return to === '/' ? pathname === '/' : pathname === to || pathname.startsWith(`${to}/`);
}

function groupContainsRoute(group: NavGroup, pathname: string): boolean {
  return group.items.some((item) => isItemActive(pathname, item.to));
}

function GroupLinks({ group }: { group: NavGroup }) {
  return (
    <ul className="shell__list">
      {group.items.map((item) => (
        <li key={item.to}>
          <NavLink to={item.to} end={item.to === '/'} className="shell__link">
            {item.label}
          </NavLink>
        </li>
      ))}
    </ul>
  );
}

function CollapsibleGroup({
  group,
  expanded,
  onToggle,
}: {
  group: NavGroup;
  expanded: boolean;
  onToggle: () => void;
}) {
  const panelId = useId();

  return (
    <div className="shell__group">
      <h2 className="shell__group-heading">
        <button
          type="button"
          className="shell__toggle"
          aria-expanded={expanded}
          aria-controls={panelId}
          onClick={onToggle}
        >
          <span className="shell__chevron" aria-hidden="true" />
          {group.heading}
        </button>
      </h2>
      <div id={panelId} hidden={!expanded}>
        <GroupLinks group={group} />
      </div>
    </div>
  );
}

function SignedInUser() {
  const state = useCurrentUser();

  if (state.status === 'loading') {
    return <span className="shell__user">Identifying user…</span>;
  }

  if (state.status === 'error') {
    return <span className="shell__user shell__user--error">{state.message}</span>;
  }

  return (
    <span className="shell__user">
      {state.user.fullName ?? state.user.userPrincipalName ?? 'Unknown user'}
    </span>
  );
}

export function AppShell({ children }: { children: React.ReactNode }) {
  const { pathname } = useLocation();
  const { can } = usePermissions();
  const isCompact = useMediaQuery('(max-width: 60rem)');

  // Only show pages the current role may view (AD-041). Empty groups drop out.
  const navGroups = useMemo<NavGroup[]>(
    () =>
      NAV_GROUPS.map((group) => ({
        ...group,
        items: group.items.filter((item) => can(item.resource)),
      })).filter((group) => group.items.length > 0),
    [can],
  );

  const [collapsed, setCollapsed] = useState<string[]>(() => {
    const stored = readCollapsedGroups();
    const active = NAV_GROUPS.find((group) => groupContainsRoute(group, pathname));
    return active ? stored.filter((heading) => heading !== active.heading) : stored;
  });
  const [lastPath, setLastPath] = useState(pathname);

  // A user should never have to open a group to see where they already are.
  if (lastPath !== pathname) {
    setLastPath(pathname);
    const active = NAV_GROUPS.find((group) => groupContainsRoute(group, pathname));
    if (active && collapsed.includes(active.heading)) {
      setCollapsed(collapsed.filter((heading) => heading !== active.heading));
    }
  }

  useEffect(() => {
    writeCollapsedGroups(collapsed);
  }, [collapsed]);

  const toggle = (heading: string) =>
    setCollapsed((current) =>
      current.includes(heading)
        ? current.filter((item) => item !== heading)
        : [...current, heading],
    );

  return (
    <div className="shell">
      <a className="skip-link" href="#main">
        Skip to main content
      </a>

      <header className="shell__header">
        <span className="shell__product">Outcome Testing</span>
        <SignedInUser />
      </header>

      <nav className="shell__rail" aria-label="Sections">
        {navGroups.map((group) =>
          isCompact ? (
            <div className="shell__group" key={group.heading}>
              <h2 className="visually-hidden">{group.heading}</h2>
              <GroupLinks group={group} />
            </div>
          ) : (
            <CollapsibleGroup
              key={group.heading}
              group={group}
              expanded={!collapsed.includes(group.heading)}
              onToggle={() => toggle(group.heading)}
            />
          ),
        )}
      </nav>

      <main className="shell__main" id="main" tabIndex={-1}>
        {children}
      </main>
    </div>
  );
}
