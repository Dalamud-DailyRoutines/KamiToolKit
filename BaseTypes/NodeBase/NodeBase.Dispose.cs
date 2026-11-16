using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Internal.Classes;

namespace KamiToolKit.BaseTypes;

/// <summary>
///     Abstract base class for all nodes used in KamiToolKit.
/// </summary>
public abstract unsafe partial class NodeBase : IDisposable
{
    internal const uint NODE_ID_BASE = 100_000_000;

    internal static uint CurrentOffset;

    private static int logIndent = -1;

    internal bool IsAddonRootNode;

    private AtkResNode.Delegates.Destroy destroyFunction = null!;

    private DisposeState                       disposeState;
    private AtkResNode.AtkResNodeVirtualTable* modifiedVirtualTable;

    private AtkResNode.AtkResNodeVirtualTable* originalVirtualTable;

    /// <summary>
    ///     Gets the list of all allocated nodes for this KamiToolKit instance.
    /// </summary>
    internal static List<NodeBase> CreatedNodes { get; } = [];

    /// <summary>
    ///     Indicates whether this instance has been disposed or is in the process of being disposed.
    /// </summary>
    protected bool IsDisposed => disposeState is not DisposeState.Alive;

    public abstract AtkResNode* ResNode { get; }

    /// <summary>
    ///     When true, enables hyper verbose node disposal logging.
    /// </summary>
    private static bool EnableFullLogging => false;

    /// <summary>
    ///     Disposes this instance. Has double dispose guards.
    /// </summary>
    /// <remarks>
    ///     Must be invoked from the main game thread.
    /// </remarks>
    public void Dispose()
    {
        if (disposeState is not DisposeState.Alive) return;

        try
        {
            logIndent++;
            LogIndented($"Beginning Dispose for {GetType()}", true);
            logIndent++;

            if (IFramework.Get().IsFrameworkUnloading)
            {
                LogIndented("Game is shutting down, aborting manual dispose.", EnableFullLogging);
                return;
            }

            disposeState = DisposeState.Disposing;

            if (!IsNodeValid())
            {
                IPluginLog.Get().Warning("Invalid node, dispose aborted.");
                return;
            }

            LogIndented("Disposing Tooltip Events", EnableFullLogging);
            UnregisterTooltipEvents();

            LogIndented("Clearing Native Focus", EnableFullLogging);
            AtkStage.Instance()->ClearNodeFocus(ResNode);

            LogIndented("Detaching From UI", EnableFullLogging);
            DetachNode();

            LogIndented("Disposing Timeline", EnableFullLogging);
            var timeline = Timeline;
            Timeline          = null;
            ResNode->Timeline = null;

            try
            {
                timeline?.Dispose();
            }
            catch (Exception e)
            {
                IPluginLog.Get().Exception(e);
            }

            LogIndented("Invoking Native Dispose", EnableFullLogging);
            Dispose(true, false);
        }
        catch (Exception e)
        {
            IPluginLog.Get().Exception(e);
        }
        finally
        {
            if (disposeState is DisposeState.Disposing)
                disposeState = DisposeState.Disposed;
            GC.SuppressFinalize(this);
            logIndent--;
            LogIndented("Dispose Complete", true);
            logIndent--;
        }
    }

    /// <summary>
    ///     Implicit operator to convert this instance to a AtkResNode* for cleaner interop.
    /// </summary>
    public static implicit operator AtkResNode*
    (
        NodeBase node
    ) => node.ResNode;

    /// <summary>
    ///     Implicit operator to convert this instance to a AtkEventTarget* for cleaner interop.
    /// </summary>
    public static implicit operator AtkEventTarget*
    (
        NodeBase node
    ) => &node.ResNode->AtkEventTarget;

    /// <summary>
    ///     Implicit operator to convert this instance to a nint node pointer.
    /// </summary>
    public static implicit operator nint
    (
        NodeBase node
    ) => (nint)node.ResNode;

    private static void LogIndented
    (
        string message,
        bool   enableLogging
    )
    {
        if (!enableLogging) return;

        IPluginLog.Get().Verbose(new string(' ', logIndent * 2) + message);
    }

    internal static void WarnLeakedNodes()
    {
        var leakedNodeCount = CreatedNodes.Count(node => !node.IsAddonRootNode && node.ResNode is not null && node.ResNode->ParentNode is null);

        if (leakedNodeCount is not 0)
            IPluginLog.Get().Warning($"There were {leakedNodeCount} node(s) that were not disposed safely.");

        foreach (var node in CreatedNodes.ToArray())
        {
            if (node.ResNode is null) continue;
            if (node.ResNode->ParentNode is not null) continue;
            if (node.IsAddonRootNode) continue;

            IPluginLog.Get().Warning($"Forcing disposal of: {node.GetType()}");
        }
    }

    /// <summary>
    ///     Warning, this is only to ensure there are no memory leaks.
    ///     Ensure you have detached nodes safely from native ui before disposing.
    /// </summary>
    internal static void DisposeNodes()
    {
        foreach (var node in CreatedNodes.ToArray())
        {
            if (node.ResNode is null) continue;
            if (node.ResNode->ParentNode is not null) continue;
            if (node.IsAddonRootNode) continue;

            try
            {
                node.Dispose();
            }
            catch (Exception e)
            {
                IPluginLog.Get().Exception(e);
            }
        }
    }

    internal static void RestoreAllNodeVirtualTables()
    {
        foreach (var node in CreatedNodes.ToArray())
        {
            try
            {
                node.RestoreNodeVirtualTable();
            }
            catch (Exception e)
            {
                IPluginLog.Get().Exception(e);
            }
        }
    }

    /// <summary>
    ///     释放本节点持有的资源，并清除托管归属关系。
    /// </summary>
    /// <param name="disposing">是否释放节点资源。</param>
    /// <param name="isNativeDestructor">
    ///     是否由原生节点析构回调触发。为真时，原生 ULD 正在遍历所属节点表，不得摘除节点、修改节点表或递归销毁子节点；
    ///     本节点自行分配的资源仍由本节点释放。组件内部节点由组件反初始化时的 ULD 最终释放流程销毁。
    /// </param>
    protected virtual void Dispose
    (
        bool disposing,
        bool isNativeDestructor
    )
    {

        UnregisterTooltipEvents();
        DisposeEvents();

        if (isNativeDestructor)
            overlayNode = null;

        DisableEditMode(NodeEditMode.Move | NodeEditMode.Resize);

        if (!isNativeDestructor)
        {
            foreach (var child in ChildNodes.ToArray())
            {
                child.SuppressNativeUpdate = true;
                child.Dispose();
            }
        }

        ChildNodes.Clear();
        RemoveParentNodeReferences();
        ParentUldManager = null;
        ParentAddon      = null;
    }

    private bool IsNodeValid()
    {
        if (ResNode is null) return false;
        if (ResNode->VirtualTable is null) return false;
        return ResNode->VirtualTable != AtkEventTarget.StaticVirtualTablePointer;
    }

    /// <summary>
    ///     Replaces the nodes entire virtual table to ensure that C#'s managed space gets notified of the games unmanaged node
    ///     dtor.
    /// </summary>
    protected void BuildVirtualTable()
    {
        // Back up original destructor pointer
        originalVirtualTable = ResNode->VirtualTable;

        // Overwrite virtual table with a custom copy,
        // Note: Currently there are only 2 virtual functions, but there's no harm in copying more for if they ever add more vfuncs to the game.
        modifiedVirtualTable = (AtkResNode.AtkResNodeVirtualTable*)IMemorySpace.GetUISpace()->AllocateZeroedArray<nint>(4);
        NativeMemory.Copy(ResNode->VirtualTable, modifiedVirtualTable, 0x8 * 4);
        ResNode->VirtualTable = modifiedVirtualTable;

        // Pin managed function to virtual table entry
        destroyFunction = Destroy;

        // Replace native destructor with
        modifiedVirtualTable->Destroy = (delegate* unmanaged<AtkResNode*, bool, void>)Marshal.GetFunctionPointerForDelegate(destroyFunction);
    }

    internal virtual void RestoreNodeVirtualTable()
    {
        if (modifiedVirtualTable is null) return;
        if (ResNode is null) return;

        var timeline = Timeline;
        Timeline          = null;
        ResNode->Timeline = null;

        try
        {
            timeline?.Dispose();
        }
        catch (Exception e)
        {
            IPluginLog.Get().Exception(e);
        }

        ResNode->VirtualTable = originalVirtualTable;

        NativeMemoryHelper.Free(modifiedVirtualTable, 0x8 * 4);
        modifiedVirtualTable = null;
    }

    /// <summary>
    ///     Pinned managed function that is used to replace the native virtual tables dtor function pointer.
    /// </summary>
    protected void Destroy
    (
        AtkResNode* thisPtr,
        bool        free
    )
    {
        if (disposeState is not DisposeState.Alive) return;
        disposeState = DisposeState.Disposing;

        var timeline = Timeline;
        Timeline          = null;
        thisPtr->Timeline = null;

        try
        {
            timeline?.Dispose();
        }
        catch (Exception e)
        {
            IPluginLog.Get().Exception(e);
        }

        try
        {
            Dispose(true, true);
        }
        catch (Exception e)
        {
            IPluginLog.Get().Exception(e);
        }
        finally
        {
            OriginalDestroy(thisPtr, free);
            disposeState = DisposeState.Disposed;
        }

        IPluginLog.Get().Verbose($"Native has disposed node {GetType()}");
    }

    // To be invoked from NodeBase.Dispose(bool, bool).
    /// <summary>
    ///     Invokes the original games destroy function without calling back to the native disposal method.
    /// </summary>
    /// <remarks>
    ///     This is intended to be used from <see cref="NodeBase" /> after the managed disposal functions have been invoked.
    /// </remarks>
    protected void OriginalDestroy
    (
        AtkResNode* thisPtr,
        bool        free
    )
    {
        thisPtr->VirtualTable = originalVirtualTable;

        originalVirtualTable->Destroy(thisPtr, free);

        if (modifiedVirtualTable is not null)
        {
            NativeMemoryHelper.Free(modifiedVirtualTable, 0x8 * 4);
            modifiedVirtualTable = null;
        }

        CreatedNodes.Remove(this);
    }

    private enum DisposeState : byte
    {
        Alive     = 0,
        Disposing = 1,
        Disposed  = 2
    }
}
