using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command ResolveImportException (AD-003, BR-002, FR-002/FR-003).
    /// Registered against the Custom API message <c>al_ResolveImportException</c>.
    ///
    /// An import exception is a row the extract could not become a case, held with the
    /// reason it was refused. Closing one is the other half of BR-002's "return invalid
    /// cases with a reason": the row goes back to whoever produced the extract, and the
    /// exception is closed with a note saying what happened to it.
    ///
    /// Two closures, both already in the deployed option set, and no third is invented
    /// here: <c>Resolved</c> when the row was corrected and re-imported, <c>Ignored</c>
    /// when it was never a case. The note is mandatory either way, because an exception
    /// closed with no explanation is indistinguishable from one closed by accident, and
    /// AD-037 forbids deleting the row to undo it.
    /// </summary>
    public class ResolveImportExceptionPlugin : PluginBase
    {
        private const string InExceptionId = "ExceptionId";
        private const string InResolution = "Resolution";
        private const string InNote = "Note";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutExceptionId = "ExceptionId";
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";

        private const string ExceptionEntity = "al_importexception";
        private const string StatusAttr = "al_exceptionstatus";
        private const string ResolvedOnAttr = "al_resolvedon";
        private const string NoteAttr = "al_resolutionnote";

        private const int StatusOpen = 120910740;
        private const int StatusResolved = 120910741;
        private const int StatusIgnored = 120910742;

        private const int CommandResolveImportException = 120910791;

        public ResolveImportExceptionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ResolveImportExceptionPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService; // caller privileges gate the write
            var systemService = localPluginContext.PluginUserService;   // audit always writes

            var exceptionId = CommandHelpers.ParseRequiredGuid(context, InExceptionId);
            var resolutionLabel = CommandHelpers.GetRequiredString(context, InResolution);
            var note = CommandHelpers.GetRequiredString(context, InNote);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            var resolution = ParseResolution(resolutionLabel);
            var canonical = ResolutionLabel(resolution);

            PermissionHelpers.EnsureAppPermission(systemService, context, "page.imports", PermissionHelpers.AccessEdit);

            // A replay returns the original closure rather than closing twice (NFR-REL-01).
            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandResolveImportException);
            if (existingAudit != null)
            {
                var priorStatus = existingAudit.GetAttributeValue<string>("al_details");
                SetResponse(context, exceptionId, string.IsNullOrEmpty(priorStatus) ? canonical : priorStatus, existingAudit.Id);
                return;
            }

            // Confirm it exists and the caller can read it before writing.
            var record = userService.Retrieve(ExceptionEntity, exceptionId, new ColumnSet(StatusAttr, "al_casereference"));
            EnsureOpen(record);

            userService.Update(new Entity(ExceptionEntity, exceptionId)
            {
                [StatusAttr] = new OptionSetValue(resolution),
                [ResolvedOnAttr] = DateTime.UtcNow,
                [NoteAttr] = note,
            });

            var reference = record.GetAttributeValue<string>("al_casereference");
            var auditId = CommandHelpers.WriteAuditEvent(
                systemService,
                CommandResolveImportException,
                "ResolveImportException " + exceptionId.ToString("D"),
                ExceptionEntity,
                exceptionId,
                note,
                canonical,
                idempotencyKey,
                context);

            localPluginContext.Trace("Import exception " + (reference ?? "(no reference)") + " closed as " + canonical);

            SetResponse(context, exceptionId, canonical, auditId);
        }

        /// <summary>
        /// Only an open exception can be closed. Without this, a second closure would
        /// overwrite the first one's note and timestamp, and the record of who actually
        /// dealt with the row — the thing the note exists to preserve — would be gone.
        /// </summary>
        public static void EnsureOpen(Entity record)
        {
            var status = record.GetAttributeValue<OptionSetValue>(StatusAttr);
            if (status == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "This import exception has no status recorded and cannot be closed.");
            }

            if (status.Value != StatusOpen)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "This import exception is already " +
                    ResolutionLabel(status.Value).ToLowerInvariant() + ".");
            }
        }

        /// <summary>
        /// The two closures the deployed option set carries. Reopening is not one of them:
        /// no requirement describes it, and a closed exception whose row was re-imported is
        /// history, not a live item.
        /// </summary>
        public static int ParseResolution(string label)
        {
            switch ((label ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "resolved": return StatusResolved;
                case "ignored": return StatusIgnored;
                default:
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + "Resolution must be Resolved or Ignored.");
            }
        }

        public static string ResolutionLabel(int value)
        {
            switch (value)
            {
                case StatusOpen: return "Open";
                case StatusResolved: return "Resolved";
                case StatusIgnored: return "Ignored";
                default: return "Unknown";
            }
        }

        private static void SetResponse(IPluginExecutionContext context, Guid exceptionId, string status, Guid auditEventId)
        {
            context.OutputParameters[OutExceptionId] = exceptionId.ToString("D");
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditEventId.ToString("D");
        }
    }
}
