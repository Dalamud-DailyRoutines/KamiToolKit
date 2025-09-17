using System.Numerics;
using KamiToolKit.Classes;
using KamiToolKit.Extensions;
using KamiToolKit.NodeParts;

namespace KamiToolKit.Nodes;

/// <summary>
///     A simple image node for use with displaying game icons.
/// </summary>
/// <remarks>This node is not intended to be used with multiple <see cref="Part" />'s.</remarks>
public class IconImageNode : ImGuiImageNode {

    public IconImageNode() {
        TextureSize = new Vector2(32.0f, 32.0f);
        FitTexture  = true;
    }
    
    public uint IconId {
        get;
        set {
            field = value;
            LoadTexture(DalamudInterface.Instance.TextureProvider.GetFromGameIcon(new(value)).GetWrapOrEmpty());
        }
    }

    public unsafe uint? LoadedIconId
        => IconId;
}
