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

        // The output parameters every al_* command answers with. See SetResponse.
        public const string OutStatus = "Status";
        public const string OutAuditEventId = "AuditEventId";
        public const string OutConflict = "Conflict";
        public const string ValidationPrefix = "VALIDATION: ";
        public const string NotFoundPrefix = "NOTFOUND: ";

        public const string AuditEntity = "al_auditevent";

        /// <summary>Every prefix a caller is meant to branch on, longest-lived first.</summary>
        private static readonly string[] RefusalPrefixes =
        {
            ConflictPrefix,
            UnauthorizedPrefix,
            PreconditionPrefix,
            ValidationPrefix,
            NotFoundPrefix,
        };

        /// <summary>
        /// The refusal carried inside a fault message, or null when there is none.
        ///
        /// <para>A refusal thrown by one command does NOT reach a command that called it as an
        /// <see cref="InvalidPluginExecutionException"/>. It crosses the service boundary as a
        /// <c>FaultException&lt;OrganizationServiceFault&gt;</c>, so the outer plug-in's catch
        /// treated a perfectly good PRECONDITION as an unexpected crash and re-labelled it —
        /// "UNEXPECTED: OutcomeTesting.Plugins.SignoffRequestPlugin could not complete.
        /// OrganizationServiceFault: PRECONDITION: This remediation action has already been
        /// approved." Seen on 2026-09-21 running PRT-093. Three faults with that: the caller's
        /// prefix branching cannot fire because the message no longer STARTS with the prefix;
        /// a deliberate refusal reads as a broken command; and the plug-in class name is named
        /// to whoever asked, which NFR-OBS-01 exists to prevent.</para>
        ///
        /// <para>Searching rather than matching the start is deliberate, and it collapses
        /// nesting of any depth: two commands deep produced two layers of packaging, and
        /// taking the message from the FIRST prefix onwards yields the original sentence
        /// whatever wrapped it. The full text still reaches the trace log and InnerException,
        /// so nothing is lost for diagnosis - only for the person reading the screen.</para>
        /// </summary>
        public static string RefusalWithin(string faultMessage)
        {
            if (string.IsNullOrEmpty(faultMessage))
            {
                return null;
            }

            var earliest = -1;
            foreach (var prefix in RefusalPrefixes)
            {
                var at = faultMessage.IndexOf(prefix, StringComparison.Ordinal);
                if (at >= 0 && (earliest < 0 || at < earliest))
                {
                    earliest = at;
                }
            }

            return earliest < 0 ? null : faultMessage.Substring(earliest);
        }

        /// <summary>
        /// Whether a platform fault is the caller being denied a Dataverse privilege.
        ///
        /// <para>Two phrasings, because the platform uses both: the security library's
        /// <c>SecLib::CheckPrivilege failed</c>, and the "Principal user (Id=...) is missing
        /// prvReadWhatever privilege" form.</para>
        /// </summary>
        public static bool IsPrivilegeDenied(string faultMessage)
        {
            if (string.IsNullOrEmpty(faultMessage))
            {
                return false;
            }

            return faultMessage.IndexOf("SecLib::CheckPrivilege", StringComparison.OrdinalIgnoreCase) >= 0
                || (faultMessage.IndexOf("is missing", StringComparison.OrdinalIgnoreCase) >= 0
                    && faultMessage.IndexOf("privilege", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// What a person is told when the PLATFORM refused them, rather than an application
        /// rule.
        ///
        /// <para>Carries no user id, no privilege id, no privilege name and no plug-in class
        /// name. The raw fault named all four - "SecLib::CheckPrivilege failed. User:
        /// 1fae3cf3-..., PrivilegeName: prvReadEntity, PrivilegeId: a3311f47-..." - which is
        /// unreadable to the person it is shown to and is exactly the internal detail
        /// NFR-OBS-01 keeps out of a browser. All of it still goes to the trace log and
        /// InnerException, which is where an administrator looks.</para>
        ///
        /// <para>The wording separates the two role systems on purpose. APP-003 found an
        /// account holding the Outcome Testing application role but NOT the Dataverse
        /// <c>Basic User</c> role, and the note that this has happened three times in a week
        /// says the confusion is the common case. Sending somebody to check their application
        /// role when the platform is what refused them wastes the one place they would have
        /// looked.</para>
        /// </summary>
        public const string PrivilegeDeniedMessage =
            UnauthorizedPrefix +
            "Your Dataverse security role does not allow this action. This is a platform " +
            "privilege rather than an Outcome Testing application role, so the two are worth " +
            "checking separately: ask an administrator to look at the security roles on your " +
            "user account. The platform trace log records which privilege was missing.";

        /// <summary>
        /// Retrieves a row a CALLER named, and turns "it is not there" into a sentence they
        /// can act on.
        ///
        /// <para>F1, 2026-09-20: <c>al_SignOffRemediation</c> and
        /// <c>al_CompleteRemediation</c> retrieved <c>al_remediationaction</c> straight from
        /// the TargetId with no guard, so passing a case id where an action id belonged
        /// answered <c>UNEXPECTED: ... OrganizationServiceFault: Entity
        /// 'al_remediationaction' With Id = ... Does Not Exist</c>. That names an internal
        /// table to whoever called it, which NFR-OBS-01 exists to prevent, and it reads as a
        /// broken command rather than as a wrong id.</para>
        ///
        /// <para>Only for an id that arrived from outside. An id read from a lookup on a row
        /// the platform handed us is NOT this: a missing row there is genuinely unexpected
        /// and should keep travelling as one, because swallowing it would hide a real
        /// referential fault behind a polite sentence.</para>
        ///
        /// <para><paramref name="message"/> carries no id, because the caller already has the
        /// one they sent and an id in a message is an internal detail to everyone else.</para>
        /// </summary>
        public static Entity RetrieveOrNotFound(
            IOrganizationService service,
            string entityName,
            Guid id,
            ColumnSet columns,
            string message)
        {
            try
            {
                return service.Retrieve(entityName, id, columns);
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                throw new InvalidPluginExecutionException(NotFoundPrefix + message);
            }
        }

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

        /// <summary>
        /// Who a drained notification is sent from: the account the step runs as, or null
        /// where the context names none. Both drain entry points ask the same question, so
        /// they get the same answer from one place.
        /// </summary>
        public static EntityReference Sender(IPluginExecutionContext context)
        {
            return context.UserId == Guid.Empty
                ? null
                : new EntityReference("systemuser", context.UserId);
        }

        /// <summary>
        /// How a contact is named in an audit detail line: name and id where the reference
        /// carries a name, the id alone where it does not, and an explicit "(none recorded)"
        /// rather than a blank where there is no contact at all.
        /// </summary>
        public static string Describe(EntityReference contact)
        {
            if (contact == null)
            {
                return "(none recorded)";
            }

            return string.IsNullOrEmpty(contact.Name)
                ? contact.Id.ToString("D")
                : contact.Name + " " + contact.Id.ToString("D");
        }

        /// <summary>The formatted (display) value of an attribute, or null where there is none.</summary>
        public static string Formatted(Entity entity, string attribute)
        {
            return entity.FormattedValues.ContainsKey(attribute) ? entity.FormattedValues[attribute] : null;
        }

        /// <summary>
        /// Writes the three output parameters every command in this solution answers with.
        ///
        /// The names are the command contract, not one plug-in's choice, which is why they
        /// live here: a command that spelled one of them differently would be accepted by the
        /// platform and then read as empty by every caller.
        /// </summary>
        public static void SetResponse(
            IPluginExecutionContext context, string status, Guid auditEventId, bool conflict)
        {
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditEventId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }

        /// <summary>
        /// True when the user belongs to the team. Used to decide whether a caller may act on
        /// a team-owned record, so it is deliberately one definition: two copies of a
        /// permission check are two things to keep in step, and only one of them gets fixed.
        /// </summary>
        public static bool IsTeamMember(IOrganizationService service, Guid teamId, Guid userId)
        {
            var query = new QueryExpression("teammembership")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);
            query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);

            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        /// <summary>
        /// Cuts a value to what its column holds. Every caller is guarding a Dataverse length
        /// limit, which is the one reason this solution ever truncates anything, so the rule
        /// lives here rather than three times over.
        /// </summary>
        public static string Truncate(string value, int length)
        {
            value = value ?? string.Empty;
            return value.Length > length ? value.Substring(0, length) : value;
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
            // ConcurrencyVersionMismatch is 0x80060882. This carried 0x80060892 - one digit
            // out - and its text fallback looked for "row version" with a space where the
            // platform writes "RowVersion", so a genuine edit conflict matched neither and
            // surfaced as UNEXPECTED with the raw fault instead of the CONFLICT reload prompt
            // (case 254398988, 2026-09-13). The text is compared with spaces removed.
            if (fault.Detail != null && fault.Detail.ErrorCode == unchecked((int)0x80060882))
            {
                return true;
            }

            var message = (fault.Message ?? string.Empty).Replace(" ", string.Empty);
            return message.IndexOf("rowversion", StringComparison.OrdinalIgnoreCase) >= 0
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

            return NameFrom(service, "systemuser", "systemuserid", actorId)
                ?? NameFrom(service, "contact", "contactid", actorId);
        }

        /// <summary>
        /// The name on one row of <paramref name="table"/>, or null when it holds no such
        /// row. Asked as a query, and with no catch.
        ///
        /// An actor is a systemuser on a command and a Contact on a portal path (AD-053),
        /// so one of the two probes above is always for a row that does not exist. Retrieve
        /// faults on a missing row, and absorbing an OrganizationService fault is the one
        /// thing a plug-in may not do: the platform aborts the whole transaction of a
        /// plug-in that catches one and carries on - "ISV code reduced the open transaction
        /// count" - the rule OptionLabels and NotificationOutbox both record. This method
        /// used to Retrieve inside a broad catch, so every audit event written for a
        /// Contact actor poisoned its own transaction and the command died on its next
        /// write, which is what CompleteRequestPlugin's portal path did on 2026-09-10.
        ///
        /// A query answers "no such row" with an empty result and nothing to swallow, so
        /// the missing half costs a read rather than the transaction.
        /// </summary>
        private static string NameFrom(IOrganizationService service, string table, string idAttribute, Guid id)
        {
            var query = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet("fullname"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(idAttribute, ConditionOperator.Equal, id);

            var rows = service.RetrieveMultiple(query).Entities;
            if (rows.Count == 0)
            {
                return null;
            }

            var name = rows[0].GetAttributeValue<string>("fullname");
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
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
