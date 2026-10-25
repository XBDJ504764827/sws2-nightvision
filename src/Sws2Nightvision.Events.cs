using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace Sws2Nightvision;

/// <summary>
/// Event wiring for cleanup and automatic preference restoration.
/// </summary>
public partial class Sws2Nightvision
{
    private void OnClientConnected(IOnClientConnectedEvent @event)
    {
        var playerId = @event.PlayerId;
        _manager.BlockActiveForPlayer(playerId);

        // The player object may not be available yet during this event.
        Core.Scheduler.NextTick(() =>
        {
            if (_loaded)
            {
                _manager.BlockActiveForPlayer(playerId);
            }
        });
    }

    private void OnClientPutInServer(IOnClientPutInServerEvent @event)
    {
        _manager.BlockActiveForPlayer(@event.PlayerId);
    }

    private void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        _manager.CleanupPlayer(@event.PlayerId);
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
        _manager.CleanupActive("map_unload");
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event)
    {
        if (!_config.Behavior.Enabled || !_config.Behavior.RestorePreferencesOnSpawn)
        {
            return HookResult.Continue;
        }

        var player = @event.UserIdPlayer;
        if (player is null)
        {
            return HookResult.Continue;
        }

        var playerId = player.PlayerID;
        _manager.BlockActiveForPlayer(playerId);

        // The pawn is not fully ready during the event itself.
        Core.Scheduler.NextTick(() =>
        {
            if (_loaded)
            {
                _manager.RestoreForPlayer(playerId);
            }
        });

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event)
    {
        if (!_config.Behavior.TurnOffOnDeath)
        {
            return HookResult.Continue;
        }

        var player = @event.UserIdPlayer;
        if (player is not null)
        {
            _manager.DisableForPlayer(player.PlayerID, clearPreference: true, reason: "death");
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event)
    {
        // Ignore the initial team join, team restores and disconnect-triggered
        // changes so a map change does not clear saved preferences.
        if (@event.Disconnect
            || @event.OldTeam == 0
            || @event.Team == 0
            || @event.Team == @event.OldTeam)
        {
            return HookResult.Continue;
        }

        var player = @event.UserIdPlayer;
        if (player is null)
        {
            return HookResult.Continue;
        }

        var joinedSpectator = @event.Team == (byte)Team.Spectator;

        if (_config.Behavior.TurnOffOnTeamSwitch)
        {
            _manager.DisableForPlayer(player.PlayerID, clearPreference: true, reason: "team_switch");
        }
        else if (joinedSpectator && _config.Behavior.DisableInSpectator)
        {
            // Keep the saved preference so it can be restored when the player
            // returns to a playing team.
            _manager.DisableForPlayer(player.PlayerID, clearPreference: false, reason: "spectator");
        }

        return HookResult.Continue;
    }
}
