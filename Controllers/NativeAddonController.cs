using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Internal.Classes;

namespace KamiToolKit.Controllers;

/// <summary>
///     Owns a native child addon controller and forwards the parent lifecycle to its children.
/// </summary>
/// <remarks>
///     Each parent name and index has one controller. Native operations must run on the game thread.
/// </remarks>
public class NativeAddonController : IDisposable, IAsyncDisposable
{
    private readonly NativeAddonControlFunctions functions;
    private readonly List<NativeChildAddon>      children          = [];
    private readonly Queue<Action>               pendingOperations = [];
    private readonly AddonEvent[] parentEvents =
    [
        AddonEvent.PostSetup,
        AddonEvent.PostOpen,
        AddonEvent.PostUpdate,
        AddonEvent.PostDraw,
        AddonEvent.PostShow,
        AddonEvent.PostHide,
        AddonEvent.PostClose,
        AddonEvent.PostRefresh,
        AddonEvent.PostRequestedUpdate,
        AddonEvent.PostFocus,
        AddonEvent.PostFocusChanged,
        AddonEvent.PreFinalize
    ];

    private bool isDisposed;
    private bool disposeWhenEmpty;
    private bool parentClosing;
    private int  dispatchDepth;
    private byte originalParentGroupFlag;
    private uint originalParentControlFlag;

    /// <summary>
    ///     Gets the internal name of the parent addon.
    /// </summary>
    public string ParentAddonName { get; }

    /// <summary>
    ///     Gets the one-based index used to resolve the parent addon.
    /// </summary>
    public int ParentAddonIndex { get; }

    /// <summary>
    ///     Gets the current parent allocation, or null while the parent is unavailable.
    /// </summary>
    public unsafe AtkUnitBase* ParentAddon { get; private set; }

    /// <summary>
    ///     Gets the native controller allocation, or null while the parent is unavailable.
    /// </summary>
    public unsafe AtkAddonControl* NativeControl { get; private set; }

    /// <summary>
    ///     Gets the managed children owned by this controller.
    /// </summary>
    public IReadOnlyList<NativeChildAddon> Children => children;

    /// <summary>
    ///     Occurs when the native controller binds to a ready parent allocation.
    /// </summary>
    public event Action? ParentReady;

    /// <summary>
    ///     Gets the controller for a parent, creating one if needed.
    /// </summary>
    /// <remarks>
    ///     A controller created by this method disposes itself after its last child unregisters. An existing explicitly constructed controller retains its original lifetime.
    /// </remarks>
    public static NativeAddonController GetOrCreate
    (
        string parentAddonName,
        int    parentAddonIndex = 1
    )
    {
        if (!ThreadSafety.IsMainThread)
            throw new InvalidOperationException("NativeAddonController must be created on the game thread.");

        var controller = KamiToolKitLibrary.Experimental.AddonControllers.FirstOrDefault
            (existing => existing.ParentAddonName == parentAddonName && existing.ParentAddonIndex == parentAddonIndex);

        return controller ??
               new NativeAddonController(parentAddonName, parentAddonIndex)
               {
                   disposeWhenEmpty = true
               };
    }

    /// <summary>
    ///     Creates a controller for the specified parent name and one-based index.
    /// </summary>
    /// <remarks>
    ///     Throws if another controller already manages that parent. Disposing this controller also disposes its registered children.
    /// </remarks>
    public unsafe NativeAddonController
    (
        string parentAddonName,
        int    parentAddonIndex = 1
    )
    {
        if (!ThreadSafety.IsMainThread)
            throw new InvalidOperationException("NativeAddonController must be created on the game thread.");

        ArgumentException.ThrowIfNullOrEmpty(parentAddonName);
        ArgumentOutOfRangeException.ThrowIfLessThan(parentAddonIndex, 1);

        ParentAddonName  = parentAddonName;
        ParentAddonIndex = parentAddonIndex;

        var controllers = KamiToolKitLibrary.Experimental.AddonControllers;
        if (controllers.Any(controller => controller.ParentAddonName == parentAddonName && controller.ParentAddonIndex == parentAddonIndex))
            throw new InvalidOperationException("This parent addon already has a NativeAddonController.");

        functions = KamiToolKitLibrary.Experimental.AddonControlFunctions;
        foreach (var addonEvent in parentEvents)
            IAddonLifecycle.Get().RegisterListener(addonEvent, ParentAddonName, OnParentEvent);

        controllers.Add(this);

        var parent = IGameGui.Get().GetAddonByName(parentAddonName, parentAddonIndex);
        if (parent.IsReady)
            Bind((AtkUnitBase*)parent.Address);
    }

    /// <summary>
    ///     Releases the native controller and disposes all registered children.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed)
            return;

        EnsureAccess();
        isDisposed = true;
        IAddonLifecycle.Get().UnregisterListener(OnParentEvent);
        KamiToolKitLibrary.Experimental.AddonControllers.Remove(this);

        if (dispatchDepth != 0)
        {
            pendingOperations.Enqueue(ReleaseOwnedAddons);
            return;
        }

        ReleaseOwnedAddons();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Disposes the controller on the game thread and waits for its children to close.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        NativeChildAddon[] ownedAddons = [];
        await IFramework.Get().Run
        (() =>
            {
                ownedAddons = [.. children];
                Dispose();
            }
        );
        foreach (var child in ownedAddons)
            await child.CloseAsync();
    }

    /// <summary>
    ///     Selects a tab index and activates and focuses its group.
    /// </summary>
    /// <remarks>
    ///     Returns uint.MaxValue when no child matches. Otherwise, the high word contains the tab index and the low word contains the child id if its group was already active, or zero.
    /// </remarks>
    public unsafe uint SelectTab
    (
        int tabIndex
    )
    {
        EnsureAccess();
        if (NativeControl is null)
            throw new InvalidOperationException("The parent addon is not allocated.");

        var selected = uint.MaxValue;
        Dispatch(() => selected = functions.SelectTab(NativeControl, tabIndex));
        return selected;
    }

    /// <summary>
    ///     Activates the specified group, optionally deactivating other attached groups and requesting focus.
    /// </summary>
    public unsafe void ActivateGroup
    (
        int  groupID,
        bool deactivateOtherGroups = true,
        bool focus                 = true
    )
    {
        EnsureAccess();
        if (NativeControl is null)
            throw new InvalidOperationException("The parent addon is not allocated.");

        Dispatch
        (() => functions.ActivateGroup
         (
             NativeControl,
             groupID,
             deactivateOtherGroups ?
                 (byte)1 :
                 (byte)0,
             focus ?
                 (byte)1 :
                 (byte)0
         )
        );
    }

    /// <summary>
    ///     Sets the size of managed children, optionally including detached children.
    /// </summary>
    public void ResizeChildren
    (
        Vector2 size,
        bool    includeDetached = false
    )
    {
        EnsureAccess();
        var windowSize = new Vector2(checked((ushort)size.X), checked((ushort)size.Y));

        foreach (var child in children)
        {
            if (includeDetached || child.IsAttached)
                child.Size = windowSize;
        }
    }

    /// <summary>
    ///     Registers an event listener on the current native controller.
    /// </summary>
    public unsafe AtkEvent* RegisterEvent
    (
        AtkEventType      eventType,
        uint              eventParam,
        AtkEventListener* listener,
        AtkResNode*       nodeParam = null
    )
    {
        EnsureAccess();
        return NativeControl is null ?
                   throw new InvalidOperationException("The parent addon is not allocated.") :
                   NativeControl->RegisterEvent(eventType, eventParam, listener, nodeParam);
    }

    /// <summary>
    ///     Removes a matching event listener from the current native controller.
    /// </summary>
    public unsafe bool UnregisterEvent
    (
        AtkEventType      eventType,
        uint              eventParam,
        AtkEventListener* listener
    )
    {
        EnsureAccess();
        return NativeControl is not null && NativeControl->EventManager.UnregisterEvent(eventType, eventParam, listener, false);
    }

    /// <summary>
    ///     Checks whether the current controller has a listener for the event type.
    /// </summary>
    public unsafe bool IsEventRegistered
    (
        AtkEventType eventType
    )
    {
        EnsureAccess();
        if (NativeControl is null)
            return false;

        for (var currentEvent = NativeControl->EventManager.Event; currentEvent is not null; currentEvent = currentEvent->NextEvent)
        {
            if (currentEvent->State.EventType == eventType)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Dispatches an event through the native controller and reports whether it was handled.
    /// </summary>
    public unsafe bool DispatchEvent
    (
        ref AtkEventDispatcher.Event addonEvent
    )
    {
        EnsureAccess();
        if (NativeControl is null)
            return false;

        fixed (AtkEventDispatcher.Event* eventPointer = &addonEvent)
        {
            var handled      = false;
            var eventAddress = (nint)eventPointer;
            Dispatch(() => handled = functions.DispatchEvent(NativeControl, (AtkEventDispatcher.Event*)eventAddress) != 0);
            return handled;
        }
    }

    internal void EnsureAccess()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (!ThreadSafety.IsMainThread)
            throw new InvalidOperationException("Native addon operations must run on the game thread.");
    }

    internal void Register
    (
        NativeChildAddon child
    )
    {
        EnsureAccess();
        if (dispatchDepth != 0)
            pendingOperations.Enqueue(() => children.Add(child));
        else
            children.Add(child);
    }

    internal unsafe void OpenChild
    (
        NativeChildAddon child
    )
    {
        if (isDisposed || !child.IsRequestedOpen || NativeControl is null || parentClosing)
            return;

        if (child.Info is not null)
        {
            child.SynchronizeState();
            return;
        }

        if (child.IsAllocated)
            return;

        if (dispatchDepth != 0)
        {
            pendingOperations.Enqueue(() => OpenChild(child));
            return;
        }

        try
        {
            ArgumentException.ThrowIfNullOrEmpty(child.InternalName);
            if (children.Any(existing => existing != child && existing.InternalName == child.InternalName))
                throw new InvalidOperationException("The child addon name must be unique within its controller.");

            if (child.InternalName == ParentAddonName)
                throw new InvalidOperationException("An addon cannot be its own parent.");

            Dispatch
            (() =>
                {
                    var position = child.LocalPosition;
                    child.IsClosing = false;
                    child.AllocateAddon();
                    var addon = child.InternalAddon;
                    if (addon is null)
                        throw new InvalidOperationException("Unable to allocate the child addon.");

                    addon->DisableUnfocusedCloseOnEsc  = true;
                    addon->DisableFocusOnShow          = true;
                    addon->DisableCloseOnLoadScreen    = true;
                    addon->DisableShowHideSoundEffects = true;
                    addon->UiFlags                     = ParentAddon->UiFlags;
                    addon->IgnoreUIDisplayMode         = ParentAddon->IgnoreUIDisplayMode;
                    addon->Open(ParentAddon->DepthLayer);

                    child.Info = functions.RegisterChild
                    (
                        NativeControl,
                        addon->Id,
                        child.InitiallyActive ?
                            (byte)1 :
                            (byte)0,
                        child.AllowDetach ?
                            (byte)1 :
                            (byte)0,
                        child.TabIndex,
                        child.GroupID,
                        0,
                        child.IsAttached ?
                            (byte)1 :
                            (byte)0,
                        0,
                        0
                    );

                    if (child.Info is null)
                    {
                        child.CloseAllocatedAddon();
                        throw new InvalidOperationException("The native addon controller rejected the child addon.");
                    }

                    addon->ParentId       = ParentAddon->Id;
                    child.Info->PositionX = checked((short)position.X);
                    child.Info->PositionY = checked((short)position.Y);
                    functions.SetScale(addon, ParentAddon->Scale / AtkUnitBase.GetGlobalUIScale());
                    if (!child.IsAttached)
                        addon->SetPosition(checked((short)child.DetachedPosition.X), checked((short)child.DetachedPosition.Y));

                    UpdateDragHandle(child);
                    UpdateAttachmentHiddenNode(child);
                    UpdateVisibility(child);
                }
            );
        }
        catch
        {
            child.CancelOpening();
            CloseChild(child);
            throw;
        }
    }

    internal unsafe void CloseChild
    (
        NativeChildAddon child
    )
    {
        if (!ThreadSafety.IsMainThread)
            throw new InvalidOperationException("Native addon operations must run on the game thread.");

        if (dispatchDepth != 0)
        {
            pendingOperations.Enqueue(() => CloseChild(child));
            return;
        }

        if (child.Info is null || NativeControl is null)
        {
            child.CloseAllocatedAddon();
            return;
        }

        var addon = child.InternalAddon;
        child.SynchronizeState();
        child.Info      = null;
        child.IsClosing = true;
        CancelDrag();
        addon->HostId   =  0;
        addon->Flags1A0 |= 4;
        addon->Flags1A3 |= 2;
        Dispatch(() => functions.RemoveChild(NativeControl, addon));
    }

    internal void Forget
    (
        NativeChildAddon child
    )
    {
        if (!ThreadSafety.IsMainThread)
            throw new InvalidOperationException("Native addon operations must run on the game thread.");

        if (dispatchDepth != 0)
        {
            pendingOperations.Enqueue(() => Forget(child));
            return;
        }

        children.Remove(child);
        child.Close();
        if (disposeWhenEmpty && children.Count == 0)
            Dispose();
    }

    internal unsafe void ChildFinalizing
    (
        NativeChildAddon child
    )
    {
        var info = child.Info;

        if (info is not null && NativeControl is not null)
        {
            var addon            = info->Addon;
            var allocatedControl = NativeControl;
            info->AddonID   = 0;
            child.IsClosing = true;
            child.CancelOpening();
            child.Released();
            CancelDrag();

            if (dispatchDepth != 0)
                pendingOperations.Enqueue
                (() =>
                    {
                        if (NativeControl == allocatedControl)
                            functions.RemoveChild(NativeControl, addon);
                    }
                );
            else
                Dispatch(() => functions.RemoveChild(NativeControl, addon));
        }
        else
            child.Released();
    }

    internal unsafe void SetAttached
    (
        NativeChildAddon child,
        bool             attached
    )
    {
        if (child.Info is null || NativeControl is null)
        {
            child.RememberAttachment(attached);
            return;
        }

        if (dispatchDepth != 0)
        {
            pendingOperations.Enqueue(() => SetAttached(child, attached));
            return;
        }

        Dispatch
        (() =>
            {
                CancelDrag();
                if (attached)
                    functions.Attach(NativeControl, child.Info);
                else
                    functions.Detach(NativeControl, child.Info);

                UpdateVisibility(child);
                child.SynchronizeState();
            }
        );
    }

    internal unsafe void UpdateDragHandle
    (
        NativeChildAddon child
    )
    {
        if (child.Info is null || NativeControl is null || (child.Info->Flags1 & 1) == 0)
            return;

        var addon = child.InternalAddon;
        child.CaptureHeaderCollisionNode();
        var header = child.DragHandle is null ?
                         child.OriginalHeaderCollisionNode :
                         (AtkCollisionNode*)child.DragHandle.ResNode;
        var handle = (AtkResNode*)header;
        if (child.Info->DragHandle == handle)
            return;

        CancelDrag();
        if (child.Info->DragHandle is not null)
            child.Info->DragHandle->AtkEventManager.UnregisterEvent(AtkEventType.ButtonPress, addon->Id, (AtkEventListener*)((byte*)NativeControl + 8), false);

        child.Info->DragHandle           = null;
        addon->WindowHeaderCollisionNode = header;
        child.Info->CollisionNode        = addon->WindowHeaderCollisionNode;
        if (handle is not null)
            functions.BindDragHandle(NativeControl, child.Info, handle);
    }

    internal unsafe void UpdateAttachmentHiddenNode
    (
        NativeChildAddon child
    )
    {
        if (child.Info is null)
            return;

        if (child.Info->AttachmentHiddenNode is not null)
        {
            child.Info->AttachmentHiddenNode->NodeFlags |= NodeFlags.Visible;
            child.Info->AttachmentHiddenNode->DrawFlags |= 0x100;
        }

        child.Info->AttachmentHiddenNode = child.AttachmentHiddenNode is null ?
                                               null :
                                               child.AttachmentHiddenNode.ResNode;
        var node = child.Info->AttachmentHiddenNode;

        if (node is not null)
        {
            if (child.IsAttached)
                node->NodeFlags &= ~NodeFlags.Visible;
            else
                node->NodeFlags |= NodeFlags.Visible;

            node->DrawFlags |= 0x100;
        }
    }

    internal unsafe void UpdateVisibility
    (
        NativeChildAddon child
    )
    {
        if (child.InternalAddon is null || child.Info is null || ParentAddon is null)
            return;

        Dispatch
        (() =>
            {
                if (!child.IsShown)
                    child.InternalAddon->Hide(true, false, CHILD_HIDE_FLAG);
                else if (ParentAddon->IsVisible)
                    child.InternalAddon->Show(true, CHILD_HIDE_FLAG);
                else
                    child.InternalAddon->Hide(true, false, 1);
            }
        );
    }

    private unsafe void Bind
    (
        AtkUnitBase* parent
    )
    {
        if (ParentAddon == parent && NativeControl is not null)
        {
            NativeControl->WindowHeaderCollisionNode = parent->WindowHeaderCollisionNode;
            return;
        }

        ReleaseControl();
        ParentAddon               = parent;
        parentClosing             = false;
        originalParentGroupFlag   = (byte)(parent->Flags1A3 & 1);
        originalParentControlFlag = parent->Flags1C8 & 8;
        NativeControl             = NativeMemoryHelper.UiAlloc<AtkAddonControl>();
        functions.Construct(NativeControl);
        functions.Initialize(NativeControl, parent);
        Dispatch(() => ParentReady?.Invoke());
    }

    private unsafe void OnParentEvent
    (
        AddonEvent addonEvent,
        AddonArgs  args
    )
    {
        var parent = (AtkUnitBase*)args.Addon.Address;
        if (isDisposed || parent is null)
            return;

        var matchingParent = IGameGui.Get().GetAddonByName(ParentAddonName, ParentAddonIndex);
        if (ParentAddon != parent && matchingParent.Address != args.Addon.Address)
            return;

        if (addonEvent is AddonEvent.PostSetup or AddonEvent.PostOpen)
            Bind(parent);

        if (ParentAddon != parent || NativeControl is null)
            return;

        switch (addonEvent)
        {
            case AddonEvent.PostUpdate:
                Dispatch(UpdateChildren);
                break;

            case AddonEvent.PostDraw:
                Dispatch(() => functions.Draw(NativeControl));
                break;

            case AddonEvent.PostShow when args is AddonShowArgs showArgs:
                Dispatch(() => functions.ShowChildren(NativeControl, showArgs.UnsetShowHideFlags));

                foreach (var child in children.ToArray())
                {
                    if (!child.IsShown)
                        UpdateVisibility(child);
                }

                break;

            case AddonEvent.PostHide when args is AddonHideArgs hideArgs:
                Dispatch
                (() => functions.HideChildren
                 (
                     NativeControl,
                     hideArgs.CallHideCallback ?
                         (byte)1 :
                         (byte)0,
                     (parent->VisibilityState & AtkUnitBaseVisibilityState.TransitionComplete) != 0 ?
                         (byte)1 :
                         (byte)0,
                     hideArgs.SetShowHideFlags
                 )
                );
                break;

            case AddonEvent.PostClose:
            case AddonEvent.PreFinalize:
                parentClosing = true;
                if (dispatchDepth != 0)
                    pendingOperations.Enqueue(ReleaseControl);
                else
                    ReleaseControl();
                break;

            case AddonEvent.PostRefresh when args is AddonRefreshArgs refreshArgs:
                Dispatch
                (() =>
                    {
                        foreach (var child in children.ToArray())
                        {
                            if (child.Info is not null && (child.Info->Flags1 & 1) != 0)
                                child.InternalAddon->OnRefresh(refreshArgs.AtkValueCount, (AtkValue*)refreshArgs.AtkValues);
                        }
                    }
                );
                break;

            case AddonEvent.PostRequestedUpdate when args is AddonRequestedUpdateArgs updateArgs:
                Dispatch
                (() =>
                    {
                        foreach (var child in children.ToArray())
                        {
                            if (child.Info is not null && (child.Info->Flags1 & 1) != 0)
                                child.InternalAddon->OnRequestedUpdate((NumberArrayData**)updateArgs.NumberArrayData, (StringArrayData**)updateArgs.StringArrayData);
                        }
                    }
                );
                break;

            case AddonEvent.PostFocus:
            case AddonEvent.PostFocusChanged when args is AddonFocusChangedArgs { ShouldFocus: true }:
                Dispatch(() => functions.FocusSelected(NativeControl));
                break;
        }
    }

    private unsafe void UpdateChildren()
    {
        if (parentClosing)
            return;

        foreach (var child in children)
        {
            OpenChild(child);
            if (child.Info is null)
                continue;

            var addon = child.InternalAddon;
            addon->UiFlags             = ParentAddon->UiFlags;
            addon->IgnoreUIDisplayMode = ParentAddon->IgnoreUIDisplayMode;
            if (addon->DepthLayer != ParentAddon->DepthLayer)
                addon->SetDepthLayer(ParentAddon->DepthLayer);

            if (child.IsAttached)
            {
                if (MathF.Abs(addon->Scale - ParentAddon->Scale) > 0.0001f)
                    functions.SetScale(addon, ParentAddon->Scale / AtkUnitBase.GetGlobalUIScale());

                if (addon->Alpha != ParentAddon->Alpha)
                    addon->SetAlpha(ParentAddon->Alpha);
            }
        }

        functions.Update(NativeControl);

        foreach (var child in children)
        {
            UpdateDragHandle(child);
            child.SynchronizeState();
        }
    }

    private void Dispatch
    (
        Action action
    )
    {
        dispatchDepth++;

        try
        {
            action();
        }
        finally
        {
            dispatchDepth--;

            if (dispatchDepth == 0)
            {
                while (pendingOperations.TryDequeue(out var operation))
                    operation();
            }
        }
    }

    private unsafe void CancelDrag()
    {
        if (NativeControl is null)
            return;

        functions.CancelDrag(NativeControl);
        var state = (NativeAddonControlState*)NativeControl;
        state->DraggingChild  = null;
        state->AttachingChild = null;
    }

    private unsafe void ReleaseControl()
    {
        if (NativeControl is null)
            return;

        CancelDrag();
        var releasedControl = NativeControl;
        var parent          = ParentAddon;
        NativeControl    = null;
        ParentAddon      = null;
        parent->Flags1A3 = (byte)((parent->Flags1A3 & ~1) | originalParentGroupFlag);
        parent->Flags1C8 = (parent->Flags1C8 & ~8u) | originalParentControlFlag;

        Dispatch
        (() =>
            {
                foreach (var child in children.ToArray())
                {
                    if (child.Info is null)
                        continue;

                    child.ParentClosing();
                    var addon = child.InternalAddon;
                    addon->HostId   =  0;
                    addon->Flags1A0 |= 4;
                    addon->Flags1A3 |= 2;
                }

                functions.Destroy(releasedControl, 0);
                NativeMemoryHelper.UiFree(releasedControl);
            }
        );
    }

    private void ReleaseOwnedAddons()
    {
        ReleaseControl();
        foreach (var child in children.ToArray())
            child.Dispose();

        children.Clear();
        GC.SuppressFinalize(this);
    }

    #region 常量

    private const uint CHILD_HIDE_FLAG = 8;

    #endregion
}
