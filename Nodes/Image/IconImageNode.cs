using System;
using System.Numerics;
using Dalamud.Interface.Textures.TextureWraps;
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
            
            var startLoadTime = Environment.TickCount64;
            IDalamudTextureWrap? texture = null;
            while (texture == null && Environment.TickCount64 - startLoadTime <= 10_000) {
                texture = DalamudInterface.Instance.TextureProvider.GetFromGameIcon(new(field)).GetWrapOrDefault();
            }
            if (texture == null) return;
            
            LoadTexture(texture);
        }
    }

    public unsafe uint? LoadedIconId
        => IconId;
}
