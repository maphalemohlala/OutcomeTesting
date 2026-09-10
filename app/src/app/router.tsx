import { Navigate, Route, Routes } from 'react-router-dom';
import { AppShell } from '../components/layout/AppShell';
import { NotBuiltYet } from '../components/feedback/NotBuiltYet';
import { RequirePermission } from './permissions/PermissionGate';
import { CaseWorklistPage } from '../features/cases/CaseWorklistPage';
import { CaseDetailPage } from '../features/cases/CaseDetailPage';
import { RecheckPage } from '../features/cases/RecheckPage';
import { CaseIntakePage } from '../features/imports/CaseIntakePage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { PeoplePage } from '../features/people/PeoplePage';
import { PersonCasesPage } from '../features/people/PersonCasesPage';
import { QuestionLibraryPage } from '../features/admin/QuestionLibraryPage';
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
        <Route
          path="/"
          element={<DashboardPage />}
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
            <NotBuiltYet
              title="Page not found"
              purpose="That address does not match a screen in this application."
              blockedBy={['Nothing — check the link you followed']}
            />
          }
        />
      </Routes>
    </AppShell>
  );
}
