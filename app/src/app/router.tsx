import { Route, Routes } from 'react-router-dom';
import { AppShell } from '../components/layout/AppShell';
import { NotBuiltYet } from '../components/feedback/NotBuiltYet';
import { CaseWorklistPage } from '../features/cases/CaseWorklistPage';

/** Routes follow 09-Application-Screens. Screens without a data source say so explicitly. */
export function AppRoutes() {
  return (
    <AppShell>
      <Routes>
        <Route
          path="/"
          element={
            <NotBuiltYet
              title="Dashboard"
              purpose="See the work waiting on you, what is ageing and what has failed validation."
              blockedBy={['Outcome Case, Case Assignment and Remediation Action tables']}
            />
          }
        />
        <Route path="/cases" element={<CaseWorklistPage />} />
        <Route
          path="/cases/:caseId"
          element={
            <NotBuiltYet
              title="Case detail"
              purpose="Review a single case, its checks, findings and history."
              blockedBy={['Outcome Case and Review Instance tables']}
            />
          }
        />
        <Route
          path="/cases/:caseId/allocation"
          element={
            <NotBuiltYet
              title="Allocation"
              purpose="Route a case to the correct team or reviewer and record why (FR-005, FR-006)."
              blockedBy={['Case Assignment table', 'OD-002']}
            />
          }
        />
        <Route
          path="/cases/:caseId/remediation"
          element={
            <NotBuiltYet
              title="Remediation"
              purpose="Complete required actions, attest to them and obtain sign-off (FR-020 to FR-023)."
              blockedBy={['Remediation Action and Sign-off tables']}
            />
          }
        />
        <Route
          path="/cases/:caseId/recheck"
          element={
            <NotBuiltYet
              title="Recheck and regrade"
              purpose="Record a recheck and set the final outcome while preserving the initial one (FR-024)."
              blockedBy={['Recheck and Outcome tables', 'OD-007']}
            />
          }
        />
        <Route
          path="/cases/:caseId/audit"
          element={
            <NotBuiltYet
              title="Audit history"
              purpose="Trace who changed what, and when, for a single case (FR-033)."
              blockedBy={['Audit Event table']}
            />
          }
        />
        <Route
          path="/reviews/:reviewId/tax"
          element={
            <NotBuiltYet
              title="Tax check"
              purpose="Complete the Tax-owned questions and route onward or return with a reason (FR-015)."
              blockedBy={['Review Instance and Response tables', 'OD-001']}
            />
          }
        />
        <Route
          path="/reviews/:reviewId/aqs"
          element={
            <NotBuiltYet
              title="AQS check"
              purpose="Complete the file and advice quality sections and issue the outcome (FR-011, BR-005)."
              blockedBy={['Review Instance and Response tables', 'OD-001']}
            />
          }
        />
        <Route
          path="/imports"
          element={
            <NotBuiltYet
              title="Case intake"
              purpose="Upload an approved extract and resolve the rows that failed validation (FR-001 to FR-003)."
              blockedBy={['Import Batch and Import Exception tables', 'OD-003']}
            />
          }
        />
        <Route
          path="/reports"
          element={
            <NotBuiltYet
              title="Management reporting"
              purpose="Review outcome volumes, remediation ageing and accountability (BR-010)."
              blockedBy={['Reporting model', 'OD-012']}
            />
          }
        />
        <Route
          path="/exports"
          element={
            <NotBuiltYet
              title="Exports"
              purpose="Prepare and reconcile Trail Light batches (FR-032)."
              blockedBy={['Export Batch and Export Record tables', 'OD-004']}
            />
          }
        />
        <Route
          path="/admin/questions"
          element={
            <NotBuiltYet
              title="Question library"
              purpose="Maintain question versions without altering historic responses (FR-030, FR-031)."
              blockedBy={['Checklist, Question and Question Version tables', 'OD-001']}
            />
          }
        />
        <Route
          path="/admin/security"
          element={
            <NotBuiltYet
              title="Security configuration"
              purpose="Check how Entra groups map to Dataverse teams and roles."
              blockedBy={['User Role Mapping table']}
            />
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
