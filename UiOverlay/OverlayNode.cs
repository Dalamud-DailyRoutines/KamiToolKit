using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;

namespace KamiToolKit.UiOverlay;

/// <summary>
///     Node used in <see cref="OverlayController" /> to display ui elements in the HUD.
/// </summary>
public abstract unsafe class OverlayNode : ResNode
{
    private float globalScale = 1.0f;

    /// <summary>
    ///     The UI Layer that should show this node.
    /// </summary>
    public abstract OverlayLayer OverlayLayer { get; }

    /// <summary>
    ///     When true, this node will automatically hide when the game hides things like nameplates
    /// </summary>
    public virtual bool HideWithNativeUi => true;

    /// <summary>
    ///     When true, this node automatically hides when the Toggle UI Display Mode hotkey is used
    /// </summary>
    public virtual bool HideWithUiToggled => true;

    /// <summary>
    ///     获取是否忽略游戏的全局界面缩放，默认忽略。派生类重写为 <see langword="false" /> 后，
    ///     节点及其子节点的显示倍率会乘以全局界面倍率，节点位置仍使用屏幕像素坐标。
    /// </summary>
    public virtual bool IgnoreGlobalScale => true;

    /// <summary>
    ///     获取或设置节点在水平方向的局部缩放倍率，不包含自动应用的全局界面倍率。
    /// </summary>
    public override float ScaleX
    {
        get => base.ScaleX / globalScale;
        set => base.ScaleX = value * globalScale;
    }

    /// <summary>
    ///     获取或设置节点在垂直方向的局部缩放倍率，不包含自动应用的全局界面倍率。
    /// </summary>
    public override float ScaleY
    {
        get => base.ScaleY / globalScale;
        set => base.ScaleY = value * globalScale;
    }

    /// <summary>
    ///     获取或设置节点的局部缩放倍率，不包含自动应用的全局界面倍率。
    ///     启用全局缩放时，实际显示倍率为此值与全局界面倍率的乘积。
    /// </summary>
    public override Vector2 Scale
    {
        get => base.Scale / globalScale;
        set => base.Scale = value * globalScale;
    }

    /// <summary>
    ///     Gets or sets whether this node should be visible.
    /// </summary>
    public override bool IsVisible { get; set; } = true;

    /// <summary>
    ///     Updates the nodes automatic visibility functions and then triggers <see cref="OnUpdate" />
    /// </summary>
    public void Update()
    {
        OnUpdate();

        var showWithNativeUi  = !(HideWithNativeUi  && !IsNameplateVisible());
        var showWithUiToggled = !(HideWithUiToggled && IsUiHidden());

        base.IsVisible = IsVisible && showWithNativeUi && showWithUiToggled;
    }

    /// <summary>
    ///     Overridable update function that is called each frame.
    /// </summary>
    protected abstract void OnUpdate();

    internal void UpdateGlobalScale()
    {
        var newGlobalScale = IgnoreGlobalScale ?
                                 1.0f :
                                 AtkUnitBase.GetGlobalUIScale();
        if (newGlobalScale.Equals(globalScale))
            return;

        var scale = base.Scale / globalScale * newGlobalScale;
        globalScale = newGlobalScale;
        ResNode->SetScale(scale.X, scale.Y);
    }

    private static bool IsNameplateVisible()
    {
        var nameplateAddon = RaptureAtkUnitManager.Instance()->GetAddonByName("NamePlate");
        return nameplateAddon is not null && nameplateAddon->IsVisible;
    }

    private static bool IsUiHidden()
        => RaptureAtkUnitManager.Instance()->Flags.HasFlag(AtkUnitManagerFlags.UiHidden);
}
