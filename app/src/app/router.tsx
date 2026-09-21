import { Navigate, Route, Routes } from 'react-router-dom';
import { AppShell } from '../components/layout/AppShell';
import { PageUnavailable } from '../components/feedback/PageUnavailable';
import { RequirePermission } from './permissions/PermissionGate';
import { CaseWorklistPage } from '../features/cases/CaseWorklistPage';
import { CaseDetailPage } from '../features/cases/CaseDetailPage';
import { RecheckPage } from '../features/cases/RecheckPage';
import { CaseIntakePage } from '../features/imports/CaseIntakePage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { PeoplePage } from '../features/people/PeoplePage';
import { PersonCasesPage } from '../features/people/PersonCasesPage';
import { AdviserMappingPage } from '../features/admin/AdviserMappingPage';
import { NotificationTemplatePage } from '../features/admin/NotificationTemplatePage';
import { QuestionLibraryPage } from '../features/admin/QuestionLibraryPage';
import { ListOptionsPage } from '../features/admin/ListOptionsPage';
import { SecurityConfigPage } from '../features/admin/SecurityConfigPage';
import { RoleDetailPage } from '../features/admin/RoleDetailPage';
import { ReviewDetailPage } from '../features/reviews/ReviewDetailPage';
import { RemediationPage } from '../features/remediation/RemediationPage';
import { ReportsPage } from '../features/reports/ReportsPage';
import { ExportsPage } from '../features/reports/ExportsPage';

/** Routes follow 09-Application-Screens. Screens without a data source say so explicitly. */
export function AppRoutes() {
  return (
    <AppShell>
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
        {/* People is a view over case data, so it is gated by the same resource (AD-041). */}
        <Route
          path="/people"
          element={
            <RequirePermission resource="page.cases">
              <PeoplePage />
            </RequirePermission>
          }
        />
        <Route
          path="/people/:role/:name"
          element={
            <RequirePermission resource="page.cases">
              <PersonCasesPage />
            </RequirePermission>
          }
        />
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
        {/* Adviser -> T&C Manager mapping (Fixes 5, AD-162). Routing only: it decides who is
            told a sign-off is waiting, never who may perform one. */}
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
        <Route path="/admin/users" element={<Navigate to="/people" replace />} />
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
    </AppShell>
  );
}
