using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Internal.Classes;
using KamiToolKit.Nodes.Simplified;
using KamiToolKit.Timelines;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using Action = System.Action;

namespace KamiToolKit.Nodes;

/// <summary>
///     Custom implementation of a native DropDownNode.
/// </summary>
/// <typeparam name="T">Data type this dropdown node is representing.</typeparam>
public unsafe class DropDownNode<T> : SimpleComponentNode
{
    private readonly ReadOnlySeString     defaultPlaceholder;
    private readonly List<ListButtonNode> popupButtons = [];

    private bool hasSelection;

    private bool isOpen;

    private bool isViewportEventRegistered;

    private int scrollPosition;

    private int selectedIndex = -1;

    private T? selectedOption;

    private ViewportEventListener? viewportListener;

    /// <summary>
    ///     Constructs a new <see cref="DropDownNode{T}" />.
    /// </summary>
    public DropDownNode()
    {
        AddNodeFlags(NodeFlags.HasCollision, NodeFlags.RespondToMouse);

        BackgroundNode = new SimpleNineGridNode
        {
            TexturePath        = "ui/uld/DropDownA.tex",
            TextureSize        = new Vector2(44.0f,  23.0f),
            TextureCoordinates = new Vector2(0.0f,   0.0f),
            Size               = new Vector2(250.0f, 24.0f),
            Height             = 23.0f,
            LeftOffset         = 16.0f,
            RightOffset        = 16.0f
        };
        BackgroundNode.AttachNode(this);

        CollapseArrowNode = new SimpleImageNode
        {
            TexturePath        = "ui/uld/DropDownA.tex",
            TextureCoordinates = new Vector2(44.0f, 0.0f),
            TextureSize        = new Vector2(12.0f, 12.0f),
            Position           = new Vector2(6.0f,  17.0f),
            Size               = new Vector2(12.0f, 12.0f),
            WrapMode           = WrapMode.Stretch
        };
        CollapseArrowNode.AttachNode(this);

        LabelNode = new TextNode
        {
            Position           = new Vector2(20.0f,  0.0f),
            Size               = new Vector2(218.0f, 21.0f),
            FontType           = FontType.Axis,
            FontSize           = 12,
            AlignmentType      = AlignmentType.Left,
            TextColorID        = 50,
            TextOutlineColorID = 7,
            TextFlags          = TextFlags.Ellipsis
        };
        LabelNode.AttachNode(this);

        defaultPlaceholder = IDataManager.Get().GetExcelSheet<Addon>().GetRow(1818).Text;
        LabelNode.String   = defaultPlaceholder;

        PopupContainerNode = new SimpleComponentNode
        {
            IsVisible = false
        };
        PopupContainerNode.AttachNode(this);
        PopupContainerNode.IsEnabled = false;
        PopupContainerNode.AddEvent(AtkEventType.MouseWheel,    OnMouseWheel);
        PopupContainerNode.AddEvent(AtkEventType.InputReceived, OnInputReceived);

        PopupBackgroundNode = new SimpleNineGridNode
        {
            TexturePath        = "ui/uld/ListB.tex",
            TextureCoordinates = new Vector2(0.0f,  0.0f),
            TextureSize        = new Vector2(32.0f, 32.0f),
            TopOffset          = 10,
            BottomOffset       = 12,
            LeftOffset         = 10,
            RightOffset        = 10
        };
        PopupBackgroundNode.AttachNode(PopupContainerNode);

        PopupScrollbarNode = new ScrollBarNode
        {
            OnValueChanged   = OnScrollUpdate,
            ScrollSpeed      = (int)ITEM_HEIGHT,
            HideWhenDisabled = true
        };
        PopupScrollbarNode.AttachNode(PopupContainerNode);

        PopupButtonListNode = new VerticalListNode
        {
            FitWidth = true
        };
        PopupButtonListNode.AttachNode(PopupContainerNode);

        BuildTimelines();

        Timeline?.PlayAnimation(4);

        CollisionNode.ShowClickableCursor = true;
        CollisionNode.AddEvent
        (
            AtkEventType.MouseOver,
            () => Timeline?.PlayAnimation
            (
                IsCollapsed ?
                    2 :
                    9
            )
        );
        CollisionNode.AddEvent
        (
            AtkEventType.MouseOut,
            () => Timeline?.PlayAnimation
            (
                IsCollapsed ?
                    4 :
                    11
            )
        );
        CollisionNode.AddEvent(AtkEventType.MouseClick,    () => Toggle());
        CollisionNode.AddEvent(AtkEventType.InputReceived, OnInputReceived);

        Component->SoundEffectId = 1;
        Component->SetEnabledState(true);

        RebuildPopupList();
    }

    /// <summary>
    ///     Not intended for public use, but it's here if you absolutely need it.
    /// </summary>
    public NineGridNode BackgroundNode { get; }

    /// <summary>
    ///     Not intended for public use, but it's here if you absolutely need it.
    /// </summary>
    public ImageNode CollapseArrowNode { get; }

    /// <summary>
    ///     Not intended for public use, but it's here if you absolutely need it.
    /// </summary>
    public TextNode LabelNode { get; }

    /// <summary>
    ///     Gets whether this node is collapsed.
    /// </summary>
    public bool IsCollapsed
        => !isOpen;

    /// <summary>
    ///     Gets or sets the maximum number of buttons to generate.
    /// </summary>
    /// <remarks>
    ///     Defaults to 5 entries, adjust this if your list is long.
    /// </remarks>
    public int MaxListOptions
    {
        get;
        set
        {
            field = Math.Max(1, value);
            RebuildPopupList();
        }
    } = DEFAULT_MAX_LIST_OPTIONS;

    /// <summary>
    ///     Gets or sets the action that is invoked when an option is selected.
    /// </summary>
    public Action<T>? OnOptionSelected { get; set; }

    /// <summary>
    ///     Gets or sets the action that is invoked when the collapse state changes.
    /// </summary>
    public Action<bool>? OnCollapseToggled { get; set; }

    /// <summary>
    ///     Gets or sets the action that is invoked when the button list is uncollapsed.
    /// </summary>
    public Action? OnUncollapsed { get; set; }

    /// <summary>
    ///     Gets or sets the action that is invoked when the button list is collapsed.
    /// </summary>
    public Action? OnCollapsed { get; set; }

    /// <summary>
    ///     Gets or sets the options represented by this node.
    /// </summary>
    public List<T> Options
    {
        get;
        set
        {
            field = value;

            if (hasSelection)
            {
                selectedIndex = selectedOption is { } option ?
                                    Options.IndexOf(option) :
                                    -1;

                if (selectedIndex < 0)
                {
                    selectedOption = default;
                    hasSelection   = false;
                }
            }
            else
                selectedIndex = -1;

            RefreshLabel();
            RebuildPopupList();
        }
    } = [];

    /// <summary>
    ///     Gets or sets the placeholder string to be shown when there is no option selected.
    /// </summary>
    public ReadOnlySeString? PlaceholderString
    {
        get;
        set
        {
            field = value;
            RefreshLabel();
        }
    }

    /// <summary>
    ///     Gets or sets the option that will be shown as the selected option.
    /// </summary>
    public T? SelectedOption
    {
        get => selectedOption;
        set
        {
            selectedOption = value;
            hasSelection   = value is not null;

            if (value is null)
                selectedIndex = -1;
            else
                selectedIndex = Options.IndexOf(value);

            RefreshLabel();
            RefreshSelection();
        }
    }

    /// <summary>
    ///     Gets or sets the index of the selected option.
    /// </summary>
    public int SelectedIndex
    {
        get => selectedIndex;
        set => SelectItem(value);
    }

    /// <summary>
    ///     Gets or sets whether the selected option's label is shown in this node's label.
    /// </summary>
    public bool ShowPreview
    {
        get;
        set
        {
            field = value;
            RefreshLabel();
        }
    } = true;

    /// <summary>
    ///     Function that is called to resolve an entries displayed label.
    /// </summary>
    public Func<T, ReadOnlySeString>? GetLabelFunction
    {
        get;
        set
        {
            field = value;
            RefreshLabel();
        }
    }

    private SimpleComponentNode PopupContainerNode  { get; }
    private SimpleNineGridNode  PopupBackgroundNode { get; }
    private ScrollBarNode       PopupScrollbarNode  { get; }
    private VerticalListNode    PopupButtonListNode { get; }

    /// <summary>
    ///     Selects the option at the specified index.
    /// </summary>
    public void SelectItem
    (
        int index
    )
    {
        if (index < 0 || index >= Options.Count)
        {
            DeselectItem();
            return;
        }

        selectedOption = Options[index];
        selectedIndex  = index;
        hasSelection   = true;

        RefreshLabel();
        RefreshSelection();

        if (isOpen)
            EnsureSelectionVisible();
    }

    /// <summary>
    ///     Clears the currently selected option.
    /// </summary>
    public void DeselectItem()
    {
        selectedOption = default;
        selectedIndex  = -1;
        hasSelection   = false;

        RefreshLabel();
        RefreshSelection();
    }

    /// <summary>
    ///     Gets the index of the currently selected option.
    /// </summary>
    public int GetSelectedItemIndex()
        => selectedIndex;

    /// <summary>
    ///     Collapses the button list.
    /// </summary>
    public void Collapse
    (
        bool playSoundEffect = true
    )
    {
        if (!isOpen) return;

        var restoreFocus = IsPopupFocused();

        isOpen = false;
        Timeline?.PlayAnimation(4);

        UnregisterViewportEvent();

        PopupContainerNode.IsVisible = false;
        PopupContainerNode.IsEnabled = false;
        PopupContainerNode.RemoveDrawFlags(DrawFlags.RenderOnTop);

        SetDropDownOwnerNode(false);

        if (restoreFocus)
            SetFocus();

        if (playSoundEffect && Component->SoundEffectId is not -1)
            Component->PlaySoundEffect();

        PopupContainerNode.Position = GetInitialPopupPosition();

        OnCollapsed?.Invoke();
        OnCollapseToggled?.Invoke(true);
    }

    /// <summary>
    ///     Uncollapses the button list.
    /// </summary>
    public void Uncollapse
    (
        bool playSoundEffect = true
    )
    {
        if (!IsEnabled) return;
        if (isOpen) return;

        SetDropDownOwnerNode(true);

        isOpen = true;
        Timeline?.PlayAnimation(11);

        RebuildPopupList();
        SetScrollPosition(0);
        EnsureSelectionVisible();

        PopupContainerNode.IsVisible = true;
        PopupContainerNode.IsEnabled = true;
        PopupContainerNode.AddDrawFlags(DrawFlags.RenderOnTop);

        RegisterViewportEvent();

        PositionPopup();
        FocusSelectedItem();

        if (playSoundEffect && Component->SoundEffectId is not -1)
            Component->PlaySoundEffect();

        OnUncollapsed?.Invoke();
        OnCollapseToggled?.Invoke(false);
    }

    /// <summary>
    ///     Toggles the visibility of the button list.
    /// </summary>
    public void Toggle
    (
        bool playSoundEffect = true
    )
    {
        if (!IsEnabled) return;

        if (isOpen)
            Collapse(playSoundEffect);
        else
            Uncollapse(playSoundEffect);
    }

    /// <inheritdoc />
    protected override void Dispose
    (
        bool disposing,
        bool isNativeDestructor
    )
    {
        if (disposing)
        {
            if (!isNativeDestructor)
            {
                UnregisterViewportEvent();

                if (AtkStage.Instance() is not null)
                {
                    viewportListener?.Dispose();
                    viewportListener = null;
                }

                SetDropDownOwnerNode(false);
            }

            base.Dispose(disposing, isNativeDestructor);
        }
    }

    /// <inheritdoc />
    protected override void OnSetEnabledState
    (
        AtkComponentBase* thisPtr,
        bool              enabled
    )
    {
        base.OnSetEnabledState(thisPtr, enabled);

        if (!enabled && isOpen && !IsDisposed)
            Collapse(false);
    }

    /// <inheritdoc />
    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();

        BackgroundNode.Size = new Vector2(Width, Height - 1.0f);
        LabelNode.Size      = new Vector2(Width         - 32.0f, Height - 3.0f);

        RebuildPopupList();
    }

    /// <inheritdoc />
    protected override AtkResNode* OnGetCollisionNodeAtCoords
    (
        AtkComponentBase* thisPtr,
        short             x,
        short             y
    )
    {
        if (!isOpen || !IsEnabled || !IsVisible) return null;
        if (!PopupContainerNode.CollisionNode.CheckCollision(x, y, false)) return null;

        if (PopupScrollbarNode is { IsVisible: true, IsEnabled: true })
        {
            var foreground = PopupScrollbarNode.ForegroundButtonNode.CollisionNode;
            if (foreground.CheckCollision(x, y, false)) return foreground.ResNode;

            var background = PopupScrollbarNode.BackgroundButtonNode.CollisionNode;
            if (background.CheckCollision(x, y, false)) return background.ResNode;
        }

        foreach (var button in popupButtons)
        {
            if (!button.IsVisible || !button.IsEnabled) continue;
            if (button.CollisionNode.CheckCollision(x, y, false)) return button.CollisionNode.ResNode;
        }

        return PopupContainerNode.CollisionNode.ResNode;
    }

    private void RebuildPopupList()
    {
        var buttonCount = Math.Min(MaxListOptions, Options.Count);

        if (popupButtons.Count != buttonCount)
        {
            PopupButtonListNode.Clear();
            popupButtons.Clear();

            for (var i = 0; i < buttonCount; i++)
            {
                var buttonNode = new ListButtonNode
                {
                    Height = ITEM_HEIGHT
                };
                buttonNode.AddEvent(AtkEventType.MouseWheel,    OnMouseWheel);
                buttonNode.AddEvent(AtkEventType.InputReceived, OnInputReceived);

                PopupButtonListNode.AddNode(buttonNode);
                popupButtons.Add(buttonNode);
            }
        }

        scrollPosition = Math.Clamp(scrollPosition, 0, GetMaxScrollPosition());

        ResizePopupList();
        PopulatePopupList();

        if (isOpen)
            PositionPopup();
        else
            PopupContainerNode.Position = GetInitialPopupPosition();
    }

    private void ResizePopupList()
    {
        var popupWidth  = Math.Max(Width - (POPUP_HORIZONTAL_INSET * 2.0f), 0.0f);
        var popupHeight = (popupButtons.Count * ITEM_HEIGHT) + POPUP_VERTICAL_PADDING;

        PopupContainerNode.Size  = new Vector2(popupWidth, popupHeight);
        PopupBackgroundNode.Size = new Vector2(popupWidth, popupHeight);

        PopupScrollbarNode.Size     = new Vector2(SCROLLBAR_WIDTH,                     Math.Max(popupHeight - POPUP_VERTICAL_PADDING, 0.0f));
        PopupScrollbarNode.Position = new Vector2(popupWidth - SCROLLBAR_RIGHT_OFFSET, SCROLLBAR_TOP_OFFSET);
        PopupScrollbarNode.UpdateScrollParams(PopupScrollbarNode.Height, Options.Count * ITEM_HEIGHT);

        PopupButtonListNode.Size = new Vector2(Math.Max(popupWidth - LIST_CONTENT_HORIZONTAL_PADDING, 0.0f), Math.Max(popupHeight - POPUP_VERTICAL_PADDING, 0.0f));
        PopupButtonListNode.Position = new Vector2(LIST_CONTENT_OFFSET_X, LIST_CONTENT_OFFSET_Y);
        PopupButtonListNode.RecalculateLayout();
    }

    private void PopulatePopupList()
    {
        for (var i = 0; i < popupButtons.Count; i++)
        {
            var optionIndex = scrollPosition + i;
            var buttonNode  = popupButtons[i];

            if (optionIndex >= Options.Count)
            {
                buttonNode.IsVisible = false;
                continue;
            }

            buttonNode.IsVisible = true;

            var option = Options[optionIndex];
            buttonNode.String  = GetLabelFunction?.Invoke(option) ?? UNSET_LABEL;
            buttonNode.OnClick = () => OnPopupButtonClicked(optionIndex);
        }

        RefreshSelection();
    }

    private void RefreshSelection()
    {
        for (var i = 0; i < popupButtons.Count; i++)
            popupButtons[i].Selected = scrollPosition + i == selectedIndex;
    }

    private void RefreshLabel()
    {
        if (!hasSelection)
        {
            LabelNode.String = PlaceholderString ?? defaultPlaceholder;
            return;
        }

        if (!ShowPreview) return;

        if (selectedOption is { } option)
            LabelNode.String = GetLabelFunction?.Invoke(option) ?? UNSET_LABEL;
    }

    private void OnPopupButtonClicked
    (
        int index
    )
    {
        if (!isOpen || index < 0 || index >= Options.Count) return;

        SelectItem(index);
        var option = Options[index];
        Collapse();
        OnOptionSelected?.Invoke(option);
    }

    private void SetScrollPosition
    (
        int itemOffset
    )
    {
        scrollPosition = Math.Clamp(itemOffset, 0, GetMaxScrollPosition());

        var targetPosition = scrollPosition * ITEM_HEIGHT;
        if (Math.Abs(PopupScrollbarNode.ScrollPosition - targetPosition) > 0.1f)
            PopupScrollbarNode.ScrollPosition = targetPosition;

        PopulatePopupList();
    }

    private void EnsureSelectionVisible()
    {
        if (selectedIndex < 0 || popupButtons.Count is 0) return;

        if (selectedIndex < scrollPosition)
            SetScrollPosition(selectedIndex);
        else if (selectedIndex >= scrollPosition + popupButtons.Count)
            SetScrollPosition(selectedIndex - popupButtons.Count + 1);
    }

    private int GetMaxScrollPosition()
        => Math.Max(0, Options.Count - popupButtons.Count);

    private void OnScrollUpdate
    (
        int newPosition
    )
    {
        var itemOffset = Math.Clamp((int)MathF.Round(newPosition / ITEM_HEIGHT), 0, GetMaxScrollPosition());
        if (itemOffset == scrollPosition) return;

        scrollPosition = itemOffset;
        PopulatePopupList();
    }

    private void OnMouseWheel
    (
        AtkEventListener* thisPtr,
        AtkEventType      eventType,
        int               eventParam,
        AtkEvent*         atkEvent,
        AtkEventData*     atkEventData
    )
    {
        if (!isOpen) return;
        if (Options.Count <= popupButtons.Count) return;
        if (atkEventData is null) return;

        SetScrollPosition
        (
            scrollPosition +
            (atkEventData->IsScrollUp ?
                 -1 :
                 1)
        );

        atkEvent->SetEventIsHandled();
    }

    private void OnInputReceived
    (
        AtkEventListener* thisPtr,
        AtkEventType      eventType,
        int               eventParam,
        AtkEvent*         atkEvent,
        AtkEventData*     atkEventData
    )
    {
        if (!isOpen) return;
        if (atkEventData is null) return;

        var inputId = atkEventData->InputData.InputId;
        if (inputId is not (2 or 3)) return;

        Collapse();

        atkEvent->SetEventIsHandled();
    }

    private void OnViewportMouseDown
    (
        AtkEventListener* thisPtr,
        AtkEventType      eventType,
        int               eventParam,
        AtkEvent*         atkEvent,
        AtkEventData*     atkEventData
    )
    {
        if (!isOpen) return;
        if (atkEventData is null) return;

        var mouseData = atkEventData->MouseData;

        if (!atkEventData->IsLeftClick)
        {
            Collapse(false);
            return;
        }

        if (PopupContainerNode.CollisionNode.CheckCollision(mouseData.PosX, mouseData.PosY, false)) return;
        if (CollisionNode.CheckCollision(mouseData.PosX, mouseData.PosY, false)) return;

        Collapse(false);
    }

    private void PositionPopup()
    {
        var initialPosition = GetInitialPopupPosition();

        PopupContainerNode.Position = initialPosition;

        if (ParentAddon is null) return;

        var stage = AtkStage.Instance();
        if (stage is null) return;

        var screenWidth  = stage->ScreenSize.Width;
        var screenHeight = stage->ScreenSize.Height;

        if (stage->IsScreenSizeScaled)
        {
            screenWidth  = (int)(screenWidth  * stage->ScreenSizeScale);
            screenHeight = (int)(screenHeight * stage->ScreenSizeScale);
        }

        if (screenWidth is 0 || screenHeight is 0) return;

        var transform = CollisionNode.ResNode->Transform;

        var scaleX = MathF.Sqrt((transform.M11 * transform.M11) + (transform.M12 * transform.M12));
        var scaleY = MathF.Sqrt((transform.M21 * transform.M21) + (transform.M22 * transform.M22));

        if (scaleX <= 0.0f || scaleY <= 0.0f) return;

        var anchor = CollisionNode.ScreenPosition;

        var screenX = anchor.X + (initialPosition.X * transform.M11) + (initialPosition.Y * transform.M21);
        var screenY = anchor.Y + (initialPosition.X * transform.M12) + (initialPosition.Y * transform.M22);

        var scaledWidth  = PopupContainerNode.Width  * scaleX;
        var scaledHeight = PopupContainerNode.Height * scaleY;

        if (scaledWidth <= 0.0f || scaledHeight <= 0.0f) return;

        var inverseScale = PopupContainerNode.Width / scaledWidth;

        var x = initialPosition.X;
        var y = initialPosition.Y;

        if (screenX + scaledWidth > screenWidth)
            x += (screenWidth - screenX - scaledWidth) * inverseScale;
        else if (screenX < 0.0f)
            x -= screenX;

        if (screenY + scaledHeight > screenHeight)
            y += (screenHeight - screenY - scaledHeight) * inverseScale;
        else if (screenY < 0.0f)
            y -= screenY;

        PopupContainerNode.Position = new Vector2((int)x, (int)y);
    }

    private Vector2 GetInitialPopupPosition()
        => new(POPUP_HORIZONTAL_INSET, Math.Max(Height - POPUP_VERTICAL_OFFSET, 0.0f));

    private void RegisterViewportEvent()
    {
        if (isViewportEventRegistered) return;

        var stage = AtkStage.Instance();
        if (stage is null) return;

        viewportListener ??= new ViewportEventListener(OnViewportMouseDown);

        stage->ViewportEventManager.RegisterEvent(AtkEventType.MouseDown, 2, null, null, viewportListener, false);

        isViewportEventRegistered = true;
    }

    private void UnregisterViewportEvent()
    {
        if (!isViewportEventRegistered) return;

        var stage = AtkStage.Instance();
        if (stage is not null && viewportListener is not null)
            stage->ViewportEventManager.UnregisterEvent(AtkEventType.MouseDown, 2, viewportListener, false);

        isViewportEventRegistered = false;
    }

    private void FocusSelectedItem()
    {
        if (!isOpen) return;

        var visibleIndex = selectedIndex - scrollPosition;
        if (visibleIndex >= 0 && visibleIndex < popupButtons.Count)
            popupButtons[visibleIndex].SetFocus();
        else
            PopupContainerNode.SetFocus();
    }

    private bool IsPopupFocused()
    {
        var stage = AtkStage.Instance();
        if (stage is null) return false;

        var inputManager = stage->AtkInputManager;
        if (inputManager is null) return false;

        var focusedNode = inputManager->FocusedNode;

        while (focusedNode is not null)
        {
            if (focusedNode == PopupContainerNode.ResNode) return true;

            focusedNode = focusedNode->ParentNode;
        }

        return false;
    }

    private void SetDropDownOwnerNode
    (
        bool isOwner
    )
    {
        if (ParentAddon is null) return;

        var componentNode = (AtkComponentNode*)ResNode;

        var inputRegions = ParentAddon->AdditionalFocusableNodes;

        if (isOwner)
        {
            for (var i = 0; i < inputRegions.Length; i++)
            {
                if (inputRegions[i].Value is not null && inputRegions[i].Value != PopupContainerNode.ResNode) continue;

                inputRegions[i]                       = PopupContainerNode.ResNode;
                ParentAddon->CurrentDropDownOwnerNode = componentNode;
                return;
            }

            throw new InvalidOperationException("下拉列表无法登记输入区域，所属窗口的附加焦点区域均已占用。");
        }

        for (var i = 0; i < inputRegions.Length; i++)
        {
            if (inputRegions[i].Value == PopupContainerNode.ResNode)
                inputRegions[i] = null;
        }

        if (ParentAddon->CurrentDropDownOwnerNode == componentNode)
            ParentAddon->CurrentDropDownOwnerNode = null;
    }

    private void BuildTimelines()
    {
        AddTimeline
        (
            new TimelineBuilder()
                .BeginFrameSet(1, 120)
                .AddLabel(1,   1,  AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(9,   0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(10,  2,  AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(19,  0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(20,  3,  AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(29,  0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(30,  7,  AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(39,  0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(40,  6,  AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(49,  0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(50,  4,  AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(59,  0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(60,  8,  AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(69,  0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(70,  9,  AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(79,  0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(80,  10, AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(89,  0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(90,  14, AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(99,  0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(100, 13, AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(109, 0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(110, 11, AtkTimelineJumpBehavior.Start,    0)
                .AddLabel(120, 0,  AtkTimelineJumpBehavior.PlayOnce, 0)
                .EndFrameSet()
                .Build()
        );
        CollapseArrowNode.AddTimeline
        (
            new TimelineBuilder()
                .BeginFrameSet(1, 9)
                .AddFrame(1, new Vector2(6, 17), rotation: 4.712389f, alpha: 255, addColor: new Vector3(0, 0, 0), multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(10, 19)
                .AddFrame(10, new Vector2(6, 17), rotation: 4.712389f, alpha: 255, addColor: new Vector3(0,  0,  0),  multiplyColor: new Vector3(100, 100, 100))
                .AddFrame(12, new Vector2(6, 17), rotation: 4.712389f, alpha: 255, addColor: new Vector3(16, 16, 16), multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(20, 29)
                .AddFrame(20, new Vector2(6, 18), rotation: 4.712389f, alpha: 255, addColor: new Vector3(16, 16, 16), multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(30, 39)
                .AddFrame(30, new Vector2(6, 17), rotation: 4.712389f, alpha: 178, addColor: new Vector3(0, 0, 0), multiplyColor: new Vector3(50, 50, 50))
                .EndFrameSet()
                .BeginFrameSet(40, 49)
                .AddFrame(40, new Vector2(6, 17), rotation: 4.712389f, alpha: 255, addColor: new Vector3(16, 16, 16), multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(50, 59)
                .AddFrame(50, new Vector2(6, 17), rotation: 4.712389f, alpha: 255, addColor: new Vector3(16, 16, 16), multiplyColor: new Vector3(100, 100, 100))
                .AddFrame(52, new Vector2(6, 17), rotation: 4.712389f, alpha: 255, addColor: new Vector3(0,  0,  0),  multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(60, 69)
                .AddFrame(60, new Vector2(6, 6), rotation: 0, alpha: 255, addColor: new Vector3(0, 0, 0), multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(70, 79)
                .AddFrame(70, new Vector2(6, 6), rotation: 0, alpha: 255, addColor: new Vector3(0,  0,  0),  multiplyColor: new Vector3(100, 100, 100))
                .AddFrame(72, new Vector2(6, 6), rotation: 0, alpha: 255, addColor: new Vector3(16, 16, 16), multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(80, 89)
                .AddFrame(80, new Vector2(6, 7), rotation: 0, alpha: 255, addColor: new Vector3(16, 16, 16), multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(90, 99)
                .AddFrame(90, new Vector2(6, 6), rotation: 0, alpha: 178, addColor: new Vector3(0, 0, 0), multiplyColor: new Vector3(50, 50, 50))
                .EndFrameSet()
                .BeginFrameSet(100, 109)
                .AddFrame(100, new Vector2(6, 6), rotation: 0, alpha: 255, addColor: new Vector3(16, 16, 16), multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(110, 120)
                .AddFrame(110, new Vector2(6, 6), rotation: 0, alpha: 255, addColor: new Vector3(16, 16, 16), multiplyColor: new Vector3(100, 100, 100))
                .AddFrame(112, new Vector2(6, 6), rotation: 0, alpha: 255, addColor: new Vector3(0,  0,  0),  multiplyColor: new Vector3(100, 100, 100))
                .EndFrameSet()
                .Build()
        );

        LabelNode.AddTimeline
        (
            new TimelineBuilder()
                .BeginFrameSet(1, 9)
                .AddFrame(1, new Vector2(20, 0), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(10, 19)
                .AddFrame(10, new Vector2(20, 0), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(20, 29)
                .AddFrame(20, new Vector2(20, 1), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(30, 39)
                .AddFrame(30, new Vector2(20, 0), 153, new Vector3(0, 0, 0), new Vector3(80, 80, 80))
                .EndFrameSet()
                .BeginFrameSet(40, 49)
                .AddFrame(40, new Vector2(20, 0), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(50, 59)
                .AddFrame(50, new Vector2(20, 0), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(60, 69)
                .AddFrame(60, new Vector2(20, 0), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(70, 79)
                .AddFrame(70, new Vector2(20, 0), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(80, 89)
                .AddFrame(80, new Vector2(20, 1), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(90, 99)
                .AddFrame(90, new Vector2(20, 0), 153, new Vector3(0, 0, 0), new Vector3(80, 80, 80))
                .EndFrameSet()
                .BeginFrameSet(100, 109)
                .AddFrame(100, new Vector2(20, 0), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(110, 120)
                .AddFrame(110, new Vector2(20, 0), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .Build()
        );

        BackgroundNode.AddTimeline
        (
            new TimelineBuilder()
                .BeginFrameSet(1, 9)
                .AddFrame(1, new Vector2(0, 0), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(10, 19)
                .AddFrame(10, new Vector2(0, 0), 255, new Vector3(0,  0,  0),  new Vector3(100, 100, 100))
                .AddFrame(12, new Vector2(0, 0), 255, new Vector3(16, 16, 16), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(20, 29)
                .AddFrame(20, new Vector2(0, 1), 255, new Vector3(16, 16, 16), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(30, 39)
                .AddFrame(30, new Vector2(0, 0), 178, new Vector3(0, 0, 0), new Vector3(50, 50, 50))
                .EndFrameSet()
                .BeginFrameSet(40, 49)
                .AddFrame(40, new Vector2(0, 0), 255, new Vector3(16, 16, 16), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(50, 59)
                .AddFrame(50, new Vector2(0, 0), 255, new Vector3(16, 16, 16), new Vector3(100, 100, 100))
                .AddFrame(52, new Vector2(0, 0), 255, new Vector3(0,  0,  0),  new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(60, 69)
                .AddFrame(60, new Vector2(0, 0), 255, new Vector3(0, 0, 0), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(70, 79)
                .AddFrame(70, new Vector2(0, 0), 255, new Vector3(0,  0,  0),  new Vector3(100, 100, 100))
                .AddFrame(72, new Vector2(0, 0), 255, new Vector3(16, 16, 16), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(80, 89)
                .AddFrame(80, new Vector2(0, 1), 255, new Vector3(16, 16, 16), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(90, 99)
                .AddFrame(90, new Vector2(0, 0), 178, new Vector3(0, 0, 0), new Vector3(50, 50, 50))
                .EndFrameSet()
                .BeginFrameSet(100, 109)
                .AddFrame(100, new Vector2(0, 0), 255, new Vector3(16, 16, 16), new Vector3(100, 100, 100))
                .EndFrameSet()
                .BeginFrameSet(110, 120)
                .AddFrame(110, new Vector2(0, 0), 255, new Vector3(16, 16, 16), new Vector3(100, 100, 100))
                .AddFrame(112, new Vector2(0, 0), 255, new Vector3(0,  0,  0),  new Vector3(100, 100, 100))
                .EndFrameSet()
                .Build()
        );
    }

    #region 常量

    private const int    DEFAULT_MAX_LIST_OPTIONS        = 5;
    private const float  ITEM_HEIGHT                     = 22.0f;
    private const float  LIST_CONTENT_HORIZONTAL_PADDING = 24.0f;
    private const float  LIST_CONTENT_OFFSET_X           = 8.0f;
    private const float  LIST_CONTENT_OFFSET_Y           = 6.0f;
    private const float  POPUP_HORIZONTAL_INSET          = 4.0f;
    private const float  POPUP_VERTICAL_OFFSET           = 3.0f;
    private const float  POPUP_VERTICAL_PADDING          = 18.0f;
    private const float  SCROLLBAR_RIGHT_OFFSET          = 14.0f;
    private const float  SCROLLBAR_TOP_OFFSET            = 4.5f;
    private const float  SCROLLBAR_WIDTH                 = 8.0f;
    private const string UNSET_LABEL                     = "DropDownNode.GetLabelFunction was not set.";

    #endregion
}
