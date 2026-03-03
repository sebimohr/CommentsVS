using System.Collections.Generic;

namespace CommentsVS.Services
{
    /// <summary>
    /// Helper methods for detecting comment spans in source code text.
    /// </summary>
    internal static class CommentSpanHelper
    {
        /// <summary>
        /// Finds all comment spans in the given text (both full-line and inline comments).
        /// </summary>
        /// <param name="text">The line text to analyze.</param>
        /// <returns>Enumerable of (Start, Length) tuples representing comment portions.</returns>
        public static IEnumerable<(int Start, int Length)> FindCommentSpans(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield break;
            }

            // Check if entire line is a comment (starts with comment prefix)
            if (LanguageCommentStyle.IsCommentLine(text))
            {
                yield return (0, text.Length);
                yield break;
            }

            var starts = new List<(int Start, string Token)>();

            AddCandidate(starts, FindInlineSlashSlashCommentStart(text), "//");
            AddCandidate(starts, FindInlineSqlCommentStart(text), "--");
            AddCandidate(starts, FindInlineBlockCommentStart(text, "/*"), "/*");
            AddCandidate(starts, FindInlineBlockCommentStart(text, "<!--"), "<!--");
            AddCandidate(starts, FindInlineHashCommentStart(text), "#");

            if (starts.Count == 0)
            {
                yield break;
            }

            starts.Sort((x, y) => x.Start.CompareTo(y.Start));
            (int Start, string Token) first = starts[0];

            // Single-line comments continue to end of line
            if (first.Token is "//" or "--" or "#")
            {
                yield return (first.Start, text.Length - first.Start);
                yield break;
            }

            // Block comments end at closing token if present, otherwise to end of line
            var closeToken = first.Token == "/*" ? "*/" : "-->";
            var closeIndex = text.IndexOf(closeToken, first.Start + first.Token.Length, StringComparison.Ordinal);
            if (closeIndex >= 0)
            {
                var end = closeIndex + closeToken.Length;
                yield return (first.Start, end - first.Start);
                yield break;
            }

            yield return (first.Start, text.Length - first.Start);
        }

        private static void AddCandidate(List<(int Start, string Token)> candidates, int index, string token)
        {
            if (index >= 0)
            {
                candidates.Add((index, token));
            }
        }

        private static int FindInlineSlashSlashCommentStart(string text)
        {
            var searchIndex = 0;
            while (searchIndex < text.Length)
            {
                var index = text.IndexOf("//", searchIndex, StringComparison.Ordinal);
                if (index < 0)
                {
                    return -1;
                }

                if (index > 0 && text[index - 1] == ':')
                {
                    searchIndex = index + 2;
                    continue;
                }

                if (!IsInsideStringLiteral(text, index))
                {
                    return index;
                }

                searchIndex = index + 2;
            }

            return -1;
        }

        private static int FindInlineSqlCommentStart(string text)
        {
            var searchIndex = 0;
            while (searchIndex < text.Length)
            {
                var index = text.IndexOf("--", searchIndex, StringComparison.Ordinal);
                if (index < 0)
                {
                    return -1;
                }

                var validPrefix = index == 0 || char.IsWhiteSpace(text[index - 1]) || text[index - 1] == ';';
                if (!validPrefix || IsInsideStringLiteral(text, index))
                {
                    searchIndex = index + 2;
                    continue;
                }

                return index;
            }

            return -1;
        }

        private static int FindInlineBlockCommentStart(string text, string token)
        {
            var searchIndex = 0;
            while (searchIndex < text.Length)
            {
                var index = text.IndexOf(token, searchIndex, StringComparison.Ordinal);
                if (index < 0)
                {
                    return -1;
                }

                if (!IsInsideStringLiteral(text, index))
                {
                    return index;
                }

                searchIndex = index + token.Length;
            }

            return -1;
        }

        private static int FindInlineHashCommentStart(string text)
        {
            var searchIndex = 0;
            while (searchIndex < text.Length)
            {
                var index = text.IndexOf('#', searchIndex);
                if (index < 0)
                {
                    return -1;
                }

                // Require whitespace or punctuation before # to avoid matching identifiers/fragments.
                var validPrefix = index == 0 || char.IsWhiteSpace(text[index - 1]) || text[index - 1] is ';' or ',' or ')';
                if (!validPrefix || IsInsideStringLiteral(text, index))
                {
                    searchIndex = index + 1;
                    continue;
                }

                // Skip common preprocessor directives when # begins the token
                if (index == 0 || (index > 0 && char.IsWhiteSpace(text[index - 1])))
                {
                    var tail = text.Substring(index + 1).TrimStart();
                    if (IsPreprocessorDirective(tail))
                    {
                        searchIndex = index + 1;
                        continue;
                    }
                }

                return index;
            }

            return -1;
        }

        private static bool IsPreprocessorDirective(string textAfterHash)
        {
            if (string.IsNullOrEmpty(textAfterHash))
            {
                return false;
            }

            return textAfterHash.StartsWith("if", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("elif", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("else", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("endif", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("region", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("endregion", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("define", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("undef", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("line", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("error", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("warning", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("pragma", StringComparison.OrdinalIgnoreCase)
                   || textAfterHash.StartsWith("nullable", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a position is inside a string literal.
        /// Uses a heuristic based on quote counting.
        /// </summary>
        /// <param name="text">The text to analyze.</param>
        /// <param name="position">The position to check.</param>
        /// <returns>True if the position is inside a string literal.</returns>
        public static bool IsInsideStringLiteral(string text, int position)
        {
            var quoteCount = 0;
            var inVerbatim = false;

            for (var i = 0; i < position; i++)
            {
                if (text[i] == '@' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    inVerbatim = true;
                    quoteCount++;
                    i++; // Skip the quote
                    continue;
                }

                if (text[i] == '"')
                {
                    // Check if it's escaped (not in verbatim)
                    if (!inVerbatim && i > 0 && text[i - 1] == '\\')
                    {
                        // Count consecutive backslashes
                        var backslashCount = 0;
                        for (var j = i - 1; j >= 0 && text[j] == '\\'; j--)
                        {
                            backslashCount++;
                        }

                        // If odd number of backslashes, the quote is escaped
                        if (backslashCount % 2 == 1)
                        {
                            continue;
                        }
                    }

                    quoteCount++;

                    // If we were in verbatim and hit a quote, check for double-quote escape
                    if (inVerbatim && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        i++; // Skip the second quote in ""
                        continue;
                    }

                    if (quoteCount % 2 == 0)
                    {
                        inVerbatim = false;
                    }
                }
            }

            // Odd quote count means we're inside a string
            return quoteCount % 2 == 1;
        }
    }
}
