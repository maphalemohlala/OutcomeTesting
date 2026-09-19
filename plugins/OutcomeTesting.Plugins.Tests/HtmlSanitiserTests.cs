using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The allow-list sanitiser that stands between a rich-text answer and everyone who
    /// reads it back (item 7, 2026-09-19).
    ///
    /// A rich-text answer is the only place in this system where one user's markup is
    /// rendered to another user's browser, so it is the only place a stored cross-site
    /// script could live. The sanitiser is deliberately a REBUILD rather than a strip: it
    /// parses what arrived and emits a fresh document from a fixed vocabulary, so anything
    /// it does not understand cannot survive by being spelled unusually. Tests that try the
    /// usual evasions are therefore tests of the design, not of a blacklist.
    ///
    /// Free of Dataverse types, like ResponseRules, so it needs no fake service.
    /// </summary>
    public class HtmlSanitiserTests
    {
        [Fact]
        public void KeepsTheFormattingTheEditorProduces()
        {
            const string html = "<p>Adviser must:</p><ul><li><strong>recalculate</strong> the LSA figure</li>"
                + "<li>reissue the <em>suitability report</em></li></ul>";

            Assert.Equal(html, HtmlSanitiser.Clean(html));
        }

        [Fact]
        public void DropsAScriptTagAndItsContents()
        {
            // The tag goes AND its text goes. A sanitiser that dropped only the tag would
            // leave the script body as visible page text, which is a different bug.
            Assert.Equal(
                "<p>before</p><p>after</p>",
                HtmlSanitiser.Clean("<p>before</p><script>alert(1)</script><p>after</p>"));
        }

        [Fact]
        public void DropsStyleContentsToo()
        {
            Assert.Equal("<p>x</p>", HtmlSanitiser.Clean("<style>body{display:none}</style><p>x</p>"));
        }

        [Theory]
        [InlineData("<img src=x onerror=alert(1)>")]
        [InlineData("<iframe src=\"https://evil.test\"></iframe>")]
        [InlineData("<object data=\"x\"></object>")]
        [InlineData("<svg onload=alert(1)></svg>")]
        [InlineData("<math><mi xlink:href=\"javascript:alert(1)\">x</mi></math>")]
        public void DropsEveryTagOutsideTheVocabulary(string html)
        {
            // Text inside a dropped tag is kept - it is still the checker's answer - so
            // what this asserts is that no MARKUP survives, not that nothing does.
            var cleaned = HtmlSanitiser.Clean(html);

            Assert.True(
                cleaned == null || cleaned.IndexOf('<') < 0,
                "markup survived: " + cleaned);
        }

        [Fact]
        public void KeepsTheTextInsideATagItDrops()
        {
            // A <div> is not in the vocabulary, but what the checker typed inside one is
            // still their answer.
            Assert.Equal("<p>kept</p>", HtmlSanitiser.Clean("<div><p>kept</p></div>"));
        }

        [Fact]
        public void StripsEveryAttributeExceptAnAnchorHref()
        {
            Assert.Equal(
                "<p>x</p>",
                HtmlSanitiser.Clean("<p class=\"a\" style=\"color:red\" onclick=\"alert(1)\">x</p>"));
        }

        [Theory]
        [InlineData("http://example.test/a")]
        [InlineData("https://example.test/a")]
        [InlineData("mailto:someone@example.test")]
        public void KeepsAnAnchorOnASafeScheme(string href)
        {
            Assert.Equal(
                "<a href=\"" + href + "\" rel=\"noopener noreferrer\" target=\"_blank\">link</a>",
                HtmlSanitiser.Clean("<a href=\"" + href + "\">link</a>"));
        }

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("JaVaScRiPt:alert(1)")]
        [InlineData("  javascript:alert(1)")]
        [InlineData("java\tscript:alert(1)")]
        [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
        [InlineData("vbscript:msgbox(1)")]
        public void RefusesAnUnsafeSchemeButKeepsTheWords(string href)
        {
            // The anchor loses its href rather than the answer losing the text: the checker
            // still sees what they wrote, and nothing is clickable.
            Assert.Equal("link", HtmlSanitiser.Clean("<a href=\"" + href + "\">link</a>"));
        }

        [Fact]
        public void EscapesTextThatLooksLikeMarkup()
        {
            Assert.Equal(
                "<p>1 &lt; 2 &amp;&amp; 3 &gt; 2</p>",
                HtmlSanitiser.Clean("<p>1 &lt; 2 &amp;&amp; 3 &gt; 2</p>"));
        }

        [Fact]
        public void DoesNotDoubleEscapeAnEntityItAlreadyHolds()
        {
            Assert.Equal("<p>Jones &amp; Co</p>", HtmlSanitiser.Clean("<p>Jones &amp; Co</p>"));
        }

        [Fact]
        public void EscapesABareAmpersandThatIsNotAnEntity()
        {
            Assert.Equal("<p>Jones &amp; Co</p>", HtmlSanitiser.Clean("<p>Jones & Co</p>"));
        }

        [Fact]
        public void ClosesWhatTheEditorLeftOpen()
        {
            // contenteditable emits unbalanced markup routinely. The rebuild closes its own
            // tags, so what is stored is always well formed however it arrived.
            Assert.Equal("<p><strong>bold</strong></p>", HtmlSanitiser.Clean("<p><strong>bold"));
        }

        [Fact]
        public void IgnoresAStrayClosingTag()
        {
            Assert.Equal("<p>x</p>", HtmlSanitiser.Clean("</em><p>x</p></div>"));
        }

        [Fact]
        public void KeepsLineBreaks()
        {
            Assert.Equal("a<br />b", HtmlSanitiser.Clean("a<br>b"));
        }

        [Fact]
        public void NormalisesBoldAndItalicToTheirSemanticTags()
        {
            Assert.Equal(
                "<strong>a</strong><em>b</em>",
                HtmlSanitiser.Clean("<b>a</b><i>b</i>"));
        }

        [Fact]
        public void SurvivesAnUnterminatedTag()
        {
            Assert.Equal("<p>x</p>", HtmlSanitiser.Clean("<p>x</p><script"));
        }

        [Fact]
        public void DropsAComment()
        {
            // A comment can hide a conditional block that some browsers still execute.
            Assert.Equal("<p>x</p>", HtmlSanitiser.Clean("<!--[if IE]><script>alert(1)</script><![endif]--><p>x</p>"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TreatsNothingAsNothing(string html)
        {
            Assert.Null(HtmlSanitiser.Clean(html));
        }

        [Fact]
        public void TreatsMarkupWithNoWordsAsNothing()
        {
            // An editor that has been focused and emptied leaves this behind. Storing it
            // would make an unanswered question look answered.
            Assert.Null(HtmlSanitiser.Clean("<p><br></p>"));
        }

        [Fact]
        public void ReportsWhetherAnAnswerCarriesAnyWords()
        {
            Assert.False(HtmlSanitiser.HasText("<p>  </p><br />"));
            Assert.True(HtmlSanitiser.HasText("<p>a</p>"));
            Assert.False(HtmlSanitiser.HasText(null));
        }

        [Fact]
        public void CapsWhatItWillStore()
        {
            var long_ = new string('a', HtmlSanitiser.MaxLength + 5000);

            var cleaned = HtmlSanitiser.Clean("<p>" + long_ + "</p>");

            Assert.True(cleaned.Length <= HtmlSanitiser.MaxLength);
        }
    }
}
