using System.Globalization;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;
using Sws2Nightvision.Services;

namespace Sws2Nightvision;

/// <summary>
/// Player commands for toggling and configuring night vision.
/// </summary>
public partial class Sws2Nightvision
{
    [Command("nv", true, "", "Toggle night vision (!nv on|off|toggle)")]
    [CommandAlias("nightvision", registerRaw: true)]
    public void NightvisionCommand(ICommandContext context)
    {
        if (!TryGetCommandPlayer(context, out var player, out var localizer))
        {
            return;
        }

        var mode = context.Args.Length > 0 ? context.Args[0].Trim().ToLowerInvariant() : "toggle";
        if (mode.Length == 0)
        {
            mode = "toggle";
        }

        bool? desired = mode switch
        {
            "toggle" => null,
            "on" or "1" or "enable" or "true" => true,
            "off" or "0" or "disable" or "false" => false,
            _ => null
        };

        if (mode != "toggle" && desired is null)
        {
            Reply(context, localizer, "nightvision.usage");
            return;
        }

        var result = desired.HasValue
            ? _manager.SetEnabled(player, desired.Value)
            : _manager.Toggle(player);

        Core.Logger.LogInformation(
            "[sws2-nightvision] !nv from {PlayerName} ({PlayerId}) -> {Result}",
            player.Name,
            player.PlayerID,
            result);

        switch (result)
        {
            case NightvisionToggleResult.Enabled:
                if (_config.Behavior.NotifyOnToggle)
                {
                    Reply(context, localizer, "nightvision.on", _manager.GetIntensity(player));
                }

                if (IsPostProcessingDisabled(player))
                {
                    Reply(context, localizer, "nightvision.postprocess_disabled");
                }

                break;

            case NightvisionToggleResult.Disabled:
                if (_config.Behavior.NotifyOnToggle)
                {
                    Reply(context, localizer, "nightvision.off");
                }

                break;

            case NightvisionToggleResult.FeatureDisabled:
                Reply(context, localizer, "nightvision.feature_disabled");
                break;

            case NightvisionToggleResult.SpectatorDisabled:
                Reply(context, localizer, "nightvision.spectator_disabled");
                break;

            case NightvisionToggleResult.Unavailable:
                Reply(context, localizer, "nightvision.unavailable");
                break;

            case NightvisionToggleResult.Failed:
                Reply(context, localizer, "nightvision.failed");
                break;
        }
    }

    [Command("nvi", true, "", "Set night vision intensity")]
    [CommandAlias("nvintensity", registerRaw: true)]
    public void NightvisionIntensityCommand(ICommandContext context)
    {
        if (!TryGetCommandPlayer(context, out var player, out var localizer))
        {
            return;
        }

        if (!_config.Behavior.AllowIntensityCommand)
        {
            Reply(context, localizer, "nightvision.intensity.disabled");
            return;
        }

        var permission = _config.Behavior.IntensityPermission;
        if (!string.IsNullOrWhiteSpace(permission)
            && !Core.Permission.PlayerHasPermission(player.SteamID, permission))
        {
            Reply(context, localizer, "nightvision.no_permission");
            return;
        }

        if (context.Args.Length == 0)
        {
            Reply(
                context,
                localizer,
                "nightvision.intensity.current",
                _manager.GetIntensity(player),
                _manager.MinIntensity,
                _manager.MaxIntensity);
            return;
        }

        var argument = context.Args[0];
        if (!TryParseFloat(argument, out var requested) || !float.IsFinite(requested))
        {
            Reply(
                context,
                localizer,
                "nightvision.intensity.invalid",
                _manager.MinIntensity,
                _manager.MaxIntensity);
            return;
        }

        var applied = _manager.SetIntensity(player, requested);
        Reply(context, localizer, "nightvision.intensity.set", applied);
    }

    [Command("nvdebug", true, "", "Show night vision diagnostics")]
    public void NightvisionDebugCommand(ICommandContext context)
    {
        if (!TryGetCommandPlayer(context, out var player, out var localizer))
        {
            return;
        }

        var diagnostics = _manager.GetDiagnostics(player);
        Core.Logger.LogInformation(
            "[sws2-nightvision] Debug for {PlayerName} ({PlayerId}): {Diagnostics}",
            player.Name,
            player.PlayerID,
            diagnostics);

        player.SendConsole($"[sws2-nightvision] {diagnostics}");
        Reply(context, localizer, "nightvision.debug.sent");
    }

    [Command("nvstatus", true, "", "Show night vision status")]
    public void NightvisionStatusCommand(ICommandContext context)
    {
        if (!TryGetCommandPlayer(context, out var player, out var localizer))
        {
            return;
        }

        if (!_config.Behavior.Enabled)
        {
            Reply(context, localizer, "nightvision.feature_disabled");
            return;
        }

        var status = _manager.GetStatus(player);

        if (status.Enabled && !status.Active)
        {
            Reply(context, localizer, "nightvision.status.pending", status.Intensity);
        }
        else if (status.Enabled)
        {
            Reply(context, localizer, "nightvision.status.on", status.Intensity);
        }
        else
        {
            Reply(context, localizer, "nightvision.status.off", status.Intensity);
        }
    }

    private bool TryGetCommandPlayer(
        ICommandContext context,
        out IPlayer player,
        out ILocalizer localizer)
    {
        localizer = context.Sender is not null
            ? Core.Translation.GetPlayerLocalizer(context.Sender)
            : Core.Localizer;

        if (!context.IsSentByPlayer || context.Sender is null)
        {
            player = null!;
            context.Reply($"{localizer["nightvision.prefix"]} {localizer["nightvision.only_players"]}");
            return false;
        }

        player = context.Sender;
        return true;
    }

    private static bool IsPostProcessingDisabled(IPlayer player)
    {
        try
        {
            return player.GetClientConvarValue("mat_postprocess_enable") == "0";
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseFloat(string input, out float value)
    {
        return float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static void Reply(
        ICommandContext context,
        ILocalizer localizer,
        string key,
        params object[] args)
    {
        var message = args.Length == 0 ? localizer[key] : localizer[key, args];
        context.Reply($"{localizer["nightvision.prefix"]} {message}");
    }
}
