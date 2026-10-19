namespace KamiToolKit.Nodes;

/// <summary>
///     Specialization of <see cref="DropDownNode{T}" /> for strings.
/// </summary>
public class StringDropDownNode : DropDownNode<string>
{
    /// <inheritdoc />
    public StringDropDownNode()
        => GetLabelFunction = entry => entry;
}
