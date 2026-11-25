import { Suspense, lazy } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { AppShell } from '../components/layout/AppShell';
import { ErrorBoundary } from '../components/feedback/ErrorBoundary';
import { PageUnavailable } from '../components/feedback/PageUnavailable';
import { RequirePermission } from './permissions/PermissionGate';
import { CaseWorklistPage } from '../features/cases/CaseWorklistPage';
import { CaseDetailPage } from '../features/cases/CaseDetailPage';
import { RecheckPage } from '../features/cases/RecheckPage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { PersonCasesPage } from '../features/people/PersonCasesPage';
import { ReviewDetailPage } from '../features/reviews/ReviewDetailPage';
import { RemediationPage } from '../features/remediation/RemediationPage';
import { Skeleton } from '../components/feedback/Skeleton';

/*
 * Administration, reporting and intake are loaded when first opened, not with the app: most
 * people never open them, and they were nearly half of what every sign-in downloaded. The
 * permission gate sits outside each, so a screen somebody may not open is never fetched.
 */
const CaseIntakePage = lazy(() => import('../features/imports/CaseIntakePage').then((m) => ({ default: m.CaseIntakePage })));
const PeoplePage = lazy(() => import('../features/people/PeoplePage').then((m) => ({ default: m.PeoplePage })));
const AdviserMappingPage = lazy(() => import('../features/admin/AdviserMappingPage').then((m) => ({ default: m.AdviserMappingPage })));
const NotificationTemplatePage = lazy(() => import('../features/admin/NotificationTemplatePage').then((m) => ({ default: m.NotificationTemplatePage })));
const QuestionLibraryPage = lazy(() => import('../features/admin/QuestionLibraryPage').then((m) => ({ default: m.QuestionLibraryPage })));
const ListOptionsPage = lazy(() => import('../features/admin/ListOptionsPage').then((m) => ({ default: m.ListOptionsPage })));
const SecurityConfigPage = lazy(() => import('../features/admin/SecurityConfigPage').then((m) => ({ default: m.SecurityConfigPage })));
const RoleDetailPage = lazy(() => import('../features/admin/RoleDetailPage').then((m) => ({ default: m.RoleDetailPage })));
const ReportsPage = lazy(() => import('../features/reports/ReportsPage').then((m) => ({ default: m.ReportsPage })));
const ExportsPage = lazy(() => import('../features/reports/ExportsPage').then((m) => ({ default: m.ExportsPage })));

/** Routes follow 09-Application-Screens. Screens without a data source say so explicitly. */
export function AppRoutes() {
  const { pathname } = useLocation();

  return (
    <AppShell>
      {/* Inside the shell, so a screen that fails - or one whose code could not be fetched
          after a new release - leaves the menu standing. Keyed on the path so choosing
          another screen clears the failure rather than repeating it. */}
      <ErrorBoundary key={pathname}>
      <Suspense fallback={<Skeleton page label="Loading page…" />}>
      <Routes>
        {/* Gated like every other screen. It was not until 2026-09-21 (F43), and the
            dashboard is the one screen where that mattered most: it is what a sign-in lands
            on, and it carries open case counts, the outcome distribution, the remediation
            backlog and case ageing. A user with no application role - never granted one, or
            a leaver deactivated through OD-010's sanctioned route - saw all of it.

            Everything around the gate was already in place, which is why this survived. The
            menu item is filtered on page.dashboard, al_pagepermission grants it to eight
            roles, permissions.ts maps '/' to page.dashboard, and a test asserts that map.
            The only thing missing was the route consulting any of it. */}
        <Route
          path="/"
          element={
            <RequirePermission resource="page.dashboard">
              <DashboardPage />
            </RequirePermission>
          }
        />
        <Route path="/cases" element={<RequirePermission resource="page.cases"><CaseWorklistPage /></RequirePermission>} />
        <Route path="/cases/:caseId" element={<RequirePermission resource="page.cases"><CaseDetailPage /></RequirePermission>} />
        {/* Allocation moved into the case edit modal on 2026-09-10 (project owner
            direction): one place to change who holds a case, rather than a screen for the
            allocation and a dialog for everything else. Kept as a redirect so bookmarks
            and any link still in the wild land on the case instead of the not-found page,
            as /audit already does. */}
        <Route
          path="/cases/:caseId/allocation"
          element={<Navigate to=".." relative="path" replace />}
        />
        <Route
          path="/cases/:caseId/remediation"
          element={
            <RequirePermission resource="page.remediation">
              <RemediationPage />
            </RequirePermission>
          }
        />
        {/* Gated by page.cases like the other case sub-screens; the regrade action itself
            is gated separately on command.regrade inside the page (AD-041). */}
        <Route
          path="/cases/:caseId/recheck"
          element={
            <RequirePermission resource="page.cases">
              <RecheckPage />
            </RequirePermission>
          }
        />
        <Route
          path="/cases/:caseId/audit"
          element={<Navigate to=".." relative="path" replace />}
        />
        {/*
          The registry: who the application knows, what they hold and their employee code
          (project owner, 2026-09-22). Under administration because the Role column writes
          al_userrolemapping, which PermissionHelpers reads as an authorisation source - on
          page.cases, where this page used to live, every case-worker could grant roles.
        */}
        <Route
          path="/admin/people"
          element={
            <RequirePermission resource="page.admin.users">
              <PeoplePage />
            </RequirePermission>
          }
        />
        {/* The caseload drill-down stays a case view: it shows work, not access. */}
        <Route
          path="/people/:role/:key"
          element={
            <RequirePermission resource="page.cases">
              <PersonCasesPage />
            </RequirePermission>
          }
        />
        <Route path="/people" element={<Navigate to="/admin/people" replace />} />
        <Route path="/reviews/:reviewId/tax" element={<RequirePermission resource="page.reviews"><ReviewDetailPage reviewType="Tax" /></RequirePermission>} />
        <Route path="/reviews/:reviewId/aqs" element={<RequirePermission resource="page.reviews"><ReviewDetailPage reviewType="AQS" /></RequirePermission>} />
        <Route
          path="/imports"
          element={<RequirePermission resource="page.imports"><CaseIntakePage /></RequirePermission>}
        />
        <Route
          path="/reports"
          element={<RequirePermission resource="page.reports"><ReportsPage /></RequirePermission>}
        />
        <Route
          path="/exports"
          element={
            <RequirePermission resource="page.exports">
              <ExportsPage />
            </RequirePermission>
          }
        />
        <Route
          path="/admin/questions"
          element={<RequirePermission resource="page.admin.questions"><QuestionLibraryPage /></RequirePermission>}
        />
        {/* Adviser -> T&C Manager mapping (Fixes 5, AD-162): who is told a sign-off is waiting,
            and who alone may perform it. */}
        <Route
          path="/admin/advisers"
          element={<RequirePermission resource="page.admin.advisers"><AdviserMappingPage /></RequirePermission>}
        />
        {/*
          The options behind the case header's dropdowns (project owner, 2026-09-21). Rows
          rather than choice metadata, so adding one needs no privilege, no deployment and
          no rebuild - see features/admin/listOptions.ts for why a page over the choice
          columns themselves could not have worked.
        */}
        <Route
          path="/admin/lists"
          element={<RequirePermission resource="page.admin.lists"><ListOptionsPage /></RequirePermission>}
        />
        {/* The wording of every letter the solution sends (Change 1, AD-163). */}
        <Route
          path="/admin/templates"
          element={<RequirePermission resource="page.admin.templates"><NotificationTemplatePage /></RequirePermission>}
        />
        {/*
          The Users admin page merged into People: one directory, sourced from Contacts,
          carrying both the caseload view and the registry admin actions. The route is kept
          as a redirect so bookmarks and any link still in the wild keep working.
        */}
        <Route path="/admin/users" element={<Navigate to="/admin/people" replace />} />
        <Route
          path="/admin/security"
          element={
            <RequirePermission resource="page.admin.security">
              <SecurityConfigPage />
            </RequirePermission>
          }
        />
        {/* One role's grants and holders. Gated by the same resource as the screen it
            drills down from; the manage actions on it are gated on permission.manage
            inside the page, as they are on the parent (AD-041). */}
        <Route
          path="/admin/security/roles/:roleCode"
          element={
            <RequirePermission resource="page.admin.security">
              <RoleDetailPage />
            </RequirePermission>
          }
        />
        <Route
          path="*"
          element={
            <PageUnavailable
              title="Page not found"
              purpose="That address does not match a screen in this application."
              heading="Page not found"
              detail="Nothing is wrong with the application. The address you followed does not match any screen in it, so there is nothing to show."
              blockedBy={['Nothing — check the link you followed']}
            />
          }
        />
      </Routes>
      </Suspense>
      </ErrorBoundary>
    </AppShell>
  );
}
