using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Internal.Classes;

namespace KamiToolKit.BaseTypes;

public unsafe partial class NativeAddon {

    private const int VirtualTableEntryCount = 200;

    private AtkUnitBase.Delegates.Dtor destructorFunction = null!;
    private AtkUnitBase.Delegates.Draw drawFunction = null!;
    private AtkUnitBase.Delegates.Finalizer finalizerFunction = null!;
    private AtkUnitBase.Delegates.Hide hideFunction = null!;
    private AtkUnitBase.Delegates.Initialize initializeFunction = null!;
    private AtkUnitBase.Delegates.OnSetup onSetupFunction = null!;
    private AtkUnitBase.Delegates.Show showFunction = null!;
    private AtkUnitBase.Delegates.Hide2 softHideFunction = null!;
    private AtkUnitBase.Delegates.Update updateFunction = null!;
    private AtkUnitBase.Delegates.OnRequestedUpdate onRequestedUpdateFunction = null!;
    private AtkUnitBase.Delegates.OnRefresh onRefreshFunction = null!;
    private AtkUnitBase.Delegates.OnScreenSizeChange onScreenSizeChangedFunction = null!;

    private AtkUnitBase.AtkUnitBaseVirtualTable* modifiedVirtualTable;
    private AtkUnitBase.AtkUnitBaseVirtualTable* originalVirtualTable;

    private void RegisterVirtualTable() {

        originalVirtualTable = InternalAddon->VirtualTable;

        // Overwrite virtual table with a custom copy,
        // Note: currently there are 73 vfuncs, but there's no harm in copying more for when they add new vfuncs to the game
        modifiedVirtualTable = (AtkUnitBase.AtkUnitBaseVirtualTable*)NativeMemoryHelper.Malloc(0x8 * VirtualTableEntryCount);
        NativeMemory.Copy(InternalAddon->VirtualTable, modifiedVirtualTable, 0x8 * VirtualTableEntryCount);
        InternalAddon->VirtualTable = modifiedVirtualTable;

        initializeFunction = Initialize;
        onSetupFunction = Setup;
        showFunction = Show;
        updateFunction = Update;
        drawFunction = Draw;
        hideFunction = Hide;
        softHideFunction = Hide2;
        finalizerFunction = Finalizer;
        destructorFunction = Destructor;
        onRequestedUpdateFunction = RequestedUpdate;
        onRefreshFunction = Refresh;
        onScreenSizeChangedFunction = ScreenSizeChange;

        modifiedVirtualTable->Initialize = (delegate* unmanaged<AtkUnitBase*, void>)Marshal.GetFunctionPointerForDelegate(initializeFunction);
        modifiedVirtualTable->OnSetup = (delegate* unmanaged<AtkUnitBase*, uint, AtkValue*, void>)Marshal.GetFunctionPointerForDelegate(onSetupFunction);
        modifiedVirtualTable->Show = (delegate* unmanaged<AtkUnitBase*, bool, uint, void>)Marshal.GetFunctionPointerForDelegate(showFunction);
        modifiedVirtualTable->Update = (delegate* unmanaged<AtkUnitBase*, float, void>)Marshal.GetFunctionPointerForDelegate(updateFunction);
        modifiedVirtualTable->Draw = (delegate* unmanaged<AtkUnitBase*, void>)Marshal.GetFunctionPointerForDelegate(drawFunction);
        modifiedVirtualTable->Hide = (delegate* unmanaged<AtkUnitBase*, bool, bool, uint, void>)Marshal.GetFunctionPointerForDelegate(hideFunction);
        modifiedVirtualTable->Hide2 = (delegate* unmanaged<AtkUnitBase*, void>)Marshal.GetFunctionPointerForDelegate(softHideFunction);
        modifiedVirtualTable->Finalizer = (delegate* unmanaged<AtkUnitBase*, void>)Marshal.GetFunctionPointerForDelegate(finalizerFunction);
        modifiedVirtualTable->Dtor = (delegate* unmanaged<AtkUnitBase*, byte, AtkEventListener*>)Marshal.GetFunctionPointerForDelegate(destructorFunction);
        modifiedVirtualTable->OnRequestedUpdate = (delegate* unmanaged<AtkUnitBase*, NumberArrayData**, StringArrayData**, void>)Marshal.GetFunctionPointerForDelegate(onRequestedUpdateFunction);
        modifiedVirtualTable->OnRefresh = (delegate* unmanaged<AtkUnitBase*, uint, AtkValue*, bool>)Marshal.GetFunctionPointerForDelegate(onRefreshFunction);
        modifiedVirtualTable->OnScreenSizeChange = (delegate* unmanaged<AtkUnitBase*, int, int, void>)Marshal.GetFunctionPointerForDelegate(onScreenSizeChangedFunction);
    }

    internal void RestoreVirtualTable() {
        if (InternalAddon is null) return;
        if (modifiedVirtualTable is null) return;

        // 将 modifiedVirtualTable 中的所有函数指针恢复为 originalVirtualTable 的内容
        // Dalamud 的 AddonVirtualTable.OriginalVirtualTable 可能指向我们的 modifiedVirtualTable
        // 程序集卸载后 managed delegate 失效, 必须将函数指针恢复为原始 native 函数
        // 否则 Dalamud 通过 OriginalVirtualTable 调用会跳转到已卸载的 managed delegate 导致崩溃
        NativeMemory.Copy(originalVirtualTable, modifiedVirtualTable, 0x8 * VirtualTableEntryCount);

        // 仅当 addon 当前仍使用我们的 modifiedVirtualTable 时才恢复
        // 如果 Dalamud 已替换为它自己的 vtable, 不要覆盖, 让 Dalamud 正常管理
        if (InternalAddon->VirtualTable == modifiedVirtualTable) {
            InternalAddon->VirtualTable = originalVirtualTable;
        }

        // 不释放 modifiedVirtualTable, 因为 Dalamud 的 OriginalVirtualTable 可能指向它
        // 释放会导致 Dalamud 持有悬空指针, 后续调用时崩溃
        // 此处造成的少量内存泄漏仅插件重载时发生, 可接受
        modifiedVirtualTable = null;

        disposeHandle?.Dispose();
        disposeHandle = null;

        InternalAddon = null;
    }
}
