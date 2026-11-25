import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Link, NavLink, useLocation, useNavigate } from 'react-router-dom';
import { NAV_GROUPS } from '../../app/navigation';
import type { NavGroup } from '../../app/navigation';
import { readCollapsedGroups, writeCollapsedGroups } from '../../app/navigationState';
import { useCurrentUser } from '../../services/auth/useCurrentUser';
import { usePermissions } from '../../app/permissions/permissionContext';
import { useMediaQuery } from '../../hooks/useMediaQuery';
import { Icon as SharedIcon } from '../icons/Icon';
import './AppShell.css';
import { useProductName } from '../../app/product/useProductName';
import { initials, roleLabel } from './shellLabels';
import { useCurrentBreadcrumbTail } from './breadcrumbTail';
import { useOpenCaseCount } from '../../features/cases/useOpenCaseCount';
// The portal header's logo, as markup so the player's content policy cannot block it.
import logo from '../../assets/ascot-lloyd-logo.svg?raw';

// The printed page's copy of the logo (owner, 2026-11-23): ids dropped so the page never holds two of each.
const printLogo = logo.replace(/\s(id|data-name)="[^"]*"/g, '');

function isItemActive(pathname: string, to: string): boolean {
  return to === '/' ? pathname === '/' : pathname === to || pathname.startsWith(`${to}/`);
}

function groupContainsRoute(group: NavGroup, pathname: string): boolean {
  return group.items.some((item) => isItemActive(pathname, item.to));
}

// One outline icon per page, drawn as markup like the logo so the player's content policy cannot block it.
const NAV_ICONS: Record<string, string> = {
  '/': 'M4 4h6v6H4zM14 4h6v6h-6zM14 14h6v6h-6zM4 14h6v6H4z',
  '/cases': 'm3 7 2 2 4-4M3 17l2 2 4-4M13 6h8M13 12h8M13 18h8',
  '/imports': 'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12',
  '/reports': 'M3 3v18h18M8 17v-5M13 17V8M18 17v-8',
  '/exports': 'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3',
  '/admin/questions': 'M21 6H9M21 12h-8M21 18h-8M4 4v4a2 2 0 0 0 2 2h3M4 8v8a2 2 0 0 0 2 2h3',
  '/admin/advisers':
    'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M13 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75',
  '/admin/templates': 'M4 5h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2zM22 7l-10 7L2 7',
  '/admin/lists': 'M9 6h12M9 12h12M9 18h12M4 6h.01M4 12h.01M4 18h.01',
  '/admin/security': 'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10zM9 12l2 2 4-4',
  '/admin/people':
    'M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM15 10a3 3 0 1 1-6 0 3 3 0 0 1 6 0M7 21v-1a3 3 0 0 1 3-3h4a3 3 0 0 1 3 3v1',
};

function Icon({ d, className = 'shell__icon' }: { d: string | undefined; className?: string }) {
  return <SharedIcon d={d} className={className} />;
}

// A figure beside a menu item, such as the open cases beside the worklist.
type Badges = Record<string, number | null>;

function GroupLinks({ group, badges }: { group: NavGroup; badges: Badges }) {
  return (
    <ul className="shell__list">
      {group.items.map((item) => (
        <li key={item.to}>
          <NavLink to={item.to} end={item.to === '/'} className="shell__link">
            <Icon d={NAV_ICONS[item.to]} />
            <span>{item.label}</span>
            {badges[item.to] != null ? (
              <span className="shell__badge">
                {badges[item.to]}
                <span className="visually-hidden"> open</span>
              </span>
            ) : null}
          </NavLink>
        </li>
      ))}
    </ul>
  );
}

/** Where this page sits: its menu group, then the page itself, linked when you are below it. */
// Pages outside the menu, placed under the page they are opened from.
const SUB_PAGE_PARENTS: [prefix: string, parent: string][] = [
  ['/reviews/', '/cases'],
  ['/people/', '/cases'],
];

function Breadcrumb() {
  const { pathname } = useLocation();
  const tail = useCurrentBreadcrumbTail();
  const placed = SUB_PAGE_PARENTS.find(([prefix]) => pathname.startsWith(prefix))?.[1] ?? pathname;
  const group = NAV_GROUPS.find((g) => groupContainsRoute(g, placed));
  const item = group?.items.find((i) => isItemActive(placed, i.to));
  if (!group || !item) return <span />;
  const here = pathname === item.to;

  return (
    <nav className="shell__crumbs" aria-label="Breadcrumb">
      <ol>
        <li>{group.heading}</li>
        <li>
          {here ? (
            <span aria-current="page">{item.label}</span>
          ) : (
            <Link to={item.to}>{item.label}</Link>
          )}
        </li>
        {!here && tail ? (
          <li>
            <span aria-current="page">{tail}</span>
          </li>
        ) : null}
      </ol>
    </nav>
  );
}

/** Opens the worklist already searched; "/" reaches it from anywhere that is not a text field. */
function QuickFind() {
  const navigate = useNavigate();
  const input = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== '/' || event.ctrlKey || event.metaKey || event.altKey) return;
      const target = event.target as HTMLElement;
      if (target.isContentEditable || target.closest('input, textarea, select')) return;
      event.preventDefault();
      input.current?.focus();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);

  return (
    <form
      role="search"
      className="shell__search"
      onSubmit={(event) => {
        event.preventDefault();
        const q = input.current?.value.trim() ?? '';
        navigate(q ? `/cases?q=${encodeURIComponent(q)}` : '/cases');
      }}
    >
      <Icon className="shell__search-icon" d="M20 20l-4.3-4.3M18 11a7 7 0 1 1-14 0 7 7 0 0 1 14 0" />
      <input ref={input} type="search" aria-label="Find a case, client or adviser" placeholder="Find a case, client or adviser" />
      <kbd aria-hidden="true">/</kbd>
    </form>
  );
}

function CollapsibleGroup({
  group,
  badges,
  expanded,
  onToggle,
}: {
  group: NavGroup;
  badges: Badges;
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
          {group.heading}
          <span className="shell__chevron" aria-hidden="true" />
        </button>
      </h2>
      <div id={panelId} hidden={!expanded}>
        <GroupLinks group={group} badges={badges} />
      </div>
    </div>
  );
}

function SignedInUser() {
  const state = useCurrentUser();
  const { ready, roles } = usePermissions();

  if (state.status === 'loading') {
    return <span className="shell__user">Identifying user…</span>;
  }

  if (state.status === 'error') {
    return <span className="shell__user shell__user--error">{state.message}</span>;
  }

  const name = state.user.fullName ?? state.user.userPrincipalName ?? 'Unknown user';
  return (
    <span className="shell__user">
      <span className="shell__avatar" aria-hidden="true">{initials(name)}</span>
      <span className="shell__user-text">
        <span className="shell__user-name">{name}</span>
        {ready ? <span className="shell__user-role">{roleLabel(roles)}</span> : null}
      </span>
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
  const product = useProductName();
  const isCompact = useMediaQuery('(max-width: 60rem)');
  const openCases = useOpenCaseCount(can('page.cases'));
  const badges = useMemo<Badges>(() => ({ '/cases': openCases }), [openCases]);

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

      <aside className="shell__sidebar">
        <span className="shell__brand">
          <span
            className="shell__logo"
            role="img"
            // The product name is no longer printed under the logo (owner, 2026-11-24); it is still read out.
            aria-label={`Ascot Lloyd ${product}`}
            dangerouslySetInnerHTML={{ __html: logo }}
          />
        </span>

        <nav className="shell__rail" aria-label="Sections">
          {navGroups.map((group) =>
            isCompact ? (
              <div className="shell__group" key={group.heading}>
                <h2 className="visually-hidden">{group.heading}</h2>
                <GroupLinks group={group} badges={badges} />
              </div>
            ) : (
              <CollapsibleGroup
                key={group.heading}
                group={group}
                badges={badges}
                expanded={!collapsed.includes(group.heading)}
                onToggle={() => toggle(group.heading)}
              />
            ),
          )}
        </nav>

        <SignedInUser />
      </aside>

      <header className="shell__header">
        <Breadcrumb />
        {can('page.cases') ? <QuickFind /> : null}
      </header>

      <main className="shell__main" id="main" tabIndex={-1}>
        {/* Ascot Lloyd branding on a printed or saved page: the header is hidden in print. */}
        <span className="shell__print-logo" aria-hidden="true" dangerouslySetInnerHTML={{ __html: printLogo }} />
        <AccessNotice />
        {children}
      </main>
    </div>
  );
}
