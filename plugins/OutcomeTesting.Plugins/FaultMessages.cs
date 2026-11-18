using System;
using System.Collections.Generic;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// What a person reads when a plug-in fails for a reason that is not one of our own
    /// refusals (owner decision, 2026-11-17): "PREFIX: action could not be completed because
    /// reason. What to do." No class name, exception type, table name or id - those go to
    /// the trace, which <see cref="PluginBase"/> writes before it throws.
    ///
    /// <para>The reason is decided by the fault's documented error code (Dataverse web
    /// service error codes), not by its text, except for the privilege denial that
    /// <see cref="CommandHelpers.IsPrivilegeDenied"/> has always recognised by text.</para>
    ///
    /// <para>No sentence here may carry a double quote or a backslash: the app and the portal
    /// end a server sentence at the first of either, because that is where an OData body's
    /// machine detail begins.</para>
    /// </summary>
    public static class FaultMessages
    {
        public const string UnexpectedPrefix = "UNEXPECTED: ";
        public const string FallbackAction = "Saving your change";

        private const string Because = " could not be completed because ";

        /// <summary>
        /// The plain action per plug-in. Every concrete plug-in must have one;
        /// FaultMessagesFixTests walks the assembly to hold that.
        /// </summary>
        public static readonly IReadOnlyDictionary<Type, string> Actions = new Dictionary<Type, string>
        {
            { typeof(AccountabilityRequestPlugin), "Recording who is accountable for the fail" },
            { typeof(AddQuestionPlugin), "Adding the question" },
            { typeof(AddSectionPlugin), "Adding the section" },
            { typeof(AdoptRoleAssignmentPlugin), "Settling the role assignment" },
            { typeof(AdviserContactPlugin), "Saving the contact" },
            { typeof(AdviserMappingPlugin), "Saving the adviser mapping" },
            { typeof(AnswerRequestPlugin), "Saving the answer" },
            { typeof(AqsQueueMembershipPlugin), "Updating the AQS queue membership" },
            { typeof(AssignCasePlugin), "Allocating the case" },
            { typeof(AssignUserRolePlugin), "Giving the user the role" },
            { typeof(CaseAccessGuardPlugin), "Saving the case" },
            { typeof(CaseAccessPlugin), "Updating who can see the case" },
            { typeof(CaseHeaderRequestPlugin), "Saving the case details" },
            { typeof(CaseStatusGuardPlugin), "Changing the case status" },
            { typeof(ClaimCasePlugin), "Claiming the case" },
            { typeof(ClaimRequestPlugin), "Claiming the case" },
            { typeof(CompleteRemediationPlugin), "Completing the remediation" },
            { typeof(CompleteRequestPlugin), "Completing the remediation" },
            { typeof(CreateExportBatchPlugin), "Starting the export" },
            { typeof(CreateRolePlugin), "Creating the role" },
            { typeof(CreateUserPlugin), "Adding the user" },
            { typeof(DeleteCasePlugin), "Deleting the case" },
            { typeof(ReassignTcManagerPlugin), "Handing the case to another T&C Manager" },
            { typeof(AmendOutcomePlugin), "Amending the outcome" },
            { typeof(DrainNotificationsPlugin), "Sending the notifications" },
            { typeof(GenerateExportPlugin), "Generating the export" },
            { typeof(GetMyRolesPlugin), "Loading your roles" },
            { typeof(GetRoleHoldersPlugin), "Loading who holds the role" },
            { typeof(ImportCasesPlugin), "Importing the cases" },
            { typeof(MoveQuestionPlugin), "Moving the question" },
            { typeof(NotificationDrainPlugin), "Sending the notification" },
            { typeof(NotificationEmitterPlugin), "Queueing the notification email" },
            { typeof(NotificationTemplateGuardPlugin), "Saving the notification template" },
            { typeof(OutcomeGuardPlugin), "Saving the outcome" },
            { typeof(ReconcileCaseAccessPlugin), "Repairing who can see the case" },
            { typeof(RegradeCasePlugin), "Regrading the case" },
            { typeof(RegradeRequestPlugin), "Recording the final outcome" },
            { typeof(RemedialActionsRequestPlugin), "Saving the remedial actions" },
            { typeof(RemediationResponseGuardPlugin), "Saving the adviser response" },
            { typeof(ResolveImportExceptionPlugin), "Resolving the import exception" },
            { typeof(ResponseGuardPlugin), "Saving the answer" },
            { typeof(ResponseProgressPlugin), "Saving the answer" },
            { typeof(RetireAndSucceedQuestionPlugin), "Changing the question" },
            { typeof(RetireQuestionPlugin), "Retiring the question" },
            { typeof(RetireSectionPlugin), "Retiring the section" },
            { typeof(ReviewSubmissionGuardPlugin), "Updating the check" },
            { typeof(SetFailAccountabilityPlugin), "Recording who is accountable for the fail" },
            { typeof(SetPagePermissionPlugin), "Saving the page permission" },
            { typeof(SetPermissionRuleActivePlugin), "Withdrawing or restoring the permission rule" },
            { typeof(SetRoleAssignmentActivePlugin), "Withdrawing or restoring the role" },
            { typeof(SetUserActivePlugin), "Deactivating or reactivating the user" },
            { typeof(SignOffRemediationPlugin), "Signing off the remediation" },
            { typeof(SignoffGuardPlugin), "Signing off the remediation" },
            { typeof(SignoffProgressPlugin), "Signing off the remediation" },
            { typeof(SignoffRequestPlugin), "Signing off the remediation" },
            { typeof(SubmitRequestPlugin), "Submitting the check" },
            { typeof(SubmitReviewPlugin), "Submitting the check" },
            { typeof(UpdateCaseDetailsPlugin), "Saving the case details" },
            { typeof(UpdateRolePlugin), "Saving the role" },
            { typeof(UpdateSectionPlugin), "Saving the section" },
            { typeof(UpdateUserPlugin), "Saving the user" },
        };

        // Dataverse web service error codes, by documented name.
        private static readonly HashSet<int> PrivilegeCodes = Codes(
            0x80040220, // PrivilegeDenied
            0x80040231, // PrincipalPrivilegeDenied
            0x80042f09); // UserNotAssignedRoles

        private static readonly HashSet<int> NotFoundCodes = Codes(
            0x80040217, // ObjectDoesNotExist
            0x80060891); // RecordNotFoundByEntityKey

        private static readonly HashSet<int> DuplicateCodes = Codes(
            0x80040237, // DuplicateRecord
            0x80060892); // DuplicateRecordEntityKey - the losing sign-off racer on al_SignoffCodeKey

        private static readonly HashSet<int> TimeoutCodes = Codes(
            0x80044151, // unManagedidssqltimeouterror (SQL timeout expired)
            0x80041222, // PSqlTimeoutDueToComplexQuery
            0x80048562, // DBQueryFailedDueToClosedTransaction (after a sandbox timeout)
            0x80048573, // LeadingWildcardCauseTimeout
            0x80048574, // ComputedColumnCauseTimeout
            0x80048575, // PerformanceValidationIssuesCauseTimeout
            0x80048553, // DBUpgradeCauseTimeout
            0x80048554, // SelfBlockedCauseTimeout
            0x8004ed47, // AppLockTimeout
            0x80044171, // SandboxClientPluginTimeout
            0x80044172, // SandboxHostPluginTimeout
            0x80044173, // SandboxWorkerPluginTimeout
            0x80081111, // SandboxWorkerPluginExecuteTimeout
            0x80048d07, // IsvAbortedRequestTimeout
            0x80048d10); // IsvAbortedGatewayTimeout

        private const string RoleReason =
            "your Dataverse security role does not allow it. This is a platform privilege rather " +
            "than one of this app's application roles: ask an administrator to check the security " +
            "roles on your user account.";
        private const string NotFoundReason =
            "a record it needs no longer exists. Nothing was saved. Reload the page and try again.";
        private const string DuplicateReason =
            "it has already been recorded. Reload the page to see it.";
        private const string ConcurrencyReason =
            "someone else changed it first. Nothing was saved. Reload the page and try again.";
        private const string TimeoutReason =
            "it took too long. Nothing was saved. Please try again.";
        private const string ServerReason =
            "of a problem on the server. Nothing was saved. Please try again, and if it " +
            "happens again, report it with the time it happened.";

        public static string ActionFor(Type pluginType)
        {
            string action;
            return pluginType != null && Actions.TryGetValue(pluginType, out action) ? action : FallbackAction;
        }

        /// <summary>The sentence for a platform fault that carried none of our own refusals.</summary>
        public static string ForFault(Type pluginType, FaultException<OrganizationServiceFault> fault)
        {
            var code = fault.Detail == null ? 0 : fault.Detail.ErrorCode;
            var action = ActionFor(pluginType);

            if (PrivilegeCodes.Contains(code) || CommandHelpers.IsPrivilegeDenied(fault.Message))
                return CommandHelpers.UnauthorizedPrefix + action + Because + RoleReason;
            if (DuplicateCodes.Contains(code))
                return CommandHelpers.ConflictPrefix + action + Because + DuplicateReason;
            if (CommandHelpers.IsConcurrencyFault(fault))
                return CommandHelpers.ConflictPrefix + action + Because + ConcurrencyReason;
            if (NotFoundCodes.Contains(code))
                return UnexpectedPrefix + action + Because + NotFoundReason;
            if (TimeoutCodes.Contains(code))
                return UnexpectedPrefix + action + Because + TimeoutReason;
            return UnexpectedPrefix + action + Because + ServerReason;
        }

        /// <summary>The sentence for any other exception a plug-in did not raise on purpose.</summary>
        public static string ForException(Type pluginType, Exception error)
        {
            return UnexpectedPrefix + ActionFor(pluginType) + Because
                + (error is TimeoutException ? TimeoutReason : ServerReason);
        }

        /// <summary>
        /// A friendly unexpected sentence a called command already wrote, or null. It names
        /// the inner action and reason, which say more than re-wrapping it as a server problem.
        /// </summary>
        public static string CarriedUnexpected(string faultMessage)
        {
            var at = faultMessage == null ? -1 : faultMessage.IndexOf(UnexpectedPrefix, StringComparison.Ordinal);
            return at >= 0 && faultMessage.IndexOf(Because, at, StringComparison.Ordinal) > at
                ? faultMessage.Substring(at)
                : null;
        }

        private static HashSet<int> Codes(params uint[] codes)
        {
            var set = new HashSet<int>();
            foreach (var code in codes)
            {
                set.Add(unchecked((int)code));
            }

            return set;
        }
    }
}
