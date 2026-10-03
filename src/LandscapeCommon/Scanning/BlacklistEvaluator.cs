namespace LandscapeCommon.Scanning;

/// <summary>
/// Applies the two exclusion rules shared by every landscape tool: mesh path wildcards and
/// EditorID substring keywords. Either match is enough to skip a candidate record entirely.
/// </summary>
public sealed class BlacklistEvaluator
{
    private readonly IReadOnlyList<string> _meshPatterns;
    private readonly IReadOnlyList<string> _editorIdKeywords;
    private readonly bool _leadingWildcardMatchesRoot;

    /// <param name="leadingWildcardMatchesRoot">See <see cref="WildcardMatcher.MatchesAny"/>.</param>
    public BlacklistEvaluator(IReadOnlyList<string> meshPatterns, IReadOnlyList<string> editorIdKeywords, bool leadingWildcardMatchesRoot = true)
    {
        _meshPatterns = meshPatterns;
        _editorIdKeywords = editorIdKeywords;
        _leadingWildcardMatchesRoot = leadingWildcardMatchesRoot;
    }

    public bool IsMeshBlacklisted(string meshPath) =>
        WildcardMatcher.MatchesAny(meshPath, _meshPatterns, _leadingWildcardMatchesRoot);

    public bool IsEditorIdBlacklisted(string editorId)
    {
        foreach (var keyword in _editorIdKeywords)
        {
            if (editorId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
