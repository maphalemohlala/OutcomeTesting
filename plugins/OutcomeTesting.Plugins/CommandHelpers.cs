using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Shared building blocks for the server-side command plug-ins (AD-003). Keeps the
    /// distinct failure prefixes, input parsing, optimistic-concurrency detection,
    /// idempotency lookup and the immutable Audit Event write (BR-012, NFR-AUD-01) in one
    /// place so every command behaves identically.
    /// </summary>
    internal static class CommandHelpers
    {
        // Distinct failure prefixes so the client can branch (command-concurrency skill).
        public const string ConflictPrefix = "CONFLICT: ";
        public const string UnauthorizedPrefix = "UNAUTHORIZED: ";
        public const string PreconditionPrefix = "PRECONDITION: ";
        public const string ValidationPrefix = "VALIDATION: ";
        public const string NotFoundPrefix = "NOTFOUND: ";

        public const string AuditEntity = "al_auditevent";

        public static Guid ParseRequiredGuid(IPluginExecutionContext context, string name)
        {
            var raw = GetRequiredString(context, name);
            Guid value;
            if (!Guid.TryParse(raw, out value) || value == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(PreconditionPrefix + name + " must be a valid record id.");
            }

            return value;
        }

        public static string GetRequiredString(IPluginExecutionContext context, string name)
        {
            var value = GetOptionalString(context, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidPluginExecutionException(PreconditionPrefix + name + " is required.");
            }

            return value;
        }

        public static string GetOptionalString(IPluginExecutionContext context, string name)
        {
            object value;
            if (context.InputParameters.TryGetValue(name, out value) && value is string)
            {
                return (string)value;
            }

            return null;
        }

        public static bool IsConcurrencyFault(FaultException<OrganizationServiceFault> fault)
        {
            // ConcurrencyVersionMismatch (0x80060892); fall back to message text in case
            // the exact code varies by platform build.
            if (fault.Detail != null && fault.Detail.ErrorCode == unchecked((int)0x80060892))
            {
                return true;
            }

            var message = fault.Message ?? string.Empty;
            return message.IndexOf("row version", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("concurrency", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static Entity FindAuditByKey(IOrganizationService service, string idempotencyKey)
        {
            var query = new QueryExpression(AuditEntity)
            {
                ColumnSet = new ColumnSet("al_targetid", "al_details"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_idempotencykey", ConditionOperator.Equal, idempotencyKey);

            var result = service.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0] : null;
        }

        public static Guid WriteAuditEvent(
            IOrganizationService service,
            int command,
            string name,
            string targetTable,
            Guid targetId,
            string reason,
            string details,
            string idempotencyKey,
            IPluginExecutionContext context)
        {
            var audit = new Entity(AuditEntity)
            {
                ["al_name"] = name,
                ["al_command"] = new OptionSetValue(command),
                ["al_targettable"] = targetTable,
                ["al_targetid"] = targetId.ToString("D"),
                ["al_actorid"] = context.InitiatingUserId.ToString("D"),
                ["al_idempotencykey"] = idempotencyKey,
                ["al_correlationid"] = context.CorrelationId.ToString("D"),
                ["al_occurredon"] = DateTime.UtcNow,
            };

            if (!string.IsNullOrEmpty(reason))
            {
                audit["al_reason"] = reason;
            }

            if (!string.IsNullOrEmpty(details))
            {
                audit["al_details"] = details;
            }

            return service.Create(audit);
        }
    }
}
