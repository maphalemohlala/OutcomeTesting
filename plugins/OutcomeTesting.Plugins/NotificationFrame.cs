using System;
using System.Text;
using System.Text.RegularExpressions;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The frame every markup letter is sent in: the product's name across the top, one
    /// typeface and width, and a footer saying the message is automated (owner, 2026-10-08:
    /// "ensure that the letters have a professional styling as well as the links/button").
    ///
    /// <para>
    /// <b>Applied at send, by <see cref="NotificationDrain"/>, not written into the wording.</b>
    /// A stored template is cleaned to plain tags on save (<see cref="HtmlSanitiser"/>), so
    /// styling typed into one would be stripped; and the outbox row keeps the letter as its
    /// words, which is what an administrator reads there.
    /// </para>
    /// <para>
    /// <b>Tables and inline styles</b>, because that is what mail clients render: Outlook on
    /// Windows ignores a style element, max-width and most of CSS layout. The fixed width is
    /// the attribute Outlook reads; the style lets a phone shrink it.
    /// </para>
    /// <para>
    /// <b>Plain letters too</b> (owner, same day: every email the same). A body that does not
    /// open with a tag - allocation, approval, the para-planner's note - becomes one paragraph,
    /// its line breaks kept and its bare web address made a link. It is not escaped: a stored
    /// template is markup whatever its letter (AD-170), so its values arrive escaped already,
    /// and the email has always rendered it as markup.
    /// </para>
    /// </summary>
    public static class NotificationFrame
    {
        /// <summary>Marks a body that is already framed, so a re-drain does not frame it twice.</summary>
        public const string Marker = "<!--ot-letter-frame-->";

        /// <summary>The colour the case button is drawn in; the header matches it.</summary>
        public const string Brand = "#0b5394";

        /// <summary>The typeface the button already uses.</summary>
        public const string Font = "font-family:Segoe UI,Arial,sans-serif;";

        /// <summary>The body in the frame, or the body unchanged where it is not markup.</summary>
        public static string Wrap(string body, string product)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return body;
            }

            var trimmed = body.TrimStart();
            if (trimmed.StartsWith(Marker, StringComparison.Ordinal))
            {
                return body;
            }

            if (!trimmed.StartsWith("<", StringComparison.Ordinal))
            {
                body = "<p>" + Linked(body.Trim()).Replace("\r\n", "\n").Replace("\n", "<br />") + "</p>";
            }

            var name = NotificationTemplates.Html(
                string.IsNullOrWhiteSpace(product) ? ProductName.Default : product.Trim());

            return new StringBuilder()
                .Append(Marker)
                .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" ")
                .Append("style=\"background-color:#f3f5f8;\"><tr><td align=\"center\" style=\"padding:24px 12px;\">")
                .Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" ")
                .Append("style=\"width:100%;max-width:600px;background-color:#ffffff;border:1px solid #dde3ea;\">")

                // Header: the product, as the recipient knows it in this environment.
                .Append("<tr><td style=\"background-color:").Append(Brand).Append(";padding:16px 28px;")
                .Append(Font).Append("font-size:18px;font-weight:600;color:#ffffff;\">")
                .Append(name)
                .Append("</td></tr>")

                // The letter.
                .Append("<tr><td style=\"padding:24px 28px;").Append(Font)
                .Append("font-size:14px;line-height:1.6;color:#1f2933;\">")
                .Append(body)
                .Append("</td></tr>")

                // Footer: why nobody should reply to it.
                .Append("<tr><td style=\"padding:16px 28px;border-top:1px solid #dde3ea;").Append(Font)
                .Append("font-size:12px;line-height:1.5;color:#6b7785;\">")
                .Append("This is an automated message from ").Append(name)
                .Append(". Please do not reply to this email.")
                .Append("</td></tr>")

                .Append("</table></td></tr></table>")
                .ToString();
        }

        /// <summary>
        /// Plain text with each bare web address made a link. Punctuation that ends the
        /// sentence is not part of the address: "...?id=1." links to "...?id=1".
        /// </summary>
        private static string Linked(string text)
        {
            return Regex.Replace(text, @"https?://[^\s<>""]+", match =>
            {
                var url = match.Value.TrimEnd('.', ',', ';', ':', ')', '!', '?');
                return "<a href=\"" + url + "\" style=\"color:" + Brand + ";\">" + url + "</a>"
                    + match.Value.Substring(url.Length);
            });
        }
    }
}
