# AudioMixerVB

AudioMixerVB is a Windows-only C# WinForms mixer for controlling four virtual audio endpoint volumes:

- Game
- Chat
- Media
- Music

The app is designed for systems that already have VB-Audio Virtual Cable devices installed. It does not install, create, or emulate audio drivers.

## Requirements

- Windows 10/11 x64
- .NET 9 SDK or runtime for building/running from source
- VB-Audio CABLE A+B and C+D installed if you want the default four-cable setup
- NAudio is restored automatically through NuGet
- Visual Studio 2022/2026 or the .NET CLI

## Build and Run

```powershell
dotnet build -c Debug
dotnet run
```

The project targets `net9.0-windows`, uses WinForms, and is configured for x64.

## First Setup

1. Install VB-Audio CABLE A+B and C+D.
2. Reboot Windows after installing the audio drivers.
3. Start AudioMixerVB.
4. Choose render endpoints for Game, Chat, Media, and Music.
5. In `Application Routing`, assign active applications to channels and save the rules.
6. If Windows still keeps an app on another output device, open Windows Volume Mixer and assign the app to the matching VB-CABLE Input device.
7. Select Monitor Mix inputs from `CABLE-A/B/C/D Output` and choose a physical monitor output device.
8. Control application session volume, monitor gain, and mute state from AudioMixerVB.
9. Optionally connect an ESP32-S3 over USB Serial.

On first launch, if no settings file exists, the app tries to auto-map endpoints by friendly name:

- Game: `CABLE-A`, `VB-Audio Cable A`, or `Cable A`
- Chat: `CABLE-B`, `VB-Audio Cable B`, or `Cable B`
- Media: `CABLE-D`, `VB-Audio Cable D`, or `Cable D`
- Music: `CABLE-C`, `VB-Audio Cable C`, or `Cable C`

If a match is not found, select the endpoint manually from the ComboBox.

## Settings

Settings are saved automatically to:

```text
mixer_settings.json
```

The file is written next to the executable and stores:

- channel name
- selected endpoint ID
- selected endpoint friendly name
- last volume
- mute state
- application routing rules
- channel volume mode
- experimental/auto routing options
- monitor mix output/input endpoints
- monitor mix gains, mutes, latency, and slider mode
- selected COM port
- serial baud rate

On startup, AudioMixerVB restores endpoints by endpoint ID first, then by friendly name. If neither is available, the channel is shown as `Not found`.

## ESP32-S3 Serial Protocol

Serial is optional. The mixer works without an ESP32.

Default baud rate:

```text
115200
```

Supported volume commands:

```text
GAME:0
GAME:50
GAME:100
CHAT:0
CHAT:50
CHAT:100
MEDIA:0
MEDIA:50
MEDIA:100
MUSIC:0
MUSIC:50
MUSIC:100
```

Supported mute commands:

```text
MUTE_GAME:0
MUTE_GAME:1
MUTE_CHAT:0
MUTE_CHAT:1
MUTE_MEDIA:0
MUTE_MEDIA:1
MUTE_MUSIC:0
MUTE_MUSIC:1
```

Universal command format is also supported:

```text
SET GAME 72
SET CHAT 45
SET MEDIA 80
SET MUSIC 60
MUTE GAME 1
MUTE CHAT 0
```

Commands are read line by line, trimmed, and parsed case-insensitively. Parse errors are written to the app log.

## Application Routing

The primary channel volume mode is `ApplicationSessions`. In this mode, a channel fader controls the Windows audio sessions assigned to that channel through `ISimpleAudioVolume`.

Use the `Application Routing` table to review active audio sessions:

- Process name
- PID
- Current endpoint
- Assigned channel
- Target endpoint
- Session volume
- Status

Default example rules:

- `spotify.exe` -> Music
- `discord.exe` -> Chat
- `chrome.exe` -> Media
- `game.exe` -> Game

Default channel targets:

- Game -> `CABLE-A Input`
- Chat -> `CABLE-B Input`
- Music -> `CABLE-C Input`
- Media -> `CABLE-D Input`

Use `Refresh Apps` to rescan active audio sessions. Use `Apply Routing` to compare each active app with its target endpoint. Use `Save Routing` to persist process-to-channel rules in `mixer_settings.json`; saved rules include process name, preferred channel, preferred endpoint ID, preferred endpoint friendly name, and enabled state.

Status values:

- `Active`: no channel rule is assigned.
- `Already routed`: the app session is already on the target endpoint.
- `Routed preference saved`: Windows accepted and verified the per-app output preference.
- `Manual required`: Windows still has the app on another endpoint, so set it in Windows Volume Mixer.
- `Error`: required process or endpoint data is missing, or routing failed.
- `Restart app required`: the preference was saved, but the live app session is still on the old endpoint.
- `Experimental API error`: the undocumented Audio Policy API call failed; check the log for method, role, flow, HRESULT, exception, and interface implementation.

`Open Volume Mixer` launches `ms-settings:apps-volume` and falls back to `ms-settings:sound`.

Experimental automatic output routing is off by default. When it is off, AudioMixerVB does not call undocumented Windows audio policy APIs; it only checks whether the current endpoint matches the target and shows `Manual required` when Windows Volume Mixer must be used.

When it is on, AudioMixerVB uses the undocumented Windows Audio Policy API, the same class of API used by apps like EarTrumpet to move apps between playback devices. The implementation is isolated under `Interop/AudioPolicy` and `UndocumentedAudioPolicyRouter`.

The audio policy factory is activated with `RoGetActivationFactory`, but the returned WinRT interface is called through raw native pointer/vtable slots. AudioMixerVB does not use managed `IInspectable` marshalling.

Automatic routing writes the Windows per-app output preference. Some applications move immediately, while others keep the current audio stream until playback restarts or the app is fully restarted. If automatic routing fails, use Windows Volume Mixer manually; the manual fallback remains supported.

`Auto apply` refreshes and applies routing rules every three seconds without repeating identical log messages.

Endpoint volume control is still present as a fallback when no active routed application session matches a channel.

### Automatic Routing Test

1. Set the Music channel endpoint to `CABLE-C Input`.
2. Make sure `spotify.exe` is assigned to Music in `Application Routing`.
3. Enable `Experimental`.
4. Click `Apply Routing`.
5. Fully close Spotify.
6. Start Spotify again and begin playback.
7. Click `Refresh Apps`.
8. Spotify should appear on `CABLE-C Input`.

If Spotify does not move before restart but moves after restart, automatic routing is working.

## Monitor Mix

Application routing sends apps into VB-CABLE render endpoints:

- Game -> `CABLE-A Input`
- Chat -> `CABLE-B Input`
- Music -> `CABLE-C Input`
- Media -> `CABLE-D Input`

The Monitor Mix section captures the matching VB-CABLE recording endpoints:

- Game input = `CABLE-A Output`
- Chat input = `CABLE-B Output`
- Music input = `CABLE-C Output`
- Media input = `CABLE-D Output`

AudioMixerVB mixes those capture streams in user mode with NAudio and plays the combined monitor stream to a selected physical render device such as `Speakers (Focusrite USB Audio)`, headphones, Realtek speakers, or another real playback device.

Do not select a VB-CABLE render endpoint as the monitor output. Monitoring into `CABLE`, `VB-Audio Cable`, or another virtual cable can create a feedback loop.

For the shared monitor mix, Windows `Listen to this device` is not required. It is usually better to turn `Listen to this device` off for all VB-CABLE outputs to avoid duplicated audio.

Channel slider mode:

- `App Session Volume`: channel sliders control matching app sessions through `ISimpleAudioVolume`.
- `Monitor Mix Gain`: channel sliders control only the monitor mix gain.
- `Both`: channel sliders control app session volume and monitor mix gain.

`Both` is the default. Monitor gain is a 0.0-1.0 scalar and is not boosted above unity.

### Monitor Mix Test

1. Set channel render endpoints:
   - Game -> `CABLE-A Input`
   - Chat -> `CABLE-B Input`
   - Music -> `CABLE-C Input`
   - Media -> `CABLE-D Input`
2. Set routing rules:
   - `spotify.exe` -> Music
   - `chrome.exe` -> Media
   - `discord.exe` -> Chat
3. In Monitor Mix, select:
   - Game input = `CABLE-A Output`
   - Chat input = `CABLE-B Output`
   - Music input = `CABLE-C Output`
   - Media input = `CABLE-D Output`
   - Output device = `Speakers (Focusrite USB Audio)` or another physical device
4. Click `Start Monitor`.
5. Start Spotify playback.
6. Verify Spotify is routed to `CABLE-C Input`, `CABLE-C Output` is captured, audio is heard through the physical output, and the Music slider changes loudness.
7. Start Chrome audio and verify Media controls `CABLE-D`.
8. Turn off Windows `Listen to this device` for VB-CABLE outputs if audio is doubled.

## Important Limitations

- AudioMixerVB controls the volume and mute state of selected Windows audio render endpoints.
- It is not a kernel-mode driver.
- It does not create virtual audio devices.
- It does not inject DLLs, hook processes, bypass anti-cheat software, or modify application processes directly.
- Monitor Mix uses user-mode WASAPI capture/output through NAudio.
- VB-Audio Cable creates the virtual devices.
- For full monitoring, routing, or channel mixing, you may still need separate routing software such as Voicemeeter.
