namespace KamiToolKit.Nodes;

/// <summary>
///     Specialization of <see cref="DropDownNode{T}" /> for enums.
/// </summary>
public class EnumDropDownNode<T> : DropDownNode<T> where T : Enum
{
    /// <inheritdoc />
    public EnumDropDownNode()
        => GetLabelFunction = entry => entry.Description;
}
