using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Refuses a notification template that would send a broken letter (Change 1, AD-163).
    /// Registered as a synchronous pre-operation step on Create and Update of
    /// <c>al_notificationtemplate</c>.
    ///
    /// <para>
    /// <b>Server-side, not in the editor.</b> The Code App screen checks the same things, but
    /// a check that lives only in a screen is a suggestion: this table is reachable from the
    /// Web API and from any other client, and a bad row here is not a page that looks wrong,
    /// it is a letter somebody receives about a client's advice outcome.
    /// </para>
    /// <para>
    /// <b>It also cleans the body</b> (AD-169). An administrator writing a letter is writing
    /// markup that lands in somebody's inbox, so the stored body is reduced to
    /// <see cref="HtmlSanitiser"/>'s allow-list before it is written — the same treatment
    /// <c>ResponseGuardPlugin</c> gives <c>al_answerrichtext</c>, and for the same reason: the
    /// table is reachable from the Web API, so a screen that only produces safe markup is not
    /// a guarantee that only safe markup arrives.
    /// </para>
    /// <para>
    /// Three refusals, and all three are about what the reader would get:
    /// </para>
    /// <list type="bullet">
    ///   <item>An unrecognised code, because nothing in the solution would ever read it, so
    ///   the row would look like configuration while changing nothing.</item>
    ///   <item>A token the letter never supplies, because it renders as a gap rather than an
    ///   error - the administrator would see their words disappear and have nothing to
    ///   explain it.</item>
    ///   <item>An empty subject or body, which is the one case that sends a letter with
    ///   nothing in it. <c>NotificationTemplates.Render</c> falls back rather than send it,
    ///   so this refusal is what stops the fallback silently masking a save that did not do
    ///   what the administrator thought.</item>
    /// </list>
    /// <para>
    /// An update is checked against the MERGED row, not against the fields that happened to
    /// change. Clearing a body while leaving the subject alone sends only the subject to the
    /// plug-in, and judging that in isolation would let the one shape this guard exists to
    /// prevent straight through.
    /// </para>
    /// </summary>
    public class NotificationTemplateGuardPlugin : PluginBase
    {
        public NotificationTemplateGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(NotificationTemplateGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var service = localPluginContext.PluginUserService;

            object target;
            if (!context.InputParameters.TryGetValue("Target", out target))
            {
                return;
            }

            var row = target as Entity;
            if (row == null
                || !string.Equals(
                    row.LogicalName, NotificationTemplates.TemplateEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var code = Merged(service, row, NotificationTemplates.CodeAttr);

            // Cleaned BEFORE anything is judged, so the refusals below rule on what will
            // actually be stored rather than on what was sent.
            var emptied = SanitiseBody(row);

            var subject = Merged(service, row, NotificationTemplates.SubjectAttr);
            var body = Merged(service, row, NotificationTemplates.BodyAttr);

            if (emptied)
            {
                // Distinguished from a body nobody typed. "It needs a body" to somebody who
                // just wrote one reads as a bug in the screen.
                throw new InvalidPluginExecutionException(
                    "Nothing in that body is formatting this system keeps, so saving it would "
                    + "leave the letter empty. Text, bold, italic, underline, lists and links "
                    + "are kept; anything else is removed.");
            }

            var refusal = Refusal(
                code,
                subject,
                body,
                MergedOption(service, row, NotificationTemplateRows.EventAttr),
                MergedOption(service, row, NotificationTemplateRows.RecipientKindAttr),
                MergedLookup(service, row, NotificationTemplateRows.RecipientContactAttr) != null);
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(refusal);
            }
        }

        /// <summary>
        /// Why this template cannot be saved, or null when it can.
        ///
        /// Separated from the plug-in plumbing so the rules are testable without a context,
        /// and so the Code App can be given the same words rather than inventing its own.
        /// </summary>
        public static string Refusal(string code, string subject, string body)
        {
            return Refusal(code, subject, body, null, null, false);
        }

        /// <summary>
        /// Why this template cannot be saved, or null when it can.
        ///
        /// <para>
        /// A letter an administrator invented is judged differently from one of the twelve
        /// (AD-168). It has no compiled copy behind it, so there is nothing for a bad row to
        /// fall back to: without an event nothing would ever raise it, and without a recipient
        /// there is nobody to send it to. Both would look like configuration and do nothing,
        /// which is the failure this guard exists to make loud.
        /// </para>
        /// </summary>
        public static string Refusal(
            string code,
            string subject,
            string body,
            int? eventValue,
            int? recipientKind,
            bool hasContact)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return "A notification template needs a template code.";
            }

            if (recipientKind.HasValue && !NotificationRecipients.IsKnown(recipientKind.Value))
            {
                return "That is not somebody this system can work out from a case. Choose the "
                    + "adviser, the T&C Manager, the para-planner, the checker, or a named contact.";
            }

            if (recipientKind == NotificationRecipients.KindContact && !hasContact)
            {
                return "This letter is set to go to a named contact, but no contact is chosen. "
                    + "Pick the person, or choose one of the roles instead.";
            }

            var definition = NotificationTemplates.Definition(code.Trim());
            if (definition == null)
            {
                return CustomRefusal(code.Trim(), subject, body, eventValue, recipientKind);
            }

            if (string.IsNullOrWhiteSpace(subject))
            {
                return "The " + definition.Name + " letter needs a subject line.";
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                // Without this the row is simply ignored at send time and the compiled copy
                // goes out instead - correct, and indistinguishable from the save having
                // worked.
                return "The " + definition.Name + " letter needs a body. Saving it empty would "
                    + "quietly fall back to the built-in wording instead of sending what you wrote.";
            }

            var unknown = NotificationTemplates.UnknownTokens(definition.Code, subject, body);
            if (unknown.Length > 0)
            {
                return "The " + definition.Name + " letter does not supply "
                    + Join(Braced(unknown)) + ". "
                    + (unknown.Length == 1 ? "It would render as a gap. " : "They would render as gaps. ")
                    + "This letter can use: "
                    + Join(Braced(NotificationTemplates.AllowedTokens(definition.Tokens))) + ".";
            }

            return null;
        }

        /// <summary>
        /// Why a letter of somebody's own cannot be saved, or null when it can.
        ///
        /// The tokens are the ones a case can answer whatever raised it. A letter attached to
        /// an arbitrary event has no claim on a grade or an outcome, which exist only at the
        /// moment one particular event fires - offering them would produce a letter that reads
        /// correctly on the screen where it was written and arrives with gaps in it.
        /// </summary>
        private static string CustomRefusal(
            string code, string subject, string body, int? eventValue, int? recipientKind)
        {
            if (!eventValue.HasValue)
            {
                return "\"" + code + "\" is a letter of your own, so it needs to be attached to "
                    + "something. Choose the event that should send it.";
            }

            if (!NotificationOutbox.IsKnownEvent(eventValue.Value))
            {
                return "That is not an event this system raises, so nothing would ever send "
                    + "\"" + code + "\".";
            }

            if (!recipientKind.HasValue)
            {
                return "\"" + code + "\" has nobody to go to. Choose who should receive it.";
            }

            if (string.IsNullOrWhiteSpace(subject))
            {
                return "\"" + code + "\" needs a subject line.";
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                // There is no compiled copy behind a letter of your own, so an empty one is
                // simply never sent - and nothing says so.
                return "\"" + code + "\" needs a body. A letter of your own has no built-in "
                    + "wording to fall back on, so an empty one is never sent at all.";
            }

            var unknown = NotificationTemplates.UnknownTokensAgainst(
                NotificationTemplateRows.CustomTokens, subject, body);

            if (unknown.Length > 0)
            {
                return "\"" + code + "\" cannot use " + Join(Braced(unknown)) + ". "
                    + (unknown.Length == 1 ? "It would render as a gap. " : "They would render as gaps. ")
                    + "A letter of your own can use: "
                    + Join(Braced(NotificationTemplates.AllowedTokens(
                        NotificationTemplateRows.CustomTokens))) + ".";
            }

            return null;
        }

        /// <summary>
        /// Reduces the body to the markup the allow-list keeps, in place, and reports whether
        /// that left nothing of something somebody actually wrote.
        ///
        /// <para>
        /// <b>Every letter, not only the ones marked HTML.</b> `NotificationDrain.Compose` puts
        /// the body in <c>email.description</c>, which is rendered as markup — that is what the
        /// HTML letters and <c>{{caseButton}}</c> rely on — so a tag in a letter the catalogue
        /// calls plain text is live markup all the same. Escaping a literal <c>&amp;</c> or
        /// <c>&lt;</c> there makes it DISPLAY correctly rather than breaking it, so there is no
        /// case for treating the two kinds differently.
        /// </para>
        /// <para>
        /// Only when the write carries a body. An update that changes the subject alone must
        /// not rewrite a body it never mentioned.
        /// </para>
        /// <para>
        /// <c>{{caseButton}}</c> survives untouched: it is plain text in the stored row and
        /// becomes markup only at render, from a value this assembly builds.
        /// </para>
        /// </summary>
        private static bool SanitiseBody(Entity row)
        {
            if (!row.Contains(NotificationTemplates.BodyAttr))
            {
                return false;
            }

            var original = row.GetAttributeValue<string>(NotificationTemplates.BodyAttr);
            if (string.IsNullOrWhiteSpace(original))
            {
                return false;
            }

            var cleaned = HtmlSanitiser.Clean(original);
            row[NotificationTemplates.BodyAttr] = cleaned;

            return string.IsNullOrWhiteSpace(cleaned);
        }

        /// <summary>The option this write leaves behind, merged as <see cref="Merged"/> is.</summary>
        private static int? MergedOption(IOrganizationService service, Entity row, string attribute)
        {
            var value = MergedValue<OptionSetValue>(service, row, attribute);
            return value == null ? (int?)null : value.Value;
        }

        /// <summary>The lookup this write leaves behind.</summary>
        private static EntityReference MergedLookup(
            IOrganizationService service, Entity row, string attribute)
        {
            return MergedValue<EntityReference>(service, row, attribute);
        }

        private static T MergedValue<T>(IOrganizationService service, Entity row, string attribute)
            where T : class
        {
            if (row.Contains(attribute))
            {
                return row.GetAttributeValue<T>(attribute);
            }

            if (row.Id == Guid.Empty)
            {
                return null;
            }

            try
            {
                var stored = service.Retrieve(
                    NotificationTemplates.TemplateEntity, row.Id, new ColumnSet(attribute));
                return stored.GetAttributeValue<T>(attribute);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The value after this write lands: what was sent if it was sent, and what the row
        /// already holds otherwise.
        ///
        /// A create has nothing behind it, so an absent field is absent. An update sends only
        /// what changed, so judging it alone would let "clear the body, keep the subject"
        /// through - the exact shape this guard exists to refuse.
        /// </summary>
        private static string Merged(IOrganizationService service, Entity row, string attribute)
        {
            if (row.Contains(attribute))
            {
                return row.GetAttributeValue<string>(attribute);
            }

            if (row.Id == Guid.Empty)
            {
                return null;
            }

            try
            {
                var stored = service.Retrieve(
                    NotificationTemplates.TemplateEntity, row.Id, new ColumnSet(attribute));
                return stored.GetAttributeValue<string>(attribute);
            }
            catch (Exception)
            {
                // A row that cannot be read is not evidence that the field is empty, and
                // refusing the save on that basis would block an edit for the wrong reason.
                return null;
            }
        }

        private static string[] Braced(string[] tokens)
        {
            var braced = new string[tokens.Length];
            for (var i = 0; i < tokens.Length; i++)
            {
                braced[i] = "{{" + tokens[i] + "}}";
            }

            return braced;
        }

        /// <summary>"a", "a and b", "a, b and c" - the list reads as a sentence.</summary>
        private static string Join(string[] values)
        {
            if (values.Length == 0)
            {
                return string.Empty;
            }

            if (values.Length == 1)
            {
                return values[0];
            }

            return string.Join(", ", values, 0, values.Length - 1) + " and " + values[values.Length - 1];
        }
    }
}
