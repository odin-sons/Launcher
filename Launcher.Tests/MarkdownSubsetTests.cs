using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class MarkdownSubsetTests
    {
        private static string Describe(IEnumerable<MarkdownSpan> spans) => string.Concat(spans.Select(span =>
        {
            string text = span.Text;
            if (span.Bold) text = "**" + text + "**";
            if (span.Italic) text = "_" + text + "_";
            return span.Link == null ? text : "[" + text + "](" + span.Link.Host + ")";
        }));

        private static string Describe(IEnumerable<MarkdownBlock> blocks) => string.Join("|", blocks.Select(block => block switch
        {
            MarkdownParagraph p => "P:" + Describe(p.Spans),
            MarkdownHeading h => "H" + h.Level + ":" + Describe(h.Spans),
            MarkdownList l => "L:" + string.Join(";", l.Items.Select(Describe)),
            _ => "?"
        }));

        private static string Parsed(string markdown) => Describe(MarkdownSubset.Parse(markdown));

        [Fact]
        public void Paragraphs_AreSeparatedByBlankLines_AndLinesInOneAreJoined()
        {
            Assert.Equal("P:one two|P:three", Parsed("one\ntwo\n\nthree"));
        }

        [Fact]
        public void HeadingsOfLevelTwoAndThree_AreRecognised()
        {
            Assert.Equal("H2:Rules|H3:Fighting|P:text", Parsed("## Rules\n### Fighting\ntext"));
        }

        [Theory]
        [InlineData("# Title", "P:# Title")]
        [InlineData("#### Deep", "P:#### Deep")]
        [InlineData("##NoSpace", "P:##NoSpace")]
        [InlineData("  ## Indented", "P:## Indented")]
        public void OtherHeadings_AreJustText(string markdown, string expected)
        {
            Assert.Equal(expected, Parsed(markdown));
        }

        [Fact]
        public void BulletLists_CollectTheirItems_AndAContinuationJoinsTheItem()
        {
            Assert.Equal("L:Weekly events;Friendly admins and more;Third", Parsed("- Weekly events\n* Friendly admins\n  and more\n+ Third"));
        }

        [Fact]
        public void AListEndsAtAParagraph_AndAParagraphAtAList()
        {
            Assert.Equal("P:before|L:a;b|P:after", Parsed("before\n- a\n- b\nafter"));
        }

        [Fact]
        public void BoldAndItalic_AreStyles_AndMayOverlapLinks()
        {
            Assert.Equal("P:a **b** c _d_ e", Parsed("a **b** c *d* e"));
            Assert.Equal("P:_x_ and _y_", Parsed("_x_ and _y_"));
            Assert.Equal("P:_**both**_", Parsed("***both***"));
        }

        [Theory]
        [InlineData("2 * 3 * 4", "P:2 * 3 * 4")]
        [InlineData("a **unclosed", "P:a **unclosed")]
        [InlineData("snake_case_name", "P:snake_case_name")]
        [InlineData("__init__", "P:__init__")]
        [InlineData("*unclosed", "P:*unclosed")]
        public void MarkersThatDoNotPair_StayAsWritten(string markdown, string expected)
        {
            Assert.Equal(expected, Parsed(markdown));
        }

        [Fact]
        public void AnEscapedMarker_IsLiteral()
        {
            Assert.Equal("P:*not italic* and [not](a link)", Parsed("\\*not italic\\* and \\[not\\](a link)"));
        }

        [Fact]
        public void AnHttpsLink_KeepsItsTarget_AndShowsItsHost()
        {
            var paragraph = Assert.IsType<MarkdownParagraph>(Assert.Single(MarkdownSubset.Parse("see [our site](https://example.org/rules?a=1)")));

            MarkdownSpan link = paragraph.Spans[1];
            Assert.Equal("our site", link.Text);
            Assert.Equal("https://example.org/rules?a=1", link.Link.Target.AbsoluteUri);
            Assert.Equal("example.org", link.Link.Host);
        }

        [Fact]
        public void ALinkWithAPort_ShowsIt()
        {
            Assert.Equal("P:[x](example.org:8443)", Parsed("[x](https://example.org:8443/a)"));
        }

        [Fact]
        public void AnInternationalHost_IsShownInItsPunycodeForm()
        {
            Assert.Equal("P:[x](xn--e1afmkfd.xn--p1ai)", Parsed("[x](https://пример.рф/)"));
        }

        [Theory]
        [InlineData("[x](http://example.org/)")]
        [InlineData("[x](javascript:alert(1))")]
        [InlineData("[x](file:///etc/passwd)")]
        [InlineData("[x](ftp://example.org/)")]
        [InlineData("[x](//example.org/)")]
        [InlineData("[x](/relative)")]
        [InlineData("[x](https://user:pass@example.org/)")]
        [InlineData("[x]()")]
        public void ALinkThatIsNotAnHttpsAddress_IsOnlyItsText(string markdown)
        {
            var paragraph = Assert.IsType<MarkdownParagraph>(Assert.Single(MarkdownSubset.Parse(markdown)));

            Assert.All(paragraph.Spans, span => Assert.Null(span.Link));
        }

        [Fact]
        public void ALinkLongerThanTheLimit_IsOnlyItsText()
        {
            string markdown = "[x](https://example.org/" + new string('a', MarkdownSubset.MaxLinkLength) + ")";

            var paragraph = Assert.IsType<MarkdownParagraph>(Assert.Single(MarkdownSubset.Parse(markdown)));

            Assert.All(paragraph.Spans, span => Assert.Null(span.Link));
        }

        [Fact]
        public void AnImage_IsNeverRendered_ItsSourceStaysText()
        {
            var paragraph = Assert.IsType<MarkdownParagraph>(Assert.Single(MarkdownSubset.Parse("![logo](https://example.org/a.png) after")));

            Assert.Equal("![logo](https://example.org/a.png) after", Describe(paragraph.Spans));
            Assert.All(paragraph.Spans, span => Assert.Null(span.Link));
        }

        [Fact]
        public void RawHtml_AppearsAsText()
        {
            Assert.Equal("P:<b>bold</b> <script>x</script> <a href=\"https://evil.example\">go</a>",
                Parsed("<b>bold</b> <script>x</script> <a href=\"https://evil.example\">go</a>"));
        }

        [Fact]
        public void ControlAndDirectionCharacters_AreRemoved()
        {
            Assert.Equal("P:abc", Parsed("a\u0000b‮c​"));
        }

        [Fact]
        public void AWindowsFile_ParsesLikeAUnixOne()
        {
            Assert.Equal("H2:Title|P:one two|L:a;b", Parsed("## Title\r\n\r\none\r\ntwo\r\n\r\n- a\r\n- b\r\n"));
        }

        [Fact]
        public void NothingOrOnlyBlankLines_GiveNoBlocks()
        {
            Assert.Empty(MarkdownSubset.Parse(null));
            Assert.Empty(MarkdownSubset.Parse(""));
            Assert.Empty(MarkdownSubset.Parse("\n \n\t\n"));
        }

        [Fact]
        public void ALongFile_IsCutAtTheBlockLimit()
        {
            string markdown = string.Join("\n\n", Enumerable.Range(0, MarkdownSubset.MaxBlocks * 2).Select(i => "paragraph " + i));

            Assert.Equal(MarkdownSubset.MaxBlocks, MarkdownSubset.Parse(markdown).Count);
        }

        [Fact]
        public void PathologicalInput_FinishesQuickly()
        {
            string markdown = new string('*', 20000) + "\n" + string.Concat(Enumerable.Repeat("[a](", 5000)) + "\n" + new string('_', 20000);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            MarkdownSubset.Parse(markdown);

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
        }
    }
}
