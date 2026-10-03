using nifly;

namespace LandscapeCommon.Nif;

/// <summary>
/// Shared by both Snow Fixer (its snow-duplicated meshes) and AutoBlend (its blend-duplicated/
/// in-place-patched meshes): every shape that carries a NiAlphaProperty (alpha test OR blend,
/// whatever the shape's own mode already is) is missing ZBuffer_Write/No_Fade on a real,
/// surprisingly large share of source meshes - both vanilla and third-party. Without them, an
/// alpha-blended shape renders with visible artifacts and unwanted distance-based dithering.
/// Deliberately gated on HasAlphaProperty(): a mesh can carry other alpha-tested shapes that were
/// never meant to be touched at all (e.g. an unrelated decal AutoBlend's own caller never targeted)
/// - the per-shape check alone is what makes it safe to reuse verbatim across both tools' very
/// different scoping (Snow Fixer runs this over every shape in its own wholesale mesh duplicate;
/// AutoBlend runs it only over the specific shape names its own caller already decided are in
/// scope).
/// </summary>
public static class AlphaShaderFlagFixup
{
    // Both confirmed against niftools/nifxml's SkyrimShaderPropertyFlags2 enum.
    private const uint ZBufferWriteBit = 0x1; // value 1, bit 0
    private const uint NoFadeBit = 0x8; // value 8, bit 3 - disables distance-based dithered fading

    /// <summary>Sets ZBuffer_Write and No_Fade on <paramref name="shape"/>'s own shader flags2 if
    /// (and only if) it carries a NiAlphaProperty; a shape with no alpha property at all - or no
    /// shader property to hold the flags on - is left untouched. Returns whether anything
    /// changed.</summary>
    public static bool ApplyToShape(NifFile nifFile, NiShape shape)
    {
        if (!shape.HasAlphaProperty() || !shape.HasShaderProperty())
        {
            return false;
        }

        var shaderBlockIndex = shape.ShaderPropertyRef().index;
        if (nifFile.GetHeader().GetBlockById(shaderBlockIndex) is not BSShaderProperty shaderProperty)
        {
            return false;
        }

        var flags2 = shaderProperty.shaderFlags2;
        var changed = false;

        if ((flags2 & ZBufferWriteBit) == 0)
        {
            flags2 |= ZBufferWriteBit;
            changed = true;
        }

        if ((flags2 & NoFadeBit) == 0)
        {
            flags2 |= NoFadeBit;
            changed = true;
        }

        if (changed)
        {
            shaderProperty.shaderFlags2 = flags2;
        }

        return changed;
    }

    /// <summary>Applies <see cref="ApplyToShape"/> to every shape in the mesh - for a caller (Snow
    /// Fixer) whose whole duplicated mesh is fair game, unlike AutoBlend's own name-filtered
    /// scoping which calls <see cref="ApplyToShape"/> directly per shape instead.</summary>
    public static bool ApplyToAllShapes(NifFile nifFile)
    {
        var any = false;
        foreach (var shape in nifFile.GetShapes())
        {
            if (ApplyToShape(nifFile, shape))
            {
                any = true;
            }
        }

        return any;
    }
}
