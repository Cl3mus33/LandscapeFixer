using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace LandscapeCommon.Scanning;

/// <summary>
/// Matches paths against simple "*" wildcard patterns (e.g. "*\glass\*"), case-insensitive,
/// treating "/" and "\" as equivalent.
/// </summary>
public static class WildcardMatcher
{
    // Every pattern passed through here comes from one of a handful of small, fixed settings lists
    // (mesh blacklist, EditorID keywords) that never change during a run - but MatchesAny is called
    // once per record scanned, which on a real load order is thousands of calls.
    // RegexOptions.Compiled does real IL-generation/JIT work per construction, so building a fresh
    // Regex on every call would mean redoing that work for the exact same pattern string over and
    // over. Caching the compiled Regex per normalized pattern means each unique pattern is only ever
    // compiled once per process; behavior is identical since building the Regex is a pure function
    // of the normalized pattern.
    private static readonly ConcurrentDictionary<(string Pattern, bool RootMatch), Regex> RegexCache = new();

    /// <param name="leadingWildcardMatchesRoot">When true (Snow Fixer's behavior), a leading "*\"
    /// also matches when the next segment is the path's very first component, so "*\dungeons\*"
    /// excludes "dungeons\caves\x.nif". AutoBlend historically did NOT do this (its default
    /// "*\dungeons\*" blacklist therefore never excluded a top-level dungeons folder), and turning
    /// it on there changes which meshes get patched - so it stays an explicit per-caller choice,
    /// pending a deliberate decision for the merged tool.</param>
    public static bool MatchesAny(string path, IEnumerable<string> patterns, bool leadingWildcardMatchesRoot = true)
    {
        var normalizedPath = Normalize(path);
        foreach (var pattern in patterns)
        {
            if (GetOrBuildRegex(pattern, leadingWildcardMatchesRoot).IsMatch(normalizedPath))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string value) => value.Replace('\\', '/').Trim();

    private static Regex GetOrBuildRegex(string pattern, bool rootMatch)
    {
        var key = (Normalize(pattern), rootMatch);
        return RegexCache.GetOrAdd(key, static k =>
        {
            var escaped = Regex.Escape(k.Pattern).Replace(@"\*", ".*");

            // A leading "*/" must also match when the following segment is the very first path
            // component, with nothing (not even a separator) before it - Bethesda's own relative
            // mesh paths never start with a separator, so a rule like "*\architecture\*" needs to
            // match "architecture\whiterun\house01.nif" (top-level folder), not just something like
            // "dungeons\architecture\house01.nif" (nested). Without this, a rule targeting any
            // top-level folder would silently never exclude anything - reported directly against
            // Snow Fixer: files kept generating in a folder the user had explicitly blacklisted.
            // Only the leading wildcard needs this treatment; a trailing "*" already matches
            // zero-or-more with no separator required.
            if (k.RootMatch && escaped.StartsWith(".*/", StringComparison.Ordinal))
            {
                escaped = "(?:.*/)?" + escaped[3..];
            }

            return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });
    }
}
