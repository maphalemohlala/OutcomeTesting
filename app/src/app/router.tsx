import { Navigate, Route, Routes } from 'react-router-dom';
import { AppShell } from '../components/layout/AppShell';
import { NotBuiltYet } from '../components/feedback/NotBuiltYet';
import { RequirePermission } from './permissions/PermissionGate';
import { CaseWorklistPage } from '../features/cases/CaseWorklistPage';
import { CaseDetailPage } from '../features/cases/CaseDetailPage';
import { AllocationPage } from '../features/cases/AllocationPage';
import { CaseIntakePage } from '../features/imports/CaseIntakePage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { PeoplePage } from '../features/people/PeoplePage';
import { PersonCasesPage } from '../features/people/PersonCasesPage';
import { QuestionLibraryPage } from '../features/admin/QuestionLibraryPage';
import { SecurityConfigPage } from '../features/admin/SecurityConfigPage';
import { UsersPage } from '../features/admin/UsersPage';
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
        <Route
          path="/cases/:caseId/allocation"
          element={
            <RequirePermission resource="page.cases">
              <AllocationPage />
            </RequirePermission>
          }
        />
        <Route
          path="/cases/:caseId/remediation"
          element={
            <RequirePermission resource="page.remediation">
              <RemediationPage />
            </RequirePermission>
          }
        />
        <Route
          path="/cases/:caseId/recheck"
          element={
            <NotBuiltYet
              title="Recheck and regrade"
              purpose="Record a recheck and set the final outcome while preserving the initial one (FR-024)."
              blockedBy={['Nothing — the screen itself is the gap. OD-007 is resolved (AD-031), al_Outcome carries the final outcome (AD-032) and al_RegradeCase is registered']}
            />
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
        <Route
          path="/admin/users"
          element={
            <RequirePermission resource="page.admin.users">
              <UsersPage />
            </RequirePermission>
          }
        />
        <Route
          path="/admin/security"
          element={
            <RequirePermission resource="page.admin.security">
              <SecurityConfigPage />
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
