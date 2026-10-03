namespace LandscapeCommon.Configuration;

/// <summary>
/// A curated list of vanilla/base-game landscape texture identities known to be safe to
/// auto-generate a "blend" (alpha-stripped) sibling for - see MissingTextureGenerator/
/// LandscapeFolderDetector. Shared between AutoBlend and Snow Fixer so both tools own "generate
/// missing statics/blend textures" pass covers exactly the same identities without drifting apart.
/// </summary>
public static class AutoGenerateAllowlistDefaults
{
    public static IReadOnlyList<string> Values { get; } = new[]
    {
        @"textures\_resourcepack\landscape\desertrocks01_d.dds",
        @"textures\_resourcepack\landscape\sand01_d.dds",
        @"textures\_resourcepack\landscape\sand02_d.dds",
        @"textures\_resourcepack\landscape\volcaniccracked01_d.dds",
        @"textures\_resourcepack\landscape\volcanicrocks01_d.dds",
        @"textures\bscyrodiil\landscape\colovianrocks01revised.dds",
        @"textures\bscyrodiil\landscape\dirt01.dds",
        @"textures\bscyrodiil\landscape\oakforestgrass01.dds",
        @"textures\bscyrodiil\landscape\oakforestleaves01.dds",
        @"textures\bstamriel\landscape\westweald\rockswestweald.dds",
        @"textures\bstamriel\landscape\westweald\wwlemoss01.dds",
        @"textures\dlc02\landscape\volcanic_ash_01.dds",
        @"textures\dlc02\landscape\volcanic_ash_rocks_01_d.dds",
        @"textures\dlc02\landscape\volcanic_ash_tundra_02.dds",
        @"textures\dlc02\landscape\volcanic_ash_tundra_04.dds",
        @"textures\landscape\coastbeach01.dds",
        @"textures\landscape\dirt02.dds",
        @"textures\landscape\fallforestdirt01.dds",
        @"textures\landscape\fallforestgrass01.dds",
        @"textures\landscape\fallforestleaves01.dds",
        @"textures\landscape\fallforestrocks01.dds",
        @"textures\landscape\fieldgrass01.dds",
        @"textures\landscape\fieldgrass02.dds",
        @"textures\landscape\frozenmarshice01.dds",
        @"textures\landscape\mineralpoolterrace.dds",
        @"textures\landscape\reachmossyrocks01.dds",
        @"textures\landscape\riverbededge.dds",
        @"textures\landscape\rocks01.dds",
        @"textures\landscape\snow01.dds",
        @"textures\landscape\snowrocks01.dds",
        @"textures\landscape\tundra01.dds",
        @"textures\landscape\tundrarocks01.dds",
        @"textures\landscape\volcanictundradirt01.dds",
        @"textures\landscape\volcanictundragravel01.dds",
        @"textures\landscape\volcanictundraminerals01.dds",
        @"textures\landscape\volcanictundrarocks01.dds",
    };
}
