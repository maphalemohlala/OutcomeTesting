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
            var subject = Merged(service, row, NotificationTemplates.SubjectAttr);
            var body = Merged(service, row, NotificationTemplates.BodyAttr);

            var refusal = Refusal(code, subject, body);
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
            if (string.IsNullOrWhiteSpace(code))
            {
                return "A notification template needs a template code.";
            }

            var definition = NotificationTemplates.Definition(code.Trim());
            if (definition == null)
            {
                return "\"" + code.Trim() + "\" is not a notification this solution sends, so a template "
                    + "for it would never be read. Check the code against the list of letters.";
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
                    + Join(unknown) + ". "
                    + (unknown.Length == 1 ? "It would render as a gap. " : "They would render as gaps. ")
                    + "This letter can use: " + Join(Braced(definition.Tokens)) + ".";
            }

            return null;
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
