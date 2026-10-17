using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Internal.Classes;

namespace KamiToolKit.Controllers;

/// <inheritdoc />
public class NativeListController : NativeListController<AtkUnitBase, ListItemData>;

/// <inheritdoc />
public class NativeListController<T> : NativeListController<T, ListItemData> where T : unmanaged;

/// <summary>
///     Controller for modifying native AtkListComponents and their various parts and properties.
/// </summary>
public class NativeListController<T, TU> : IDisposable, IAsyncDisposable where T : unmanaged where TU : ListItemData, new()
{
    /// <summary>
    ///     Delegate that is called when the list controller is setting up and trying to hook the games node populator.
    /// </summary>
    public unsafe delegate AtkComponentListItemRenderer* GetPopulatorNodeHandler
    (
        T* addon
    );

    /// <summary>
    ///     Delegate that is called to undo a change from a list entry.
    /// </summary>
    public unsafe delegate void ResetElementHandler
    (
        T* unitBase,
        TU listItem
    );

    /// <summary>
    ///     Delegate that is called when the controller is trying to determine if an element should be modified.
    /// </summary>
    public unsafe delegate bool ShouldModifyElementHandler
    (
        T* unitBase,
        TU listItem
    );

    /// <summary>
    ///     Delegate that is called to apply a change to a list entry.
    /// </summary>
    public unsafe delegate void UpdateElementHandler
    (
        T* unitBase,
        TU listItem
    );

    private readonly List<ListNodeEntry> attachedNodes = [];

    private Hook<AtkComponentListItemPopulator.PopulateDelegate>?             onListPopulate;
    private Hook<AtkComponentListItemPopulator.PopulateWithRendererDelegate>? onRendererPopulate;

    /// <summary>
    ///     Addon name to bind to.
    /// </summary>
    public required string AddonName { get; init; }

    /// <summary>
    ///     Define a function that will return true if the provided list item should be modified by this controller.
    /// </summary>
    /// <remarks>
    ///     If no function is defined it will be assumed that each line should be edited.
    /// </remarks>
    public ShouldModifyElementHandler? ShouldModifyElement { get; init; }

    /// <summary>
    ///     Define how specifically you want the list item to be modified.
    /// </summary>
    public UpdateElementHandler? UpdateElement { get; init; }

    /// <summary>
    ///     Define how specifically you want the list item to be reset.
    /// </summary>
    public ResetElementHandler? ResetElement { get; init; }

    /// <summary>
    ///     Function that gets the root ComponentItemRenderer to extract the populator functions from.
    /// </summary>
    public required GetPopulatorNodeHandler GetPopulatorNode { get; init; }

    /// <summary>
    ///     List of modified node indexes.
    /// </summary>
    public List<uint> ModifiedIndexes { get; } = [];

    /// <summary>
    ///     Adds an attached node to the list item's collision list. Added nodes are owned by the controller.
    /// </summary>
    /// <param name="listItem">List item containing the node.</param>
    /// <param name="node">Node to add.</param>
    /// <remarks>
    ///     Configure the node's events before adding it. This must be invoked from the main game thread.
    /// </remarks>
    public unsafe void AddNode
    (
        TU        listItem,
        NodeBase? node
    )
    {
        if (node is null) return;
        if (attachedNodes.Any(entry => entry.Node == node)) return;

        var renderer = listItem.TryGetListItemRenderer();
        if (renderer is null) return;

        var count  = renderer->CollisionNodeListCount;
        var buffer = (AtkResNode**)IMemorySpace.GetUISpace()->Realloc<nint>(renderer->CollisionNodeList, count + 1);

        for (var index = (int)count; index > 0; index--)
            buffer[index] = buffer[index - 1];

        buffer[0] = node;

        renderer->CollisionNodeList = buffer;
        renderer->CollisionNodeListCount++;

        attachedNodes.Add(new ListNodeEntry(node, renderer));
    }

    /// <summary>
    ///     Removes and disposes a node owned by this controller.
    /// </summary>
    /// <param name="node">Node to remove.</param>
    /// <remarks>
    ///     This must be invoked from the main game thread.
    /// </remarks>
    public unsafe void RemoveNode
    (
        NodeBase node
    )
    {
        if (attachedNodes.FirstOrDefault(entry => entry.Node == node) is not { } listEntry) return;

        var renderer = listEntry.Renderer;

        for (var index = 0; index < renderer->CollisionNodeListCount; index++)
        {
            if (renderer->CollisionNodeList[index] != node.ResNode) continue;

            renderer->CollisionNodeListCount--;

            for (var next = index; next < renderer->CollisionNodeListCount; next++)
                renderer->CollisionNodeList[next] = renderer->CollisionNodeList[next + 1];

            renderer->CollisionNodeList[renderer->CollisionNodeListCount] = null;
            break;
        }

        if (renderer->EventNode == node.ResNode)
            renderer->EventNode = null;

        node.Dispose();

        attachedNodes.Remove(listEntry);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
        => await DisableAsync();

    /// <inheritdoc />
    public void Dispose()
        => Disable();

    /// <summary>
    ///     Enables this native list controller.
    /// </summary>
    /// <remarks>
    ///     Warning, it can't properly track modified state if the list is already opened when the controller is enabled.
    ///     This must be invoked from the main game thread.
    /// </remarks>
    public unsafe void Enable()
    {

        IAddonLifecycle.Get().RegisterListener(AddonEvent.PostSetup,   AddonName, OnAddonSetup);
        IAddonLifecycle.Get().RegisterListener(AddonEvent.PreFinalize, AddonName, OnAddonFinalize);

        var addon = (T*)RaptureAtkUnitManager.Instance()->GetAddonByName(AddonName);

        if (addon is not null)
        {
            IPluginLog.Get().Warning("Caution: ListController was loaded after list was initialized, data may be stale.");
            LoadPopulators(addon);
        }
    }

    /// <summary>
    ///     Enables this native list controller.
    /// </summary>
    public async Task EnableAsync()
    {
        IAddonLifecycle.Get().RegisterListener(AddonEvent.PostSetup,   AddonName, OnAddonSetup);
        IAddonLifecycle.Get().RegisterListener(AddonEvent.PreFinalize, AddonName, OnAddonFinalize);

        await IFramework.Get().Run
        (() =>
            {
                unsafe
                {
                    var addon = (T*)RaptureAtkUnitManager.Instance()->GetAddonByName(AddonName);

                    if (addon is not null)
                    {
                        IPluginLog.Get().Warning("Caution: ListController was loaded after list was initialized, data may be stale.");
                        LoadPopulators(addon);
                    }
                }
            }
        );
    }

    /// <summary>
    ///     Disables this native list controller.
    /// </summary>
    /// <remarks>
    ///     This must be invoked from the main game thread.
    /// </remarks>
    public void Disable()
    {

        IAddonLifecycle.Get().UnregisterListener(OnAddonSetup, OnAddonFinalize);

        RemoveNodes();

        onListPopulate?.Dispose();
        onListPopulate = null;

        onRendererPopulate?.Dispose();
        onRendererPopulate = null;
    }

    /// <summary>
    ///     Disables this native list controller.
    /// </summary>
    public async Task DisableAsync()
    {
        IAddonLifecycle.Get().UnregisterListener(OnAddonSetup, OnAddonFinalize);

        await IFramework.Get().Run(RemoveNodes);

        await onListPopulate.DisposeAsync();
        onListPopulate = null;

        await onRendererPopulate.DisposeAsync();
        onRendererPopulate = null;
    }

    private unsafe void OnAddonSetup
    (
        AddonEvent type,
        AddonArgs  args
    )
        => LoadPopulators((T*)args.Addon.Address);

    private void OnAddonFinalize
    (
        AddonEvent type,
        AddonArgs  args
    )
    {
        onListPopulate?.Disable();
        onRendererPopulate?.Disable();

        RemoveNodes();

        ModifiedIndexes.Clear();
    }

    private unsafe void LoadPopulators
    (
        T* addon
    )
    {
        var populateMethod = GetPopulatorNode(addon)->Populator;

        if (populateMethod.Populate is not null)
        {
            onListPopulate ??= IGameInteropProvider.Get().HookFromAddress<AtkComponentListItemPopulator.PopulateDelegate>(populateMethod.Populate, OnPopulateDetour);
            onListPopulate?.Enable();
        }

        if (populateMethod.PopulateWithRenderer is not null)
        {
            onRendererPopulate ??= IGameInteropProvider.Get().HookFromAddress<AtkComponentListItemPopulator.PopulateWithRendererDelegate>
                (populateMethod.PopulateWithRenderer, OnRendererPopulateDetour);
            onRendererPopulate?.Enable();
        }
    }

    private unsafe void OnPopulateDetour
    (
        AtkEventListener*                           unitBase,
        AtkComponentListItemPopulator.ListItemInfo* itemInfo,
        AtkResNode**                                nodeList
    )
    {
        try
        {
            var listItemNode = itemInfo->ListItem->Renderer->OwnerNode;

            var parentAddon = RaptureAtkUnitManager.Instance()->GetAddonByNode((AtkResNode*)listItemNode);

            if (parentAddon is null || parentAddon->NameString != AddonName)
            {
                onListPopulate!.Original(unitBase, itemInfo, nodeList);
                return;
            }

            var listItemData = new TU
            {
                ItemInfo  = itemInfo,
                NodeList  = nodeList,
                ItemIndex = itemInfo->ListItemIndex,
                NodeId    = itemInfo->ListItem->Renderer->OwnerNode->NodeId
            };

            var shouldModifyElement = false;

            try
            {
                shouldModifyElement = ShouldModifyElement?.Invoke((T*)unitBase, listItemData) ?? true;
            }
            catch (Exception e)
            {
                IPluginLog.Get().Exception(e);
            }

            if (!shouldModifyElement)
            {
                if (ModifiedIndexes.Contains(listItemData.NodeId))
                {
                    try
                    {
                        ResetElement?.Invoke((T*)unitBase, listItemData);
                    }
                    catch (Exception e)
                    {
                        IPluginLog.Get().Exception(e);
                    }

                    ModifiedIndexes.Remove(listItemData.NodeId);
                }
            }

            onListPopulate!.Original(unitBase, itemInfo, nodeList);

            if (shouldModifyElement)
            {
                try
                {
                    UpdateElement?.Invoke((T*)unitBase, listItemData);
                }
                catch (Exception e)
                {
                    IPluginLog.Get().Exception(e);
                }

                ModifiedIndexes.Add(listItemData.NodeId);
            }
        }
        catch (Exception e)
        {
            IPluginLog.Get().Exception(e);
        }
    }

    private unsafe void OnRendererPopulateDetour
    (
        AtkEventListener*             unitBase,
        int                           listItemIndex,
        AtkResNode**                  nodeList,
        AtkComponentListItemRenderer* listItemRenderer
    )
    {
        try
        {
            var listItemNode = listItemRenderer->OwnerNode;

            var parentAddon = RaptureAtkUnitManager.Instance()->GetAddonByNode((AtkResNode*)listItemNode);

            if (parentAddon is null || parentAddon->NameString != AddonName)
            {
                onRendererPopulate!.Original(unitBase, listItemIndex, nodeList, listItemRenderer);
                return;
            }

            var listItemData = new TU
            {
                ItemRenderer = listItemRenderer,
                NodeList     = nodeList,
                ItemIndex    = listItemIndex,
                NodeId       = listItemRenderer->OwnerNode->NodeId
            };

            var shouldModifyElement = false;

            try
            {
                shouldModifyElement = ShouldModifyElement?.Invoke((T*)unitBase, listItemData) ?? true;
            }
            catch (Exception e)
            {
                IPluginLog.Get().Exception(e);
            }

            if (!shouldModifyElement)
            {
                if (ModifiedIndexes.Contains(listItemData.NodeId))
                {
                    try
                    {
                        ResetElement?.Invoke((T*)unitBase, listItemData);
                    }
                    catch (Exception e)
                    {
                        IPluginLog.Get().Exception(e);
                    }

                    ModifiedIndexes.Remove(listItemData.NodeId);
                }
            }

            onRendererPopulate!.Original(unitBase, listItemIndex, nodeList, listItemRenderer);

            if (shouldModifyElement)
            {
                try
                {
                    UpdateElement?.Invoke((T*)unitBase, listItemData);
                }
                catch (Exception e)
                {
                    IPluginLog.Get().Exception(e);
                }

                ModifiedIndexes.Add(listItemData.NodeId);
            }
        }
        catch (Exception e)
        {
            IPluginLog.Get().Exception(e);
        }
    }

    private void RemoveNodes()
    {
        foreach (var entry in attachedNodes.ToArray())
            RemoveNode(entry.Node);
    }
}
