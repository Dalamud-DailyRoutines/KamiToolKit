using System.ComponentModel;

namespace KamiToolKit.Enums;

/// <summary>
///     Anchor definitions for <see cref="Nodes.HorizontalListNode" />
/// </summary>
public enum HorizontalListAnchor
{
    /// <summary>
    ///     Anchors the left edge of the contents to the node position.
    /// </summary>
    [Description("Left")]
    Left,

    /// <summary>
    ///     将内容的水平中心对齐至节点矩形范围的水平中心。
    /// </summary>
    [Description("Center")]
    Center,

    /// <summary>
    ///     Anchors the right edge of the contents to the node position.
    /// </summary>
    [Description("Right")]
    Right
}
