using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Reduces a rich-text answer to a fixed vocabulary of markup (item 7, 2026-09-19).
    ///
    /// A rich-text answer is the one place in this system where markup written by one user
    /// is rendered into another user's browser, so it is the one place a stored cross-site
    /// script could live. Everything else the surfaces draw is escaped text.
    ///
    /// This REBUILDS rather than strips. It reads what arrived and emits a fresh document
    /// containing only tags and attributes it recognises, so anything it does not
    /// understand cannot survive by being spelled unusually - no blacklist to get around,
    /// no "&lt;scr&lt;script&gt;ipt&gt;" reassembling itself after a removal pass. Unknown
    /// tags are dropped and their text kept, because the text is still the checker's
    /// answer; script and style lose their contents too, since that content is code rather
    /// than words.
    ///
    /// Runs server-side in AnswerWriter, so it applies to every write whatever sent it -
    /// the portal, the Code App, or a request someone made by hand against the Web API.
    /// ResponseGuardPlugin calls it again pre-operation on al_response, so a write that
    /// never passes through AnswerWriter is cleaned too. Both of those are WRITE-time.
    /// Nothing sanitises at RENDER time: the surfaces emit al_answerrichtext as markup as
    /// it comes out of the column (OT Review Detail, OT Tax Notes), so what these two
    /// calls store is what a browser is handed. An earlier version of this comment
    /// claimed a render-time pass and there has never been one - do not relax anything
    /// here on the strength of a layer that does not exist (audit finding 11).
    ///
    /// Free of Dataverse types, like ResponseRules, so it is unit testable on its own.
    /// </summary>
    public static class HtmlSanitiser
    {
        /// <summary>
        /// The most markup one answer may store. Comfortably longer than any remedial note
        /// anyone has written, and short enough that a paste of an entire web page cannot
        /// fill the column. The cap is applied to the REBUILT markup, so it bounds what is
        /// stored rather than what was sent.
        /// </summary>
        public const int MaxLength = 32000;

        /// <summary>
        /// Tags kept, mapped to the tag emitted. b and i arrive from execCommand on some
        /// browsers and mean the same as strong and em, so they are folded together rather
        /// than stored as two spellings of one thing.
        /// </summary>
        private static readonly Dictionary<string, string> Allowed =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "p", "p" },
                { "br", "br" },
                { "strong", "strong" },
                { "b", "strong" },
                { "em", "em" },
                { "i", "em" },
                { "u", "u" },
                { "ul", "ul" },
                { "ol", "ol" },
                { "li", "li" },
                { "a", "a" },
            };

        /// <summary>Tags whose CONTENT is code, not words, and goes with them.</summary>
        private static readonly HashSet<string> Opaque =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "script", "style", "title", "textarea" };

        /// <summary>Emitted without a closing tag, so they never join the open stack.</summary>
        private static readonly HashSet<string> Void =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "br" };

        /// <summary>
        /// The safe markup for this answer, or null when it carries no words.
        ///
        /// Null rather than empty markup: an editor that has been focused and emptied
        /// leaves "&lt;p&gt;&lt;br&gt;&lt;/p&gt;" behind, and storing that would make an
        /// unanswered question look answered to every count and gate that reads it.
        /// </summary>
        public static string Clean(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var output = new StringBuilder();
            var open = new List<string>();
            var index = 0;

            while (index < html.Length)
            {
                var next = html.IndexOf('<', index);
                if (next < 0)
                {
                    AppendText(output, html.Substring(index));
                    break;
                }

                if (next > index)
                {
                    AppendText(output, html.Substring(index, next - index));
                }

                index = ReadTag(html, next, output, open);
            }

            // Whatever the editor left open, the rebuild closes. What is stored is always
            // well formed however it arrived.
            for (var i = open.Count - 1; i >= 0; i--)
            {
                output.Append("</").Append(open[i]).Append(">");
            }

            var cleaned = output.ToString();
            if (cleaned.Length > MaxLength)
            {
                cleaned = Truncate(cleaned);
            }

            return HasText(cleaned) ? cleaned : null;
        }

        /// <summary>
        /// Whether markup carries any words at all, ignoring tags and whitespace. What
        /// "answered" means for a rich-text question: SubmitReviewPlugin's mandatory gate
        /// and ResponseRules' shape check both ask this rather than testing the column for
        /// null, because an empty editor still sends markup.
        /// </summary>
        public static bool HasText(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return false;
            }

            var inside = false;
            for (var i = 0; i < html.Length; i++)
            {
                var c = html[i];
                if (c == '<')
                {
                    inside = true;
                }
                else if (c == '>')
                {
                    inside = false;
                }
                else if (!inside && !char.IsWhiteSpace(c))
                {
                    // A non-breaking space is whitespace to a reader even though char does
                    // not say so, and it is what an emptied editor tends to leave behind.
                    if (c == '\u00a0')
                    {
                        continue;
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads one tag at <paramref name="start"/>, emits its canonical form when it is
        /// allowed, and returns where reading should continue.
        /// </summary>
        private static int ReadTag(string html, int start, StringBuilder output, List<string> open)
        {
            // A comment can hide a conditional block that some browsers still act on, so it
            // goes whole rather than being read for tags.
            if (string.CompareOrdinal(html, start, "<!--", 0, 4) == 0)
            {
                var close = html.IndexOf("-->", start + 4, StringComparison.Ordinal);
                return close < 0 ? html.Length : close + 3;
            }

            if (start + 1 < html.Length && html[start + 1] == '!')
            {
                var doctypeEnd = html.IndexOf('>', start);
                return doctypeEnd < 0 ? html.Length : doctypeEnd + 1;
            }

            var end = html.IndexOf('>', start);
            if (end < 0)
            {
                // An unterminated tag is not text: emitting the rest would put a half
                // written "<script" into the page.
                return html.Length;
            }

            var inner = html.Substring(start + 1, end - start - 1).Trim();
            var closing = inner.StartsWith("/", StringComparison.Ordinal);
            if (closing)
            {
                inner = inner.Substring(1).Trim();
            }

            var name = ReadName(inner);
            if (name.Length == 0)
            {
                return end + 1;
            }

            if (Opaque.Contains(name))
            {
                // The tag goes and its text goes with it. Dropping only the tag would leave
                // the script body on the page as visible words, which is a different bug.
                return closing ? end + 1 : SkipTo(html, end + 1, name);
            }

            string emitted;
            if (!Allowed.TryGetValue(name, out emitted))
            {
                // Unknown tag, kept text: a div is not vocabulary but what was typed inside
                // one is still the answer.
                return end + 1;
            }

            if (closing)
            {
                CloseTag(output, open, emitted);
                return end + 1;
            }

            if (Void.Contains(emitted))
            {
                output.Append("<").Append(emitted).Append(" />");
                return end + 1;
            }

            if (emitted == "a")
            {
                var href = SafeHref(inner);
                if (href == null)
                {
                    // The anchor loses its href, not its words: the checker still sees what
                    // they wrote and nothing is clickable. Not pushed onto the open stack,
                    // so its closing tag is ignored as a stray.
                    return end + 1;
                }

                output.Append("<a href=\"").Append(href)
                    .Append("\" rel=\"noopener noreferrer\" target=\"_blank\">");
                open.Add("a");
                return end + 1;
            }

            output.Append("<").Append(emitted).Append(">");
            open.Add(emitted);
            return end + 1;
        }

        /// <summary>
        /// Closes <paramref name="tag"/> if it is open, closing anything opened inside it
        /// first. A closing tag for something never opened is ignored rather than emitted.
        /// </summary>
        private static void CloseTag(StringBuilder output, List<string> open, string tag)
        {
            var at = open.LastIndexOf(tag);
            if (at < 0)
            {
                return;
            }

            for (var i = open.Count - 1; i >= at; i--)
            {
                output.Append("</").Append(open[i]).Append(">");
                open.RemoveAt(i);
            }
        }

        /// <summary>Everything up to the matching close of an opaque tag, discarded.</summary>
        private static int SkipTo(string html, int from, string name)
        {
            var needle = "</" + name;
            var at = html.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return html.Length;
            }

            var end = html.IndexOf('>', at);
            return end < 0 ? html.Length : end + 1;
        }

        private static string ReadName(string inner)
        {
            var i = 0;
            while (i < inner.Length && (char.IsLetterOrDigit(inner[i]) || inner[i] == '-'))
            {
                i++;
            }

            return inner.Substring(0, i);
        }

        /// <summary>
        /// The href of an anchor when its scheme is one a reader may safely follow, else
        /// null. Relative links are refused too: this markup is rendered on two different
        /// hosts, so a relative target means different things in each.
        /// </summary>
        private static string SafeHref(string inner)
        {
            var raw = ReadAttribute(inner, "href");
            if (raw == null)
            {
                return null;
            }

            // Control characters and spaces are stripped before the scheme is read.
            // "java\tscript:" and " javascript:" are both live in some browsers, and both
            // would pass a test that only trimmed the ends.
            var compact = new StringBuilder();
            foreach (var c in raw)
            {
                if (!char.IsWhiteSpace(c) && !char.IsControl(c))
                {
                    compact.Append(c);
                }
            }

            var scheme = compact.ToString();
            var safe = scheme.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || scheme.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || scheme.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

            if (!safe)
            {
                return null;
            }

            return Escape(Decode(raw.Trim()));
        }

        /// <summary>The value of one attribute, quoted or bare, or null when absent.</summary>
        private static string ReadAttribute(string inner, string name)
        {
            var at = inner.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            while (at >= 0)
            {
                var before = at == 0 || char.IsWhiteSpace(inner[at - 1]);
                var after = at + name.Length;
                if (before && after < inner.Length)
                {
                    var i = after;
                    while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
                    if (i < inner.Length && inner[i] == '=')
                    {
                        i++;
                        while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
                        if (i >= inner.Length)
                        {
                            return null;
                        }

                        if (inner[i] == '"' || inner[i] == '\'')
                        {
                            var quote = inner[i];
                            var close = inner.IndexOf(quote, i + 1);
                            return close < 0 ? null : inner.Substring(i + 1, close - i - 1);
                        }

                        var space = i;
                        while (space < inner.Length && !char.IsWhiteSpace(inner[space])) space++;
                        return inner.Substring(i, space - i);
                    }
                }

                at = inner.IndexOf(name, at + 1, StringComparison.OrdinalIgnoreCase);
            }

            return null;
        }

        /// <summary>
        /// Text, decoded and re-encoded so it round-trips: an answer already holding
        /// "&amp;amp;" keeps one ampersand rather than growing one on every save.
        /// </summary>
        private static void AppendText(StringBuilder output, string text)
        {
            output.Append(Escape(Decode(text)));
        }

        private static string Decode(string text)
        {
            if (text.IndexOf('&') < 0)
            {
                return text;
            }

            var output = new StringBuilder(text.Length);
            var i = 0;
            while (i < text.Length)
            {
                if (text[i] != '&')
                {
                    output.Append(text[i++]);
                    continue;
                }

                var semi = text.IndexOf(';', i + 1);
                if (semi < 0 || semi - i > 10)
                {
                    output.Append(text[i++]);
                    continue;
                }

                var entity = text.Substring(i + 1, semi - i - 1);
                var decoded = DecodeEntity(entity);
                if (decoded == null)
                {
                    output.Append(text[i++]);
                    continue;
                }

                output.Append(decoded);
                i = semi + 1;
            }

            return output.ToString();
        }

        private static string DecodeEntity(string entity)
        {
            switch (entity.ToLowerInvariant())
            {
                case "amp": return "&";
                case "lt": return "<";
                case "gt": return ">";
                case "quot": return "\"";
                case "apos": return "'";
                case "nbsp": return "\u00a0";
            }

            if (entity.Length > 1 && entity[0] == '#')
            {
                var digits = entity.Substring(1);
                var hex = digits.Length > 1 && (digits[0] == 'x' || digits[0] == 'X');
                int code;
                var parsed = hex
                    ? int.TryParse(digits.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)
                    : int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out code);

                if (parsed && code > 0 && code <= 0x10FFFF)
                {
                    try
                    {
                        return char.ConvertFromUtf32(code);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        private static string Escape(string text)
        {
            var output = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                switch (c)
                {
                    case '&': output.Append("&amp;"); break;
                    case '<': output.Append("&lt;"); break;
                    case '>': output.Append("&gt;"); break;
                    case '"': output.Append("&quot;"); break;
                    default: output.Append(c); break;
                }
            }

            return output.ToString();
        }

        /// <summary>
        /// Cuts to the cap without splitting a tag or an entity, then closes what the cut
        /// left open, so a truncated answer is still well formed markup.
        ///
        /// Deliberately not "cut, then Clean the offcut": the closing tags a cut needs can
        /// push the result back over the cap, and re-cleaning it cuts to the same place and
        /// adds them again. That recursed until the test host died of a stack overflow.
        /// This walks down instead, and the cut index strictly decreases, so it ends.
        /// </summary>
        private static string Truncate(string cleaned)
        {
            var cut = SafeCut(cleaned, MaxLength);

            while (cut > 0)
            {
                var closers = ClosersFor(cleaned, cut);
                if (cut + closers.Length <= MaxLength)
                {
                    return cleaned.Substring(0, cut) + closers;
                }

                cut = SafeCut(cleaned, cut - closers.Length - 1);
            }

            return string.Empty;
        }

        /// <summary>
        /// The largest index at or below <paramref name="max"/> that is not inside a tag or
        /// an entity.
        /// </summary>
        private static int SafeCut(string cleaned, int max)
        {
            if (max <= 0)
            {
                return 0;
            }

            var cut = Math.Min(max, cleaned.Length);

            var lastOpen = cleaned.LastIndexOf('<', cut - 1);
            var lastClose = cleaned.LastIndexOf('>', cut - 1);
            if (lastOpen > lastClose)
            {
                cut = lastOpen;
            }

            if (cut <= 0)
            {
                return 0;
            }

            var lastAmp = cleaned.LastIndexOf('&', cut - 1);
            var lastSemi = cleaned.LastIndexOf(';', cut - 1);
            if (lastAmp > lastSemi && cut - lastAmp <= 10)
            {
                cut = lastAmp;
            }

            return cut < 0 ? 0 : cut;
        }

        /// <summary>
        /// The closing tags the first <paramref name="length"/> characters of already
        /// sanitised markup still owe. Reads our own canonical output, so a tag is a name
        /// followed by attributes we wrote ourselves.
        /// </summary>
        private static string ClosersFor(string cleaned, int length)
        {
            var open = new List<string>();

            for (var i = 0; i < length; i++)
            {
                if (cleaned[i] != '<')
                {
                    continue;
                }

                var end = cleaned.IndexOf('>', i);
                if (end < 0 || end >= length)
                {
                    break;
                }

                var inner = cleaned.Substring(i + 1, end - i - 1);
                i = end;

                var closing = inner.StartsWith("/", StringComparison.Ordinal);
                var name = ReadName(closing ? inner.Substring(1) : inner);
                if (name.Length == 0 || Void.Contains(name))
                {
                    continue;
                }

                if (closing)
                {
                    var at = open.LastIndexOf(name);
                    if (at >= 0)
                    {
                        open.RemoveRange(at, open.Count - at);
                    }
                }
                else
                {
                    open.Add(name);
                }
            }

            var closers = new StringBuilder();
            for (var i = open.Count - 1; i >= 0; i--)
            {
                closers.Append("</").Append(open[i]).Append(">");
            }

            return closers.ToString();
        }
    }
}
