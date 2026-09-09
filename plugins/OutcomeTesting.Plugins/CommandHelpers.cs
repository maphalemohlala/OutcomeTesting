using System;
using System.Collections.Generic;
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
    public static class CommandHelpers
    {
        // Distinct failure prefixes so the client can branch (command-concurrency skill).
        public const string ConflictPrefix = "CONFLICT: ";
        public const string UnauthorizedPrefix = "UNAUTHORIZED: ";
        public const string PreconditionPrefix = "PRECONDITION: ";
        public const string ValidationPrefix = "VALIDATION: ";
        public const string NotFoundPrefix = "NOTFOUND: ";

        public const string AuditEntity = "al_auditevent";

        /// <summary>
        /// Retrieves every row matching <paramref name="query"/>, following Dataverse's
        /// paging cookie rather than stopping at the first page.
        ///
        /// A bare RetrieveMultiple returns at most 5000 rows and simply stops — no error,
        /// no signal. Any command that counts, exports or reconciles has to page, or it
        /// silently reports a truncated figure as a complete one. Callers that genuinely
        /// want one row should set TopCount and call RetrieveMultiple directly instead.
        /// </summary>
        public static List<Entity> RetrieveAll(IOrganizationService service, QueryExpression query)
        {
            if (service == null) throw new ArgumentNullException("service");
            if (query == null) throw new ArgumentNullException("query");

            // TopCount and PageInfo are mutually exclusive in Dataverse; a caller that set a
            // cap means it, so honour it rather than silently paging past it.
            if (query.TopCount.HasValue)
            {
                return new List<Entity>(service.RetrieveMultiple(query).Entities);
            }

            var all = new List<Entity>();
            query.PageInfo = new PagingInfo
            {
                Count = PageSize,
                PageNumber = 1,
                PagingCookie = null,
            };

            while (true)
            {
                var page = service.RetrieveMultiple(query);
                all.AddRange(page.Entities);

                if (!page.MoreRecords)
                {
                    return all;
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        /// <summary>Rows fetched per page when following a paging cookie.</summary>
        private const int PageSize = 5000;

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

        public static bool GetRequiredBool(IPluginExecutionContext context, string name)
        {
            var value = GetOptionalBool(context, name);
            if (!value.HasValue)
            {
                throw new InvalidPluginExecutionException(PreconditionPrefix + name + " is required.");
            }

            return value.Value;
        }

        public static bool? GetOptionalBool(IPluginExecutionContext context, string name)
        {
            object value;
            if (context.InputParameters.TryGetValue(name, out value) && value is bool)
            {
                return (bool)value;
            }

            return null;
        }

        public static int? GetOptionalInt(IPluginExecutionContext context, string name)
        {
            object value;
            if (context.InputParameters.TryGetValue(name, out value) && value is int)
            {
                return (int)value;
            }

            return null;
        }

        /// <summary>True when the row is in the Active state (statecode 0).</summary>
        public static bool IsActive(Entity record)
        {
            var state = record.GetAttributeValue<OptionSetValue>("statecode");
            return state == null || state.Value == 0;
        }

        /// <summary>
        /// Activates or deactivates a row. Deactivation is the sanctioned alternative to
        /// deletion for retained data (AD-037/OD-010), so the row and its history survive.
        /// </summary>
        public static void SetState(IOrganizationService service, string entity, Guid id, bool active)
        {
            service.Update(new Entity(entity, id)
            {
                ["statecode"] = new OptionSetValue(active ? 0 : 1),
                ["statuscode"] = new OptionSetValue(active ? 1 : 2),
            });
        }

        /// <summary>
        /// True when this plug-in is running inside the pipeline of <paramref name="messageName"/>
        /// — a Create issued from within a Custom API, say — found by walking the parent
        /// contexts. Bounded so a malformed chain cannot loop.
        /// </summary>
        public static bool IsWithinMessage(IPluginExecutionContext context, string messageName)
        {
            var parent = context == null ? null : context.ParentContext;
            var depth = 0;
            while (parent != null && depth++ < 16)
            {
                if (string.Equals(parent.MessageName, messageName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                parent = parent.ParentContext;
            }

            return false;
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

        /// <summary>
        /// Finds this command's own prior audit event for an idempotency key, so a retry
        /// replays its original result instead of acting twice.
        ///
        /// The key alone is not enough to identify a replay. Keys are supplied by the
        /// caller on unbound APIs and the key column is globally unique across the audit
        /// table, so matching on the key by itself lets a key first used by one command be
        /// replayed against another — returning a successful-looking response, built from
        /// the requested target and the other command's details, for work that never ran.
        /// Scoping to <paramref name="command"/> means a replay can only ever return the
        /// result of the same command that recorded it.
        /// </summary>
        public static Entity FindAuditByKey(IOrganizationService service, string idempotencyKey, int command)
        {
            var query = new QueryExpression(AuditEntity)
            {
                ColumnSet = new ColumnSet("al_targettable", "al_targetid", "al_details"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_idempotencykey", ConditionOperator.Equal, idempotencyKey);
            query.Criteria.AddCondition("al_command", ConditionOperator.Equal, command);

            var result = service.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0] : null;
        }

        /// <summary>
        /// A display name for an audit actor, or null when it cannot be established.
        ///
        /// The actor is normally a Dataverse user, so systemuser is asked first. A portal
        /// path passes the signed-in Contact instead - a Power Pages write reaches Dataverse
        /// as the site's application user, so InitiatingUserId names the site rather than
        /// the person (AD-053) - and that id resolves against contact. Asking both, in that
        /// order, keeps one column meaningful for both kinds of actor without the audit
        /// table having to record which kind it holds.
        ///
        /// Never throws. An audit event is immutable and required (NFR-AUD-01); losing the
        /// whole event because a name lookup failed would be a strictly worse outcome than
        /// an event whose actor is unnamed, and the id is stamped either way. That is also
        /// why the catch is deliberately broad: a missing row, a privilege the plug-in user
        /// lacks on systemuser, and a malformed id all arrive here as different exceptions
        /// and all mean the same thing to this method.
        /// </summary>
        public static string ResolveActorName(IOrganizationService service, Guid actorId)
        {
            if (service == null || actorId == Guid.Empty)
            {
                return null;
            }

            return NameFrom(service, "systemuser", actorId)
                ?? NameFrom(service, "contact", actorId);
        }

        private static string NameFrom(IOrganizationService service, string table, Guid id)
        {
            try
            {
                var row = service.Retrieve(table, id, new ColumnSet("fullname"));
                var name = row == null ? null : row.GetAttributeValue<string>("fullname");
                return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
            catch (Exception)
            {
                return null;
            }
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
            IPluginExecutionContext context,
            Guid? actorId = null,
            string actorName = null)
        {
            // The initiating user unless a caller names someone better. A portal path does:
            // its write reaches Dataverse as the site's application user, so the context
            // names the site and the signed-in Contact is the actual actor (AD-053).
            var actor = actorId ?? context.InitiatingUserId;

            var audit = new Entity(AuditEntity)
            {
                ["al_name"] = name,
                ["al_command"] = new OptionSetValue(command),
                ["al_targettable"] = targetTable,
                ["al_targetid"] = targetId.ToString("D"),
                ["al_actorid"] = actor.ToString("D"),
                ["al_idempotencykey"] = idempotencyKey,
                ["al_correlationid"] = context.CorrelationId.ToString("D"),
                ["al_occurredon"] = DateTime.UtcNow,
            };

            // al_actorname was never written by anything, so every history log fell back to
            // createdbyname - a service account on every row, whoever had actually acted.
            // Resolved here rather than at each of the call sites so one writer cannot start
            // naming its actor while the others carry on not naming theirs.
            var resolved = string.IsNullOrWhiteSpace(actorName)
                ? ResolveActorName(service, actor)
                : actorName.Trim();

            if (!string.IsNullOrEmpty(resolved))
            {
                audit["al_actorname"] = resolved;
            }

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
