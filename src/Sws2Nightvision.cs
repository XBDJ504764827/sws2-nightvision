using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Plugins;
using Sws2Nightvision.Configuration;
using Sws2Nightvision.Services;

namespace Sws2Nightvision;

/// <summary>
/// Main plugin entry point. Plugin metadata lives in <c>Metadata.cs</c>.
/// </summary>
public partial class Sws2Nightvision : BasePlugin
{
    private NightvisionConfig _config = new();
    private NightvisionManager _manager = null!;
    private ServiceProvider _provider = null!;

    private volatile bool _loaded;

    public Sws2Nightvision(ISwiftlyCore core) : base(core)
    {
    }

    public override void Load(bool hotReload)
    {
        // 1. Initialize config.jsonc from the bundled template on first run and
        //    watch it for changes.
        Core.Configuration
            .InitializeWithTemplate("config.jsonc", "config.template.jsonc")
            .Configure(builder =>
            {
                builder.AddJsonFile("config.jsonc", optional: false, reloadOnChange: true);
            });

        // 2. Bind the config through DI so IOptionsMonitor can detect hot reloads.
        var services = new ServiceCollection();
        services.AddSwiftly(Core)
            .AddOptionsWithValidateOnStart<NightvisionConfig>()
            .BindConfiguration("sws2-nightvision");

        _provider = services.BuildServiceProvider();
        var options = _provider.GetRequiredService<IOptionsMonitor<NightvisionConfig>>();
        _config = options.CurrentValue;

        // 3. Load saved player preferences and create the manager.
        var preferences = new PreferenceStore(
            Core,
            Core.Configuration.GetConfigPath("preferences.json"),
            _config.Behavior.SavePreferences);
        preferences.Load();

        _manager = new NightvisionManager(Core, _config, preferences);
        _manager.Start();

        options.OnChange(newConfig =>
        {
            Core.Scheduler.NextTick(() =>
            {
                if (!_loaded)
                {
                    return;
                }

                _config = newConfig;
                _manager.ApplyConfig(newConfig);

                Core.Logger.LogInformation(
                    "[sws2-nightvision] Configuration reloaded (enabled: {Enabled}, default intensity: {Intensity})",
                    newConfig.Behavior.Enabled,
                    newConfig.Behavior.DefaultIntensity);
            });
        });

        // 4. Runtime events and game events.
        Core.Event.OnClientConnected += OnClientConnected;
        Core.Event.OnClientPutInServer += OnClientPutInServer;
        Core.Event.OnClientDisconnected += OnClientDisconnected;
        Core.Event.OnMapUnload += OnMapUnload;

        Core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
        Core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath);
        Core.GameEvent.HookPost<EventPlayerTeam>(OnPlayerTeam);

        _loaded = true;

        // 5. Restore preferences for players already on the server (mid-map load
        //    or hot reload).
        Core.Scheduler.NextTick(() =>
        {
            if (_loaded)
            {
                _manager.RestoreAll();
            }
        });

        Core.Logger.LogInformation("[sws2-nightvision] Plugin loaded (hotReload: {HotReload})", hotReload);
    }

    public override void Unload()
    {
        _loaded = false;

        Core.Event.OnClientConnected -= OnClientConnected;
        Core.Event.OnClientPutInServer -= OnClientPutInServer;
        Core.Event.OnClientDisconnected -= OnClientDisconnected;
        Core.Event.OnMapUnload -= OnMapUnload;

        Core.GameEvent.UnhookPost<EventPlayerSpawn>();
        Core.GameEvent.UnhookPost<EventPlayerDeath>();
        Core.GameEvent.UnhookPost<EventPlayerTeam>();

        _manager?.Shutdown();
        _provider?.Dispose();
    }
}
