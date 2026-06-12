# AudioMixerVB

AudioMixerVB is a Windows-only C# WinForms mixer for routing apps into four VB-CABLE channels and mixing them into separate Monitor and Stream mixes:

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
- monitor mix master gain, channel gains, mutes, latency, exclusive output mode, and slider mode
- stream mix output endpoint
- stream mix master gain, channel gains, mutes, and latency
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

`Auto apply` refreshes and applies routing rules every few seconds without repeating identical log messages. It is designed to leave the audio engine alone in steady state:

- The persisted per-app output preference is read first and only written when it does not already match the target, because every write makes Windows re-evaluate the app's live streams (audible as a brief glitch).
- After a successful write, the rule is not re-applied until the app's process set changes (for example, after an app restart).
- `System Sounds` (PID 0) cannot be routed through the API at all; it always follows the Windows default output device. To land system sounds on a channel, set the matching VB-CABLE Input as the Windows default playback device.

The Mixer tab controls Monitor Mix gain. It does not change Windows app-session volume or VB-CABLE endpoint volume, because those source-level changes would affect both Monitor and Stream mixes.

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

- `Monitor Mix Gain`: channel sliders control only the Monitor Mix heard in headphones.

`Monitor Mix Gain` is the default. Monitor gain is a 0.0-1.0 scalar and is not boosted above unity.

Monitor latency is adjustable from the Monitor Mix panel (10-500 ms, default 20). The `Exclusive output` checkbox switches the monitor output to WASAPI exclusive mode (16-bit PCM), which bypasses the shared Windows audio engine for the lowest output latency; if exclusive mode is unavailable the engine falls back to shared mode automatically and logs why. Exclusive mode locks the output device, which is safe in this design because all applications render into VB-CABLE devices, not the physical output.

AudioMixerVB keeps Monitor Mix gains separate from Stream Mix gains. App Session Volume changes Windows app volume before audio reaches both Monitor and Stream mixes, so it is not used by the Mixer tab when independent monitor/stream mixes are needed.

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

## Stream Mix for OBS

AudioMixerVB has two independent mixes:

1. Monitor Mix goes to the user's headphones, Focusrite, or other physical monitoring output.
2. Stream Mix goes to OBS through a separate unused virtual cable.

Recommended channel routing:

- Game -> `CABLE-A Input`
- Chat -> `CABLE-B Input`
- Music -> `CABLE-C Input`
- Media -> `CABLE-D Input`

Monitor Mix:

- Captures `CABLE-A/B/C/D Output`
- Outputs to `Speakers (Focusrite USB Audio)`, headphones, or another physical output

Stream Mix:

- Captures the same `CABLE-A/B/C/D Output` channel sources
- Applies independent stream gains and mutes
- Outputs to `CABLE Input (VB-Audio Virtual Cable)`

Changing a Monitor Mix channel gain does not change the Stream Mix gain for that channel. Changing a Stream Mix channel gain does not change the Monitor Mix gain.

OBS:

1. Add Source -> Audio Input Capture.
2. Select `CABLE Output (VB-Audio Virtual Cable)`.
3. Disable Desktop Audio to avoid duplicated sound.

Do not output Stream Mix to `CABLE-A Input`, `CABLE-B Input`, `CABLE-C Input`, or `CABLE-D Input`. Those render endpoints are already used as channel routing inputs and can create feedback or routing conflicts.

Do not capture both Desktop Audio and Stream Mix in OBS. Viewers may hear the same audio twice.

Do not enable Windows `Listen to this device` for VB-CABLE outputs while using AudioMixerVB monitoring.

### Stream Mix Test

1. Set channel render endpoints:
   - Game -> `CABLE-A Input`
   - Chat -> `CABLE-B Input`
   - Music -> `CABLE-C Input`
   - Media -> `CABLE-D Input`
2. In Monitor Mix, select:
   - Output = `Speakers (Focusrite USB Audio)` or another physical device
   - Game input = `CABLE-A Output`
   - Chat input = `CABLE-B Output`
   - Music input = `CABLE-C Output`
   - Media input = `CABLE-D Output`
3. Click `Start Monitor`.
4. In Stream Mix, select:
   - Output = `CABLE Input (VB-Audio Virtual Cable)`
   - Game input display = `CABLE-A Output`
   - Chat input display = `CABLE-B Output`
   - Music input display = `CABLE-C Output`
   - Media input display = `CABLE-D Output`
5. Click `Start Stream Mix`.
6. In OBS, add `Audio Input Capture` and select `CABLE Output (VB-Audio Virtual Cable)`.
7. Disable OBS Desktop Audio.
8. Route `spotify.exe` to Music and start playback.
9. The Music monitor slider changes headphone volume, and the Music stream slider changes OBS volume independently.

Expected result:

- The user hears Monitor Mix through the physical output.
- OBS receives Stream Mix from `CABLE Output (VB-Audio Virtual Cable)`.
- Changing Stream Music gain does not change headphone volume.
- Changing Monitor Music gain does not change OBS volume.

## Low-Latency Engine

Both mix engines use event-driven WASAPI capture (20 ms buffers) and event-driven output. The process runs at high priority, audio threads register with MMCSS as `Pro Audio`, and the GC runs in sustained low-latency mode. Default mix latency is 20 ms per engine (adjustable 10-500 ms).

Each channel passes through a drift-compensating resampler that converts the capture format to the 48 kHz mix and servo-adjusts the effective rate (up to ±1%, with a -3% panic brake when the queue gets critically low) so the capture queue stays at its target depth. Clock drift between a virtual cable and the physical output is corrected continuously and inaudibly instead of by skipping samples.

The queue target adapts per channel to what its cable actually does: peak packet size, peak output read size, peak delivery gaps, and an escalating surcharge when the queue keeps running dry. A channel on a well-behaved cable keeps a tight ~25-40 ms cushion; a channel on a cable that stalls buys itself just enough cushion to survive, and the surcharge decays while the cable behaves. When a queue runs dry (a cable stopped delivering), it is pre-filled with silence so the restarting stream has its cushion immediately.

Log messages to know:

- `servo: backlog X ms, target Y ms, rate correction Z%`: per-channel health line every 30 seconds. A correction pinned at ±1.00% means the cable clock is outside the servo's reach.
- `queue ran dry; inserted N ms cushion (...)`: the cable paused. `AUDIBLE interruption` means real audio was cut; `source silent for N ms` and `no audio seen yet` are harmless idle-pump restarts.
- `stall recovery: dropped N ms`: a large burst was skipped after a real system stall.
- `output read gap of N ms`: the playback thread itself stalled (GC, driver, scheduler).

VB-CABLE driver configuration matters more than any in-app setting:

- Keep the sample rate identical on both sides of each cable (the `Input` render endpoint and the `Output` capture endpoint) in Windows sound settings. A mismatch forces the driver to resample internally and can make its delivery stall mid-stream.
- In each cable pair's `VBCABLE_ControlPanel` (run as administrator), set `Options -> Set Max Latency` to a moderate value such as 2048-4096 smp and `Internal Sampling Rate` to 48000 Hz. Both require a reboot. Oversized or mismatched driver buffering shows up as periodic `AUDIBLE interruption` log entries on that cable's channel.

## Important Limitations

- AudioMixerVB controls the volume and mute state of selected Windows audio render endpoints.
- It is not a kernel-mode driver.
- It does not create virtual audio devices.
- It does not inject DLLs, hook processes, bypass anti-cheat software, or modify application processes directly.
- Monitor Mix uses user-mode WASAPI capture/output through NAudio.
- Stream Mix uses user-mode WASAPI capture/output through NAudio.
- VB-Audio Cable creates the virtual devices.
- For full monitoring, routing, or channel mixing, you may still need separate routing software such as Voicemeeter.
