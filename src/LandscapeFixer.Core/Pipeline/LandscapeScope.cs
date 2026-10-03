using LandscapeFixer.Core.Configuration;

namespace LandscapeFixer.Core.Pipeline;

/// <summary>
/// Decides whether a mesh belongs to what this tool handles: a mesh living in one of the game's
/// landscape folders (any folder segment named "landscape" - meshes\landscape\, meshes\dlc01\landscape\,
/// meshes\_resourcepack\landscape\, ...) or matching the user's manual add list. Paths are relative
/// to the "meshes" folder, backslash or slash separated.
/// </summary>
public sealed class LandscapeScope
{
    private readonly bool _restrict;
    private readonly IReadOnlyList<string> _addList;

    public LandscapeScope(ExtractSettings settings)
    {
        _restrict = settings.RestrictToLandscapeFolders;
        _addList = settings.MeshAddList;
    }

    public bool IsInScope(string meshRelativePath)
    {
        if (!_restrict)
        {
            return true;
        }

        if (HasLandscapeFolderSegment(meshRelativePath))
        {
            return true;
        }

        return _addList.Count > 0 && WildcardMatcher.MatchesAny(meshRelativePath, _addList);
    }

    internal static bool HasLandscapeFolderSegment(string meshRelativePath)
    {
        var segments = meshRelativePath.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        // The last segment is the file name - only folders count.
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "landscape", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
