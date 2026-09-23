using KamiToolKit.Nodes;

namespace KamiToolKit.Enums;

/// <summary>
///     Which gauge texture a <see cref="ProgressBarCraftNode" /> fills itself with.
/// </summary>
public enum CraftProgressBarType
{
    /// <summary>
    ///     ui/uld/Synthesis.tex part 4, the green to yellow gradient used by the progress gauge.
    /// </summary>
    Progress,

    /// <summary>
    ///     ui/uld/Synthesis.tex part 3, the blue to teal gradient used by the quality gauge.
    /// </summary>
    Quality
}
