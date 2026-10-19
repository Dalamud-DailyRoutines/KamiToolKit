using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes.ComponentNode;
using KamiToolKit.Classes;
using KamiToolKit.Nodes.Simplified;
using KamiToolKit.Timelines;

namespace KamiToolKit.Nodes;

/// <summary>
///     Implementation of the games NumericInputNode and associated component.
/// </summary>
public unsafe class NumericInputNode : ComponentNode<AtkComponentNumericInput, AtkUldComponentDataNumericInput>
{
    private AllowedEntities previousInputSanitizationFlags;
    private bool            isInputSanitizationFlagsOverridden;
    private string?         pendingInputText;
    private int             pendingPreviousValue;
    private ByteColor       previousFocusColor;
    private ByteColor       previousDefaultColor;

    /// <summary>
    ///     Constructs a new <see cref="NumericInputNode" />.
    /// </summary>
    public NumericInputNode()
    {
        SetInternalComponentType(ComponentType.NumericInput);

        BackgroundNode = new SimpleNineGridNode
        {
            NodeId             = 8,
            Position           = new Vector2(0.0f, 3.0f),
            TexturePath        = "ui/uld/NumericStepperB.tex",
            TextureCoordinates = new Vector2(56.0f, 0.0f),
            TextureSize        = new Vector2(24.0f, 24.0f),
            Height             = 24.0f,
            Offsets            = new Vector4(10.0f)
        };
        BackgroundNode.AttachNode(this);

        AddButton = new TextureButtonNode
        {
            NodeId             = 7,
            TexturePath        = "ui/uld/NumericStepperB.tex",
            TextureCoordinates = new Vector2(28.0f, 0.0f),
            TextureSize        = new Vector2(28.0f, 28.0f),
            Size               = new Vector2(28.0f, 28.0f)
        };
        AddButton.AttachNode(this);

        SubtractButton = new TextureButtonNode
        {
            NodeId             = 6,
            TexturePath        = "ui/uld/NumericStepperB.tex",
            TextureCoordinates = new Vector2(0.0f,  0.0f),
            TextureSize        = new Vector2(28.0f, 28.0f),
            Size               = new Vector2(28.0f, 28.0f)
        };
        SubtractButton.AttachNode(this);

        ValueTextNode = new TextNode
        {
            NodeId        = 5,
            Position      = new Vector2(6.0f, 6.0f),
            FontType      = FontType.Axis,
            TextColorID   = 1,
            FontSize      = 12,
            AlignmentType = AlignmentType.Top,
            String        = "999"
        };
        ValueTextNode.AttachNode(this);

        FocusBorderNode = new SimpleNineGridNode
        {
            NodeId             = 4,
            TexturePath        = "ui/uld/TextInputA.tex",
            TextureCoordinates = new Vector2(0.0f,  0.0f),
            TextureSize        = new Vector2(24.0f, 24.0f),
            Position           = new Vector2(-3.0f, -2.0f),
            Offsets            = new Vector4(10.0f),
            IsVisible          = false
        };
        FocusBorderNode.AttachNode(this);

        CursorNode = new CursorNode
        {
            NodeId  = 2,
            Size    = new Vector2(4.0f, 24.0f),
            OriginY = 4.0f
        };

        CursorNode.AttachNode(this);

        BuildTimelines();

        Data->Nodes[0] = ValueTextNode.NodeId;
        Data->Nodes[1] = 0;
        Data->Nodes[2] = CursorNode.NodeId;
        Data->Nodes[3] = AddButton.NodeId;
        Data->Nodes[4] = SubtractButton.NodeId;

        Data->Max        = int.MaxValue;
        Data->FocusColor = ColorHelper.GetColor(67).ToByteColor();

        InitializeComponentEvents();
        previousFocusColor   = Component->FocusColor;
        previousDefaultColor = Component->DefaultTextColor;

        AddEvent(AtkEventType.ValueUpdate, ValueUpdateHandler);
    }

    /// <summary>
    ///     Not intended for public use, but it's here if you absolutely need it.
    /// </summary>
    public ButtonBase AddButton { get; }

    /// <summary>
    ///     Not intended for public use, but it's here if you absolutely need it.
    /// </summary>
    public NineGridNode BackgroundNode { get; }

    /// <summary>
    ///     Not intended for public use, but it's here if you absolutely need it.
    /// </summary>
    public CursorNode CursorNode { get; }

    /// <summary>
    ///     Not intended for public use, but it's here if you absolutely need it.
    /// </summary>
    public NineGridNode FocusBorderNode { get; }

    /// <summary>
    ///     Not intended for public use, but it's here if you absolutely need it.
    /// </summary>
    public ButtonBase SubtractButton { get; }

    /// <summary>
    ///     Not intended for public use, but it's here if you absolutely need it.
    /// </summary>
    public TextNode ValueTextNode { get; }

    /// <summary>
    ///     Gets or sets the current value of the input.
    /// </summary>
    /// <remarks>
    ///     Setting the value this way will still trigger <see cref="OnValueUpdate" />, but will not trigger any sound effects.
    /// </remarks>
    public int Value
    {
        get => Component->Value;
        set => Component->InnerSetValue(value, true, false);
    }

    /// <summary>
    ///     Gets or sets the minimum value allowed.
    /// </summary>
    public int Min
    {
        get => Component->Data.Min;
        set => Component->Data.Min = value;
    }

    /// <summary>
    ///     Gets or sets the maximum value allowed.
    /// </summary>
    public int Max
    {
        get => Component->Data.Max;
        set => Component->Data.Max = value;
    }

    /// <summary>
    ///     Gets or sets the range of values allowed.
    /// </summary>
    public Range Range
    {
        get => new(Min, Max);
        set
        {
            Min = value.Start.Value;
            Max = value.End.Value;
        }
    }

    /// <summary>
    ///     Gets or sets how much the value will be increased for each scroll or button press.
    /// </summary>
    public int Step
    {
        get => Component->Data.Add;
        set => Component->Data.Add = value;
    }

    /// <summary>
    ///     Gets or sets the action that is invoked when the value changes. Contains the new value.
    /// </summary>
    public Action<int>? OnValueUpdate { get; set; }

    /// <inheritdoc />
    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();

        ValueTextNode.Size   = new Vector2(Width - 58.0f, Height / 2.0f);
        FocusBorderNode.Size = new Vector2(Width - 40.0f, Height + 4.0f);

        BackgroundNode.Width = Width - 46.0f;
        AddButton.X          = Width - 50.0f;
        SubtractButton.X     = Width - 28.0f;
    }

    /// <inheritdoc />
    protected override void OnColorThemeChanged()
    {
        base.OnColorThemeChanged();

        if (ValueTextNode.TextColorID is { } colorID)
            ColorHelper.RefreshColor(ref Component->DefaultTextColor, colorID, ref previousDefaultColor);

        if (Component->IsActive)
            ColorHelper.RefreshColor(ref ValueTextNode.Node->TextColor, 67, ref previousFocusColor);

        var focusColor = ColorHelper.GetColor(67).ToByteColor();
        previousFocusColor                  = focusColor;
        Component->FocusColor               = focusColor;
        Component->InputDataBase.FocusColor = focusColor;
        Component->Data.FocusColor          = focusColor;
        Data->FocusColor                    = focusColor;
    }

    /// <inheritdoc />
    protected override void OnReceiveEvent
    (
        AtkComponentBase* thisPtr,
        AtkEventType      eventType,
        int               eventParam,
        AtkEvent*         atkEvent,
        AtkEventData*     atkEventData
    )
    {
        switch (eventType)
        {
            case AtkEventType.FocusStart:
            {
                var textInput = AtkStage.Instance()->AtkInputManager->TextInput;

                if (!isInputSanitizationFlagsOverridden)
                {
                    previousInputSanitizationFlags     = textInput->InputSanitizationFlags;
                    isInputSanitizationFlagsOverridden = true;
                }

                base.OnReceiveEvent(thisPtr, eventType, eventParam, atkEvent, atkEventData);

                textInput->InputSanitizationFlags |= AllowedEntities.Numbers | AllowedEntities.SpecialCharacters;

                return;
            }
            case AtkEventType.FocusStop:
            {
                pendingInputText     = ValueTextNode.Node->NodeText.ToString();
                pendingPreviousValue = Component->Value;

                base.OnReceiveEvent(thisPtr, eventType, eventParam, atkEvent, atkEventData);

                if (isInputSanitizationFlagsOverridden)
                {
                    AtkStage.Instance()->AtkInputManager->TextInput->InputSanitizationFlags = previousInputSanitizationFlags;
                    isInputSanitizationFlagsOverridden                                      = false;
                }

                ApplyCommittedValue();

                return;
            }
            case AtkEventType.InputReceived:
                pendingInputText     = ValueTextNode.Node->NodeText.ToString();
                pendingPreviousValue = Component->Value;
                break;
        }

        base.OnReceiveEvent(thisPtr, eventType, eventParam, atkEvent, atkEventData);

        ApplyCommittedValue();
    }

    private void ApplyCommittedValue()
    {
        if (pendingInputText is not { } inputText)
            return;

        pendingInputText = null;

        if (GetCommittedValue(inputText) is { } value && value != Component->Value)
            Component->InnerSetValue(value, true, false);
    }

    private void ValueUpdateHandler()
    {
        if (pendingInputText is { } inputText)
        {
            pendingInputText = null;

            if (GetCommittedValue(inputText) is { } value)
            {
                if (value == pendingPreviousValue)
                {
                    Component->Value = value;

                    return;
                }

                if (value != Component->Value)
                {
                    Component->InnerSetValue(value, true, false);

                    return;
                }
            }
        }

        OnValueUpdate?.Invoke(Value);
    }

    private int? GetCommittedValue
    (
        string text
    )
    {
        if (ClampValue(ParseDigitValue(text)) != Component->Value)
            return null;

        return ClampValue(ParseSignedValue(text));
    }

    private int ClampValue
    (
        int value
    )
    {
        if (value < Min)
            value = Min;

        return value > Max ?
                   Max :
                   value;
    }

    private static int ParseDigitValue
    (
        string text
    )
    {
        var value = 0;

        foreach (var character in text)
            if (character is >= '0' and <= '9')
                value = unchecked((value * 10) + (character - '0'));

        return value;
    }

    private static int ParseSignedValue
    (
        string text
    )
    {
        var value    = 0L;
        var negative = false;

        foreach (var character in text)
        {
            switch (character)
            {
                case '-':
                    negative = true;

                    break;
                case >= '0' and <= '9':
                    value = Math.Min((value * 10) + (character - '0'), int.MaxValue);

                    break;
            }
        }

        return (int)(negative ?
                         -value :
                         value);
    }

    private void BuildTimelines()
    {
        AddTimeline
        (
            new TimelineBuilder()
                .BeginFrameSet(1, 29)
                .AddLabel(1,  17, AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(9,  0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(10, 18, AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(19, 0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(20, 7,  AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(29, 0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .EndFrameSet()
                .Build()
        );

        BackgroundNode.AddTimeline
        (
            new TimelineBuilder()
                .BeginFrameSet(1, 9)
                .AddFrame(1, addColor: new Vector3(0, 0, 0), multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(10, 19)
                .AddFrame(10, addColor: new Vector3(0,  0,  0),  multiplyColor: new Vector3(100, 100, 100))
                .AddFrame(12, addColor: new Vector3(20, 20, 20), multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(20, 29)
                .AddFrame(20, addColor: new Vector3(0, 0, 0), multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .Build()
        );

        ValueTextNode.AddTimeline
        (
            new TimelineBuilder()
                .BeginFrameSet(1, 19)
                .AddFrame(1, alpha: 255)
                .EndFrameSet()
                .BeginFrameSet(20, 29)
                .AddFrame(20, alpha: 127)
                .EndFrameSet()
                .Build()
        );

        FocusBorderNode.AddTimeline
        (
            new TimelineBuilder()
                .BeginFrameSet(10, 19)
                .AddFrame(10, alpha: 0)
                .AddFrame(12, alpha: 255)
                .EndFrameSet()
                .Build()
        );

        CursorNode.AddTimeline
        (
            new TimelineBuilder()
                .BeginFrameSet(1, 15)
                .AddLabel(1,  101, AtkTimelineJumpBehavior.Start,       0)
                .AddLabel(15, 0,   AtkTimelineJumpBehavior.LoopForever, 101)
                .EndFrameSet()
                .BeginFrameSet(1, 19)
                .AddEmptyFrame(1)
                .EndFrameSet()
                .Build()
        );
    }
}
