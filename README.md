<div align="center">
  <h2><strong>SWS2 Nightvision</strong></h2>
  <h3>Per-player night vision for SwiftlyS2.</h3>
</div>

<p align="center">
  <img src="https://img.shields.io/github/downloads/XBDJ504764827/sws2-nightvision/total" alt="Downloads">
  <img src="https://img.shields.io/github/v/release/XBDJ504764827/sws2-nightvision" alt="Release">
  <img src="https://img.shields.io/github/license/XBDJ504764827/sws2-nightvision" alt="License">
</p>

## Features

- Per-player night vision powered by a `post_processing_volume` with exposure control
- Owner-only transmission: only the player who enabled it receives the effect
- Toggle with `!nv` / `!nightvision`, or explicitly with `!nv on` / `!nv off`
- Adjustable intensity with `!nvi` / `!nvintensity`
- Per-player preferences saved to `preferences.json` and restored on spawn
- Hot-reloadable `config.jsonc`
- Automatic cleanup on death, team switch, disconnect and map change (configurable)
- No client mods and no external plugin dependencies
- Translations: English, 简体中文, 繁體中文

## Requirements

- [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2) running on a CS2 dedicated server
- .NET 10 SDK (only needed to build from source)

## Installation

1. Download the latest release zip.
2. Extract the folder into `csgo/addons/swiftlys2/plugins/`:

```
csgo/addons/swiftlys2/plugins/
└── sws2-nightvision/
```

3. Start or reload the server, or run `sw plugins load sws2-nightvision` in the server console.

## Commands

| Command | Description |
| :--- | :--- |
| `!nv` / `!nightvision` | Toggle night vision |
| `!nv on` | Enable night vision |
| `!nv off` | Disable night vision |
| `!nvi [intensity]` / `!nvintensity [intensity]` | Set your intensity, or show the current value and range |
| `!nvstatus` | Show whether night vision is enabled and the current intensity |
| `!nvdebug` | Write detailed diagnostics to your console and the server log |

Commands are registered as raw commands, so they can also be bound in the client console:

```
bind n "nv"
```

Intensity is an exposure value: higher values brighten the scene more. The default `1.0` is a subtle boost; use `2.0`–`3.0` on very dark maps.

## Configuration

The first load creates `csgo/addons/swiftlys2/configs/plugins/sws2-nightvision/config.jsonc` from the bundled template. The file is hot-reloaded when changed.

```jsonc
{
  "sws2-nightvision": {
    // Enable debug logging.
    "Debug": false,

    "Behavior": {
      // Enable or disable the plugin's night vision feature.
      "Enabled": true,

      // Intensity for players without a saved preference.
      "DefaultIntensity": 1.0,

      // Allowed intensity range for !nvi.
      "MinIntensity": 0.5,
      "MaxIntensity": 10.0,

      // Allow players to change their intensity with !nvi.
      "AllowIntensityCommand": true,

      // Optional permission required for !nvi. Empty means everyone.
      "IntensityPermission": "",

      // Show a chat message when toggling.
      "NotifyOnToggle": true,

      // Turn night vision off and clear the preference when the player dies.
      "TurnOffOnDeath": false,

      // Turn night vision off and clear the preference when the player changes teams.
      "TurnOffOnTeamSwitch": true,

      // Prevent spectators from using night vision.
      "DisableInSpectator": true,

      // Persist preferences to preferences.json.
      "SavePreferences": true,

      // Restore a saved "enabled" preference when the player spawns.
      "RestorePreferencesOnSpawn": true
    },

    "Effect": {
      // Fade duration in seconds when the volume is applied.
      "FadeDuration": 0.0,

      // Extra exposure compensation on top of the intensity.
      "ExposureCompensation": 0.0
    }
  }
}
```

Player preferences are stored in `csgo/addons/swiftlys2/configs/plugins/sws2-nightvision/preferences.json`.

> Upgrading the plugin does not overwrite an existing `config.jsonc`. If you are upgrading from an early preview and want the new default intensity, set `DefaultIntensity` to `1.0` manually or delete `config.jsonc` so it is regenerated from the template. Saved player preferences are not changed automatically.

## Troubleshooting

If night vision is enabled but nothing changes on screen:

1. Run `!nvdebug` and check the output in your client console and the server console.
2. Make sure your client has post-processing enabled:

   ```
   mat_postprocess_enable 1
   ```

3. Some maps provide their own master post-processing volume or tonemap controller. Try a normal map such as `de_dust2` to rule out a map-specific conflict.
4. Check the server log for `[sws2-nightvision]` entries after running `!nv`.

## How it works

The plugin creates a private `post_processing_volume` entity for each player who enables night vision:

- `Master` and `ExposureControl` are enabled.
- `MinExposure` and `MaxExposure` are set to the player's intensity.
- The volume is attached to the owner's `CameraServices.PostProcessingVolumes` list.
- Transmission is blocked for every other player; the owner is never blocked.
- On disable, the plugin briefly applies a neutral exposure (never brighter than the current night vision intensity), then removes the volume to avoid a brightness flash.

This is the same exposure-control approach used by [rcnoob/Nightvision](https://github.com/rcnoob/Nightvision), with explicit camera-service attachment for SwiftlyS2. Because the volume is a master post-processing volume, the effect is applied by the client without modifying map lighting.

> **Note:** Other master post-processing volumes (for example a map's color correction or a cinematic volume) may interact with the effect. The plugin does not modify or remove map entities.

## Building

```bash
dotnet restore
dotnet build -c Release
dotnet publish -c Release
```

The release package is generated at `build/sws2-nightvision.zip`.

## Versioning and releases

This repository uses a `develop` → `main` flow:

- Pull requests run CI: `dotnet format`, Release build, package validation and a test-package artifact.
- Each PR run gets a traceable preview version such as `1.0.1-pr.12.37`, so the installed build can be identified from the plugin version.
- For pull requests from this repository, CI also publishes/updates a pre-release named `vX.Y.Z-pr.<PR>.<run>` so the package can be downloaded directly from Releases. The pre-release is removed when the PR is closed.
- When a PR is merged into `main`, the release workflow computes the next patch version from the latest stable `vX.Y.Z` tag, injects it into `src/VersionInfo.cs`, builds the package, creates the tag and publishes a GitHub Release.
- A manual release can be started with `workflow_dispatch` and a `patch`, `minor` or `major` bump.

No manual version editing is required: the version is derived from tags and injected into `src/VersionInfo.cs` at build time. The value committed in that file is only a fallback for local builds.

## Credits

- [rcnoob/Nightvision](https://github.com/rcnoob/Nightvision) — CS2 night vision approach
- [GAMMACASE/NightVision](https://github.com/GAMMACASE/NightVision) — original CS:GO night vision plugin
- [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2) and [SwiftlyS2-Plugins](https://github.com/SwiftlyS2-Plugins) — framework and project conventions

## License

[MIT](LICENSE)
