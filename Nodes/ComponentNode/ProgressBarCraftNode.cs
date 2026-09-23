using System.Numerics;
using KamiToolKit.Enums;
using KamiToolKit.Nodes.Simplified;

namespace KamiToolKit.Nodes;

/// <summary>
///     Specialization of <see cref="ProgressNode" /> representing one of the synthesis windows gauges.
///     Modeled off ui/uld/Synthesis.uld.
/// </summary>
public class ProgressBarCraftNode : ProgressNode
{
    private float progress;

    /// <summary>
    ///     Constructs a new <see cref="ProgressBarCraftNode" />.
    /// </summary>
    public ProgressBarCraftNode()
    {
        BackgroundNode = new SimpleNineGridNode
        {
            TexturePath        = "ui/uld/Synthesis.tex",
            TextureCoordinates = new Vector2(0.0f, 32.0f),
            TextureSize        = new Vector2(64.0f, 16.0f),
            Offsets            = new Vector4(2.0f, 3.0f, 8.0f, 8.0f)
        };
        BackgroundNode.AttachNode(this);

        FillNode = new SimpleNineGridNode
        {
            TexturePath        = "ui/uld/Synthesis.tex",
            TextureCoordinates = new Vector2(0.0f, 48.0f),
            TextureSize        = new Vector2(64.0f, 12.0f),
            Offsets            = new Vector4(2.0f, 3.0f, 8.0f, 8.0f),
            Position           = new Vector2(0.0f, 2.0f),
            Height             = 12.0f
        };
        FillNode.AttachNode(this);

        ApplyType();
        UpdateFill();
    }

    /// <summary>
    ///     Not intended for public use, but it's here if you absolutely need it.
    /// </summary>
    public SimpleNineGridNode BackgroundNode { get; }

    /// <summary>
    ///     Not intended for public use, but it's here if you absolutely need it.
    /// </summary>
    public SimpleNineGridNode FillNode { get; }

    /// <summary>
    ///     Gets or sets which gauge texture this bar fills itself with.
    /// </summary>
    public CraftProgressBarType Type
    {
        get;
        set
        {
            field = value;
            ApplyType();
        }
    }

    /// <inheritdoc />
    public override Vector4 BackgroundColor
    {
        get => BackgroundNode.Color;
        set => BackgroundNode.Color = value;
    }

    /// <inheritdoc />
    public override Vector4 BarColor
    {
        get => FillNode.Color;
        set => FillNode.Color = value;
    }

    /// <inheritdoc />
    public override float Progress
    {
        get => progress;
        set
        {
            progress = Math.Clamp(value, 0.0f, 1.0f);
            UpdateFill();
        }
    }

    /// <inheritdoc />
    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();

        BackgroundNode.Size = Size;

        UpdateFill();
    }

    private void ApplyType()
    {
        FillNode.V = Type switch
        {
            CraftProgressBarType.Quality => 48.0f,
            _                            => 60.0f
        };

        MarkDirty();
    }

    private void UpdateFill()
    {
        FillNode.Width     = Width * progress;
        FillNode.IsVisible = progress > 0.0f;
    }
}
