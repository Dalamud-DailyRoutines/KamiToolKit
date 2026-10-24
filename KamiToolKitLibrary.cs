using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Controllers;
using KamiToolKit.Debug;
using KamiToolKit.Internal.Classes;
using Serilog.Events;

namespace KamiToolKit;

/// <summary>
///     Primary entry point for KamiToolKit, contains initialization and disposal code.
/// </summary>
public static class KamiToolKitLibrary
{
    private const string NodeDataShareKey = "TypeMappedCustomNodes";

    internal static ConcurrentDictionary<nint, Type>? AllocatedNodes;

    internal static readonly List<NativeAddonController> AddonControllers = [];

    internal static ResourceManager? ResourceManager;
    internal static CultureInfo?     CurrentCulture;

    internal static AddonConfigFile? AddonConfigFile;

    private static bool          debugMode;
    private static WindowSystem? debugWindowSystem;
    private static DebugWindow?  debugWindow;

    /// <summary>
    ///     Gets the <see cref="IDalamudPluginInterface" /> used for KamiToolKit.
    ///     This can be accessed anytime after <see cref="InitializeAsync" /> has been called.
    /// </summary>
    public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    /// <summary>
    ///     Main initialization method for KamiToolKit. This method is required to be invoked before any KamiToolKit features
    ///     are used.
    ///     Failure to do so will not result in any direct warnings, but will result in undefined behavior.
    /// </summary>
    public static async Task InitializeAsync
    (
        IDalamudPluginInterface pluginInterface
    )
    {
        PluginInterface = pluginInterface;

        PluginInterface.Create<Services>();

        // Create node data share
        AllocatedNodes = PluginInterface.GetOrCreateData(NodeDataShareKey, () => new ConcurrentDictionary<nint, Type>());

        // Force enable Verbose so that users are able to get advanced logging information on request.
        IPluginLog.Get().MinimumLogLevel = LogEventLevel.Verbose;

        IPluginLog.Get().Info($"[KamiToolKit] 为插件“{PluginInterface.InternalName}”初始化成功。");

        AddonConfigFile = await LoadAddonConfigFileAsync();

        await IFramework.Get().Run(NativeAddon.InitializeCloseCallback);

        RegisterDebugHelpers();
    }

    /// <summary>
    ///     Sets the resource manager that KTK will use to resolve enums.
    /// </summary>
    public static void SetResourceManager
    (
        ResourceManager resourceManager
    ) =>
        ResourceManager = resourceManager;

    /// <summary>
    ///     Sets the current culture that KTK will use to resolve enums.
    /// </summary>
    public static void SetCurrentCulture
    (
        CultureInfo culture
    ) =>
        CurrentCulture = culture;

    /// <summary>
    ///     Loads or creates AddonConfigFile, and then tries to migrate any {internalName}.addon.json files to new version.
    /// </summary>
    private static async Task<AddonConfigFile> LoadAddonConfigFileAsync()
    {
        var configFile = await AddonConfigFile.LoadAsync();
        await configFile.TryMigrateOldConfigs();

        return configFile;
    }

    [Conditional("DEBUG")]
    private static void RegisterDebugHelpers()
    {
        debugMode = true;

        debugWindowSystem = new WindowSystem($"KamiToolKit - {PluginInterface.InternalName}");

        ICommandManager.Get().AddHandler
        (
            $"/ktkdebug_{PluginInterface.InternalName}",
            new CommandInfo(CommandHandler)
            {
                HelpMessage = "开启或关闭 KamiToolKit 调试界面。"
            }
        );

        PluginInterface.UiBuilder.Draw += debugWindowSystem.Draw;

        debugWindow = new DebugWindow();
        debugWindowSystem.AddWindow(debugWindow);
    }

    private static void CommandHandler
    (
        string command,
        string arguments
    )
    {
        if (command != $"/ktkdebug_{PluginInterface.InternalName}") return;

        debugWindow?.IsOpen = !debugWindow.IsOpen;
    }

    /// <summary>
    ///     Disposes KamiToolKit resources.
    /// </summary>
    public static void Dispose()
    {
        if (debugMode)
        {
            PluginInterface.UiBuilder.Draw -= debugWindowSystem!.Draw;
            ICommandManager.Get().RemoveHandler($"/ktkdebug_{PluginInterface.InternalName}");
            debugWindowSystem.RemoveAllWindows();
        }

        NativeAddon.DisposeCloseCallback();

        foreach (var controller in AddonControllers.ToArray())
            controller.Dispose();

        NodeBase.WarnLeakedNodes();
        NativeAddon.WarnLeakedAddons();

        NativeAddon.DisposeAddons();
        NodeBase.DisposeNodes();

        PluginInterface.RelinquishData(NodeDataShareKey);
    }

    /// <summary>
    ///     Disposes KamiToolkit resources async.
    /// </summary>
    public static async Task DisposeAsync()
    {
        var framework = IFramework.Get();

        await framework.Run
        (
            () =>
            {
                if (debugMode)
                {
                    PluginInterface.UiBuilder.Draw -= debugWindowSystem!.Draw;
                    ICommandManager.Get().RemoveHandler($"/ktkdebug_{PluginInterface.InternalName}");
                    debugWindowSystem.RemoveAllWindows();
                }

                NativeAddon.DisposeCloseCallback();
            }
        ).ConfigureAwait(false);

        var controllers = await framework.Run(() => AddonControllers.ToArray()).ConfigureAwait(false);
        await DisposeObjectsAsync(controllers, static controller => controller.Dispose()).ConfigureAwait(false);

        await framework.Run
        (
            () =>
            {
                NodeBase.WarnLeakedNodes();
                NativeAddon.WarnLeakedAddons();
            }
        ).ConfigureAwait(false);

        try
        {
            var addons = await framework.Run(() => NativeAddon.CreatedAddons.ToArray()).ConfigureAwait(false);

            await DisposeObjectsAsync
            (
                addons,
                static addon =>
                {
                    if (!addon.IsOverlayAddon)
                        addon.Dispose();
                }
            ).ConfigureAwait(false);

            await DisposeObjectsAsync(addons, static addon => addon.RestoreVirtualTable()).ConfigureAwait(false);
            await framework.Run(NativeAddon.CreatedAddons.Clear).ConfigureAwait(false);

            var nodes = await framework.Run(() => NodeBase.CreatedNodes.ToArray()).ConfigureAwait(false);
            await DisposeObjectsAsync(nodes, DisposeDetachedNode).ConfigureAwait(false);
        }
        finally
        {
            var nodes = await framework.Run(() => NodeBase.CreatedNodes.ToArray()).ConfigureAwait(false);
            await DisposeObjectsAsync(nodes, static node => node.RestoreNodeVirtualTable()).ConfigureAwait(false);
            await framework.Run(() => PluginInterface.RelinquishData(NodeDataShareKey)).ConfigureAwait(false);
        }
    }

    private static async Task DisposeObjectsAsync<T>
    (
        IReadOnlyList<T> objects,
        Action<T>        dispose
    )
    {
        var framework = IFramework.Get();
        var index     = 0;

        while (index < objects.Count)
        {
            await framework.Run
            (
                () =>
                {
                    var startedAt = Stopwatch.GetTimestamp();

                    do
                    {
                        try
                        {
                            dispose(objects[index++]);
                        }
                        catch (Exception ex)
                        {
                            IPluginLog.Get().Exception(ex);
                        }
                    }
                    while (index < objects.Count && Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds < 1);
                }
            ).ConfigureAwait(false);
        }
    }

    private static unsafe void DisposeDetachedNode
    (
        NodeBase node
    )
    {
        if (node.ResNode is null                 ||
            node.ResNode->ParentNode is not null ||
            node.IsAddonRootNode)
            return;

        node.Dispose();
    }
}
