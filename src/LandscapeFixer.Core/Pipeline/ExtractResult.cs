namespace LandscapeFixer.Core.Pipeline;

public sealed record ExtractResult(
    int RecordsMatched,
    int MeshesDuplicated,
    int MeshesFailed,
    int MalformedRecordsSkipped,
    int ShaderFlagsPatched,
    int VertexColorsNeutralized,
    int CollisionMaterialsRemapped,
    int DecalShapesHidden,
    int DecalCompanionShapesRetextured,
    int IceSnowMaterialsRemoved,
    int VertexColorMeshesPatchedInPlace,
    int LandscapesPatched,
    int BlendMeshesPatched,
    int BlendTextureSetsCreated,
    int BlendAlternateTexturesAssigned,
    IReadOnlyList<string> Diagnostics,
    string? OutputEspPath);
