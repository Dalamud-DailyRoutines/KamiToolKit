using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KamiToolKit.Internal.Classes;
using Serilog;

namespace KamiToolKit.BaseTypes;

public partial class NativeAddon : IDisposable, IAsyncDisposable {

    /// <summary>
    /// Triggers the disposal of this addon. <br/> <br/>
    /// This will not await the addon to actually close which happens several frames later
    /// due to the addons closing animation. If you need to fully wait for the window to close use <see cref="DisposeAsync"/> and await the result.
    /// </summary>
    /// <code>await thisInstance.DisposeAsync();</code>
    public virtual void Dispose() {
        if (IsOverlayAddon) {
            // Intentionally leak OverlayAddons,
            // until Dalamud can implement OverlayAddons globally.
            CreatedAddons.Remove(this);
            GC.SuppressFinalize(this);
            return;
        }

        if (!isDisposed) {
            Services.Log.Debug($"Disposing addon {GetType()}");

            Close();

            // Close will remove this node automatically on AtkUnitBase.Finalize,
            // However, this is after the plugin unloads,
            // and will trigger a warning in auto-dispose if we don't remove this now.
            CreatedAddons.Remove(this);

            GC.SuppressFinalize(this);
        }

        isDisposed = true;
    }

    /// <summary>
    /// Triggers the disposal of this addon, and awaits for it to fully close before returning <see cref="ValueTask.CompletedTask"/>
    /// </summary>
    /// <remarks>
    /// This <em>must not</em> be called from the main thread, or it will deadlock the game.
    /// </remarks>
    public virtual async ValueTask DisposeAsync() {
        if (IsOverlayAddon) {
            // Intentionally leak OverlayAddons,
            // until Dalamud can implement OverlayAddons globally.
            CreatedAddons.Remove(this);
            GC.SuppressFinalize(this);
            return;
        }

        if (!isDisposed) {
            Services.Log.Debug($"Disposing addon {GetType()}");

            await CloseAsync();

            CreatedAddons.Remove(this);

            GC.SuppressFinalize(this);
        }

        isDisposed = true;
    }

    internal static void WarnLeakedAddons() {
        foreach (var addon in CreatedAddons.ToArray()) {
            if (addon.IsOverlayAddon) continue;

            Services.Log.Warning($"Addon {addon.GetType()} was not disposed properly please ensure you call dispose at an appropriate time.");
            Services.Log.Debug($"Automatically disposing addon {addon.GetType()} as a safety measure.");
        }
    }

    internal static void DisposeAddons() {
        var addons = CreatedAddons.ToArray();

        foreach (var addon in addons) {
            if (addon.IsOverlayAddon) continue;

            addon.Dispose();
        }

        // 必须对所有 addon (包括 OverlayAddon) 恢复 vtable
        // OverlayAddon 虽然 intentionally leaked, 但其 modifiedVirtualTable 中的函数指针
        // 指向 KTK 的 managed delegate, 程序集卸载后这些 delegate 失效
        // Dalamud 的 AddonVirtualTable.OriginalVirtualTable 指向 KTK 的 modifiedVirtualTable
        // 如果不恢复, Dalamud 调用时会跳转到已卸载的 managed delegate 导致崩溃
        foreach (var addon in addons) {
            addon.RestoreVirtualTable();
        }

        CreatedAddons.Clear();
    }

    private static readonly List<NativeAddon> CreatedAddons = [];
    private bool isDisposed;
}
