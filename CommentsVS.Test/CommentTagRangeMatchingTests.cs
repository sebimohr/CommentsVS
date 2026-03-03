using System.Text.RegularExpressions;

namespace CommentsVS.Test;

/// <summary>
/// Tests range-based anchor tag matching logic used by classifier/overview taggers.
/// Mirrors production behavior to validate offset and span correctness.
/// </summary>
[TestClass]
public sealed class CommentTagRangeMatchingTests
{
    private const string AnchorKeywordsPattern = "TODO|HACK|NOTE|BUG|FIXME|UNDONE|REVIEW|ANCHOR";

    private static readonly Regex AnchorRegex = new(
        @"\b(?<tag>" + AnchorKeywordsPattern + @")\b:?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MetadataRegex = new(
        @"\b(?:" + AnchorKeywordsPattern + @")\b(?<metadata>\s*(?:\([^)]*\)|\[[^\]]*\]))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] AnchorKeywords =
        ["TODO", "HACK", "NOTE", "BUG", "FIXME", "UNDONE", "REVIEW", "ANCHOR"];

    [TestMethod]
    public void RangeMatching_InlineComment_TagOffsetStartsAtTodo()
    {
        const string text = "var x = 1; // TODO: fix this";

        List<(int Start, int Length)> tagSpans = ExtractTagSpans(text);

        Assert.AreEqual(1, tagSpans.Count);
        Assert.AreEqual(text.IndexOf("TODO", StringComparison.Ordinal), tagSpans[0].Start);
        Assert.AreEqual("TODO".Length, tagSpans[0].Length);
    }

    [TestMethod]
    public void RangeMatching_InlineComment_MultipleAnchorsInOneSpan_ReturnsBoth()
    {
        const string text = "int x = 0; // TODO: fix this and HACK: remove temp logic";

        List<(int Start, int Length)> tagSpans = ExtractTagSpans(text);

        Assert.AreEqual(2, tagSpans.Count);
        Assert.AreEqual(text.IndexOf("TODO", StringComparison.Ordinal), tagSpans[0].Start);
        Assert.AreEqual(text.IndexOf("HACK", StringComparison.Ordinal), tagSpans[1].Start);
    }

    [TestMethod]
    public void RangeMatching_Metadata_InlineComment_AlignsToMetadataSpan()
    {
        const string text = "var x = 1; // TODO(@mads)[#123]: fix this";

        List<(int Start, int Length)> metadataSpans = ExtractMetadataSpans(text);

        Assert.AreEqual(1, metadataSpans.Count);
        Assert.AreEqual(text.IndexOf("(@mads)", StringComparison.Ordinal), metadataSpans[0].Start);
        Assert.AreEqual("(@mads)".Length, metadataSpans[0].Length);
    }

    [TestMethod]
    public void RangeMatching_CodeOnlyTodo_NoComment_ReturnsNoTags()
    {
        const string text = "var s = \"TODO: not a comment\";";

        List<(int Start, int Length)> tagSpans = ExtractTagSpans(text);

        Assert.AreEqual(0, tagSpans.Count);
    }

    [TestMethod]
    public void RangeMatching_OverviewEquivalent_InlineOffsetCorrect()
    {
        const string text = "if (x) return; // NOTE: this is important";

        List<(int Start, int Length)> tagSpans = ExtractTagSpans(text);

        Assert.AreEqual(1, tagSpans.Count);
        Assert.AreEqual(text.IndexOf("NOTE", StringComparison.Ordinal), tagSpans[0].Start);
        Assert.AreEqual("NOTE".Length, tagSpans[0].Length);
    }

    private static List<(int Start, int Length)> ExtractTagSpans(string text)
    {
        List<(int Start, int Length)> spans = [];

        foreach ((int Start, int Length) commentSpan in TestHelpers.FindCommentSpans(text))
        {
            if (!ContainsAnyKeywordInRange(text, commentSpan.Start, commentSpan.Length, AnchorKeywords))
            {
                continue;
            }

            var commentEnd = commentSpan.Start + commentSpan.Length;
            Match match = AnchorRegex.Match(text, commentSpan.Start);

            while (match.Success && match.Index < commentEnd)
            {
                Group tagGroup = match.Groups["tag"];
                if (tagGroup.Success)
                {
                    spans.Add((tagGroup.Index, tagGroup.Length));
                }

                match = match.NextMatch();
            }
        }

        return spans;
    }

    private static List<(int Start, int Length)> ExtractMetadataSpans(string text)
    {
        List<(int Start, int Length)> spans = [];

        foreach ((int Start, int Length) commentSpan in TestHelpers.FindCommentSpans(text))
        {
            if (!ContainsAnyKeywordInRange(text, commentSpan.Start, commentSpan.Length, AnchorKeywords))
            {
                continue;
            }

            var commentEnd = commentSpan.Start + commentSpan.Length;
            Match match = AnchorRegex.Match(text, commentSpan.Start);

            while (match.Success && match.Index < commentEnd)
            {
                Group tagGroup = match.Groups["tag"];
                if (tagGroup.Success)
                {
                    Match metaMatch = MetadataRegex.Match(text, tagGroup.Index);
                    if (metaMatch.Success && metaMatch.Index == tagGroup.Index && metaMatch.Index < commentEnd)
                    {
                        Group metadataGroup = metaMatch.Groups["metadata"];
                        if (metadataGroup.Success && metadataGroup.Length > 0)
                        {
                            spans.Add((metadataGroup.Index, metadataGroup.Length));
                        }
                    }
                }

                match = match.NextMatch();
            }
        }

        return spans;
    }

    private static bool ContainsAnyKeywordInRange(string text, int start, int length, IReadOnlyList<string> keywords)
    {
        foreach (string keyword in keywords)
        {
            if (text.IndexOf(keyword, start, length, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
