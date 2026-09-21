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

/**
 * Says so when the menu is a guess (AD-136), or when there is no menu because the app could
 * not work out who is asking (project owner, 2026-09-21).
 *
 * Either the rules or the caller's own roles could not be read, so what is on screen is a
 * stand-in and not this person's access. The pages offered may be more, fewer or simply
 * different from the ones they actually hold. Without this the app looked entirely normal and
 * the person had no way to know — the 2026-09-14 report took an investigation to explain
 * precisely because nothing on screen said anything was wrong.
 *
 * It covers BOTH reads as of 2026-09-21 (F44). It used to watch only the rules read, while
 * the permissive stand-in is triggered by the ROLES read, so the failure that actually causes
 * a guessed menu was the one this notice stayed silent for. An account with Outcome Testing
 * App User but no Basic User hit exactly that: al_pagepermission is granted by that role and
 * read fine, al_GetMyRoles faulted, and the app handed out every role in the product without
 * a word.
 *
 * Deliberately NOT shown for an unconfigured environment with no rules stored: that is a real
 * answer, and warning on it would cry wolf on every fresh environment.
 */
function AccessNotice() {
  const { rulesUnavailable, accessUnknown } = usePermissions();

  if (accessUnknown) {
    return (
      <div className="shell__notice shell__notice--blocking" role="alert">
        <strong>No access — we could not confirm who you are.</strong> This app could not
        establish which application roles you hold, so it is showing you nothing rather than
        guessing. This is usually a missing security role on your account, not a problem with
        your work. Ask an administrator to check it, then sign in again.
      </div>
    );
  }

  if (!rulesUnavailable) return null;

  return (
    <div className="shell__notice" role="status">
      <strong>Your access could not be confirmed.</strong> Your roles are known, but this app
      could not read the rules that say what each role may open, so the menu is showing the
      built-in defaults instead. Pages may be missing, and pages may be listed that this
      environment does not actually grant you — treat what you see as unconfirmed. Nothing you
      DO is unsafe: every action is checked again on the server. Ask an administrator to check
      the permission configuration.
    </div>
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
        <AccessNotice />
        {children}
      </main>
    </div>
  );
}
