using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The stored side of the notification templates: the columns AD-168 added, the letters an
    /// administrator invented, and the recipient they chose for a built-in one.
    ///
    /// <para>
    /// <b>Why this is not in <see cref="NotificationTemplates"/>.</b> That file is LINKED into
    /// the registration tool so the seed and the fallback cannot drift (AD-163), which means
    /// everything in it has to compile against net8.0 as well as net462. This reads cases,
    /// contacts and assignments, which the tool has no business doing.
    /// </para>
    /// <para>
    /// <b>A custom letter may use only the tokens a case can always supply.</b> The built-in
    /// letters carry things like a grade or an outcome that exist only at the moment one
    /// particular event fires; a letter an administrator attaches to an arbitrary event has no
    /// claim on those. Offering them would produce a letter that reads correctly on the screen
    /// where it was written and arrives with gaps in it.
    /// </para>
    /// </summary>
    public static class NotificationTemplateRows
    {
        /// <summary>The event this template is sent at.</summary>
        public const string EventAttr = "al_event";

        /// <summary>Who receives it (<see cref="NotificationRecipients"/>).</summary>
        public const string RecipientKindAttr = "al_recipientkind";

        /// <summary>The contact, when the kind is <see cref="NotificationRecipients.KindContact"/>.</summary>
        public const string RecipientContactAttr = "al_recipientcontactid";

        /// <summary>What an administrator called this letter.</summary>
        public const string NameAttr = "al_name";

        /// <summary>
        /// The tokens a custom letter may use: everything a case can answer at any moment.
        /// </summary>
        public static readonly string[] CustomTokens =
        {
            NotificationTemplates.TokenReference,
            NotificationTemplates.TokenAdviser,
            NotificationTemplates.TokenClient,
            NotificationTemplates.TokenCaseLink,
            NotificationTemplates.TokenCaseButton,
        };

        /// <summary>A letter an administrator created, bound to an event and a recipient.</summary>
        public sealed class CustomTemplate
        {
            /// <summary>The stable code they gave it.</summary>
            public string Code { get; set; }

            /// <summary>What it is called in a list.</summary>
            public string Name { get; set; }

            /// <summary>The subject, with tokens.</summary>
            public string Subject { get; set; }

            /// <summary>The body, with tokens.</summary>
            public string Body { get; set; }

            /// <summary>Which of the five kinds receives it.</summary>
            public int RecipientKind { get; set; }

            /// <summary>The contact, when the kind names one.</summary>
            public EntityReference RecipientContact { get; set; }
        }

        /// <summary>Who a stored row says a letter goes to; null where it does not say.</summary>
        public sealed class RecipientChoice
        {
            /// <summary>One of the five kinds.</summary>
            public int Kind { get; set; }

            /// <summary>The contact, when the kind names one.</summary>
            public EntityReference Contact { get; set; }
        }

        /// <summary>
        /// The letters an administrator has attached to this event, which are sent IN ADDITION
        /// to the built-in one.
        ///
        /// <para>
        /// Built-in codes are excluded: a row carrying one of the twelve is the built-in
        /// letter's own wording, and treating it as an extra would send that letter twice.
        /// </para>
        /// <para>
        /// Never throws, and an empty list is the ordinary answer. This runs inside the
        /// transaction that raised the notification, so a table that cannot be read costs the
        /// extra letters and never the save.
        /// </para>
        /// </summary>
        public static List<CustomTemplate> CustomFor(IOrganizationService service, int eventValue)
        {
            var found = new List<CustomTemplate>();

            try
            {
                var query = new QueryExpression(NotificationTemplates.TemplateEntity)
                {
                    ColumnSet = new ColumnSet(
                        NotificationTemplates.CodeAttr,
                        NotificationTemplates.SubjectAttr,
                        NotificationTemplates.BodyAttr,
                        NameAttr,
                        RecipientKindAttr,
                        RecipientContactAttr),
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition(EventAttr, ConditionOperator.Equal, eventValue);
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                query.AddOrder(NotificationTemplates.CodeAttr, OrderType.Ascending);

                foreach (var row in service.RetrieveMultiple(query).Entities)
                {
                    var code = row.GetAttributeValue<string>(NotificationTemplates.CodeAttr);
                    if (NotificationTemplates.Definition(code) != null)
                    {
                        continue;
                    }

                    var subject = row.GetAttributeValue<string>(NotificationTemplates.SubjectAttr);
                    var body = row.GetAttributeValue<string>(NotificationTemplates.BodyAttr);

                    // Both or neither, as Render requires of a built-in: a letter with an
                    // empty body reads as a fault at the far end. There is no compiled copy to
                    // fall back to here, so the letter is simply not sent.
                    if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
                    {
                        continue;
                    }

                    var kind = row.GetAttributeValue<OptionSetValue>(RecipientKindAttr);
                    if (kind == null || !NotificationRecipients.IsKnown(kind.Value))
                    {
                        // Without a recipient there is nobody to send to, and queuing it
                        // unaddressed would put a row in the outbox that can never drain.
                        continue;
                    }

                    found.Add(new CustomTemplate
                    {
                        Code = code,
                        Name = row.GetAttributeValue<string>(NameAttr),
                        Subject = subject,
                        Body = body,
                        RecipientKind = kind.Value,
                        RecipientContact = row.GetAttributeValue<EntityReference>(RecipientContactAttr),
                    });
                }
            }
            catch (Exception)
            {
                return found;
            }

            return found;
        }

        /// <summary>
        /// The recipient a stored row chooses for a built-in letter, or null where it chooses
        /// none and the code's own routing stands.
        /// </summary>
        public static RecipientChoice OverrideFor(IOrganizationService service, string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            try
            {
                var query = new QueryExpression(NotificationTemplates.TemplateEntity)
                {
                    ColumnSet = new ColumnSet(RecipientKindAttr, RecipientContactAttr),
                    TopCount = 1,
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition(NotificationTemplates.CodeAttr, ConditionOperator.Equal, code);
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

                var rows = service.RetrieveMultiple(query).Entities;
                if (rows.Count == 0)
                {
                    return null;
                }

                var kind = rows[0].GetAttributeValue<OptionSetValue>(RecipientKindAttr);
                if (kind == null || !NotificationRecipients.IsKnown(kind.Value))
                {
                    return null;
                }

                return new RecipientChoice
                {
                    Kind = kind.Value,
                    Contact = rows[0].GetAttributeValue<EntityReference>(RecipientContactAttr),
                };
            }
            catch (Exception)
            {
                // An unreadable table leaves the built-in routing in place, which is what the
                // letter did before anybody edited it.
                return null;
            }
        }

        /// <summary>
        /// The tokens any letter about a case can be given, whatever event raised it.
        ///
        /// Values are what the case holds; a missing one becomes an empty string rather than a
        /// visible placeholder, which is what <c>Substitute</c> does for the built-ins too.
        /// </summary>
        public static Dictionary<string, string> CaseTokens(
            IOrganizationService service, EntityReference caseRef)
        {
            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
            if (caseRef == null)
            {
                return tokens;
            }

            try
            {
                var row = service.Retrieve(
                    "al_outcomecase",
                    caseRef.Id,
                    new ColumnSet("al_casereference", "al_advisername", "al_clientname"));

                tokens[NotificationTemplates.TokenReference] =
                    NotificationTemplates.ReferenceOr(row.GetAttributeValue<string>("al_casereference"));
                tokens[NotificationTemplates.TokenAdviser] =
                    row.GetAttributeValue<string>("al_advisername") ?? string.Empty;
                tokens[NotificationTemplates.TokenClient] =
                    row.GetAttributeValue<string>("al_clientname") ?? string.Empty;

                var link = NotificationOutbox.CaseLink(service, caseRef);
                tokens[NotificationTemplates.TokenCaseLink] = link ?? string.Empty;
                tokens[NotificationTemplates.TokenCaseButton] = link == null
                    ? string.Empty
                    : NotificationTemplates.CaseButton(link, "Open the case");
            }
            catch (Exception)
            {
                return tokens;
            }

            return tokens;
        }
    }
}
