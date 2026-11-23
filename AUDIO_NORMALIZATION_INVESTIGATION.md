# Filo Media Player: Audio Normalization Investigation (Windows)

**Date:** 2026-11-22
**Commit inspected:** `06abfd4` ("Back up save"), working tree clean
**Scope:** This was a read-only investigation. No source files were changed. The only new file is this report.

---

## Executive summary

The investigation found three separate faults. Together they explain all three reported problems.

1. **The live gain never reaches the effect because two copies of the effect assembly are loaded.** It is not a separate process. The playback page calls `NormalizationAudioEffect.SetLiveGain()` on the copy that the MAUI app loaded through its project reference, which lives in the default `AssemblyLoadContext`. Media Foundation activates the effect through `WinRT.Host.dll`, and by default CsWinRT loads that second copy of `Filo.AudioEffects.Windows.dll` into its own isolated `AssemblyLoadContext`. Static fields exist once per load context, so the app and the audio thread each have their own `_liveGainDb`. The build output shows `"CSWINRT_LOAD_COMPONENTS_IN_DEFAULT_ALC": false`, and the CsWinRT shim source confirms the isolated-ALC default. The only channel that crosses the boundary correctly is the WinRT `PropertySet`, and the app never writes to it after the effect is attached.
2. **`0xC00D36B4` is caused by the empty `SupportedEncodingProperties` list.** The error decodes to `MF_E_INVALIDMEDIATYPE`: *"The data specified for the media type is invalid, inconsistent, or not supported by this object."* An empty list gives Media Foundation no media type to negotiate with the effect. Microsoft documents a default for an empty list only for *video* effects (ARGB32), not audio. The four-format float list in commit `ff64a57` did negotiate: the logs show `ProcessFrame` running ("Audio thread sees −7.20 dB"). That list was correct and should be restored.
3. **The `EntryPointNotFoundException` is most likely a load or build-state problem, not a flaw in the list itself.** The same list worked before the shared-memory experiment. The leading hypothesis is a type-identity mismatch: the component and `Microsoft.Windows.SDK.NET.dll` are loaded in the isolated ALC while `WinRT.Runtime.dll` is consolidated into the default ALC. A stale or incrementally built output is a second possibility. This finding has the lowest confidence of the three, and the plan includes a decisive check.

A further defect was found that was not reported: **switching songs keeps the first song's gain.** The `PropertySet` is created once per native player and never updated, so every later track's effect instance reads the first track's `GainDb`.

**Recommendation:** Keep the custom `IBasicAudioEffect`. It is the only option that modifies PCM, supports positive gain, and keeps `MediaElement`, video, and seeking unchanged. Fix it in three ways:

- (a) Use the `PropertySet` the app already owns as the only live channel, and remove the statics and shared memory.
- (b) Restore the explicit float encoding list.
- (c) Optionally load the component into the default ALC so that only one copy exists.

If the effect cannot be stabilised in time, the fallback is an attenuate-only normalization through `MediaPlayer.Volume`.

---

## 1. Architecture overview

### 1.1 End-to-end flow

```
┌──────────────── Backend (offline analysis) ────────────────┐
│ ProcessingJobsController.CreateLoudnessAnalysis (POST)     │ queues "LoudnessAnalysis" job
│ Filo.Processing/worker.py  process_loudness_analysis       │ downloads from S3
│ Filo.Processing/normalizer.py                              │
│   analyze_loudness()  → ffmpeg loudnorm I=-16 TP=-1.5 JSON │
│   calculate_normalization_gain()                           │
│     gain = min(-16 - I, -1.5 - TP)                         │
│ api_client.save_loudness_result → POST .../loudness-result │
│ ProcessingJobsController.SaveLoudnessAnalysis              │ validates gain+TP ≤ -1.5, saves
│ MediaFile.NormalizationGainDb (PostgreSQL)                 │
└────────────────────────────────────────────────────────────┘
                │  GET api/media/{id}/playback
                ▼
 MediaController.GetPlaybackUrl → PlaybackMediaResponse
   { PlaybackUrl = S3 pre-signed URL (2 h), NormalizationGainDb }
                │
                ▼
┌──────────────── MAUI client (Windows) ─────────────────────┐
│ PlaybackPage.LoadPlaybackMedia                             │
│   _currentNormalizationGainDb = gain ?? 0                  │
│   UpdateWindowsNormalizationGain()  → MMF write + static   │  (no effect on audio)
│   InspectWindowsPlayer(gain)                               │
│     MediaElement.Handler.PlatformView (Grid)               │
│       → MediaPlayerElement.MediaPlayer (WinRT MediaPlayer) │
│     first time only: AddAudioEffect(classId, false,        │
│                        PropertySet{GainDb})                │
│   MediaPlayer.Source = PlaybackUrl                         │
└────────────────────────────────────────────────────────────┘
                │ Media Foundation (in-process) builds the topology
                ▼
 combase RoActivateInstance("Filo.AudioEffects.Windows.NormalizationAudioEffect")
   → app.manifest reg-free entry → WinRT.Host.dll (native)
   → hostfxr (runtime already loaded) → WinRT.Host.Shim.dll
   → ActivationLoader (isolated AssemblyLoadContext)
   → Filo.AudioEffects.Windows.dll  [SECOND COPY]
   → SupportedEncodingProperties / SetEncodingProperties / SetProperties(PropertySet)
   → ProcessFrame(float32 PCM) on MF worker thread → audio renderer (MediaPlayer.Volume) → device
```

### 1.2 How the client reaches the native player

CommunityToolkit.Maui.MediaElement 10.0.0 is registered with `.UseMauiCommunityToolkitMediaElement(false)` (`MauiProgram.cs:16`). On Windows, the toolkit source (main branch, `Views/MediaManager.windows.cs`) creates the native player once per handler: `Player = new(); mediaPlayer = new(); Player.SetMediaPlayer(mediaPlayer);`. `MauiMediaElement` is `Grid`, and the `MediaPlayerElement` is its child. When `Source` changes, the toolkit calls `SetUriSource` on the **same** `MediaPlayer`. It does not create a new player. When the page or handler is unloaded, `MauiMediaElement.Dispose` sets `Source = null` and calls `mediaPlayer.Dispose()`.

`PlaybackPage.InspectWindowsPlayer` (`PlaybackPage.xaml.cs:1118-1189`) relies on this undocumented internal shape: `PlatformView is Grid` → `Children.OfType<MediaPlayerElement>()` → `.MediaPlayer`. The approach works, but a toolkit update could break it (see §9).

### 1.3 Backend gain calculation

The backend calculation is correct for its purpose. `normalizer.py:63-80` targets −16 LUFS and caps the gain so that the predicted true peak stays at or below −1.5 dBTP. `ProcessingJobsController.SaveLoudnessAnalysis` (`ProcessingJobsController.cs:594-675`) checks the same constraint on the server (`:619-623`). Two consequences follow:

- Positive gains are possible, for quiet tracks that have peak headroom. A volume-only approach cannot apply them (§5).
- Because `gain + TP ≤ −1.5 dBTP`, the hard clamp in the effect (`NormalizationAudioEffect.cs:138`) should almost never engage. It is a safety net, not a limiter.

---

## 2. Current implementation status

| Area | Status | Evidence |
|---|---|---|
| Loudness analysis and gain storage | **Works** | `normalizer.py`, `ProcessingJobsController.cs:594-675`, `MediaFile.cs:23-33` |
| API returns gain | **Works** (media 15 returns −7.20 dB) | `MediaController.cs:92-145`, both `PlaybackMediaResponse` DTOs have `NormalizationGainDb` |
| Effect activation (reg-free WinRT) | **Works** (logs show attach and `ProcessFrame`) | `app.manifest:18-23`, `FiloMediaPlayer.csproj:141-155` |
| Initial gain applied to PCM | **Worked** with the float-list build (`ff64a57`) | User log "Audio thread sees −7.20 dB" |
| Toggle during playback | **Broken.** Writes to the wrong copy of the static | §3.1 |
| Gain on song change | **Broken.** Stale `PropertySet` | §3.4 |
| Playback with current build | **Broken.** `0xC00D36B4` | `NormalizationAudioEffect.cs:48-49` |

### Experimental or leftover code that is still present

| Location | What it is | Disposition |
|---|---|---|
| `PlaybackPage.xaml.cs:11` | `using System.IO.MemoryMappedFiles;` | Shared-memory experiment. Remove. |
| `PlaybackPage.xaml.cs:36-37` | `_normalizationMemory`, `_normalizationWriter` fields | Experiment. Remove. These are never disposed, so the handle leaks for each page instance. |
| `PlaybackPage.xaml.cs:59-61` | `InitializeNormalizationMemory()` call in the constructor | Experiment. Remove. |
| `PlaybackPage.xaml.cs:1198-1204` | Shared-memory `Write` inside `UpdateWindowsNormalizationGain` | Experiment. Remove. |
| `PlaybackPage.xaml.cs:1207` | `NormalizationAudioEffect.SetLiveGain(gain)` | Static-field attempt. It cannot work across ALCs. Remove. |
| `PlaybackPage.xaml.cs:1213-1237` | `InitializeNormalizationMemory()` | Experiment. Remove. |
| `PlaybackPage.xaml.cs:340-345, 386-388` | Duplicate "API gain" debug logs | Diagnostic noise. |
| `NormalizationAudioEffect.cs:17-18, 157-169` | `static _liveGainDb/_liveMultiplier`, `SetLiveGain` | Static-field attempt. Replace with instance state. |
| `NormalizationAudioEffect.cs:27-47` | Original float list, commented out | **This is the correct implementation.** Restore it. |
| `NormalizationAudioEffect.cs:48-49` | `Array.Empty<AudioEncodingProperties>()` | Temporary workaround that causes the `0xC00D36B4` failure. |
| `NormalizationAudioEffect.cs:14-15, 20-21` | `_gainDb`, `_gainMultiplier`, `_firstFrameLogged` | Unused leftovers. |
| `FiloMediaPlayer/bin/Debug/net10.0-windows10.0.19041.0/` | Stale output from an older TFM (WinRT.Runtime **2.2.0**, SDK.NET **10.0.19041.55**) | Not used by the current x64 build, but it should be cleaned out. |

The effect-side shared-memory reader is gone, as you reported. Only the app-side writer remains.

---

## 3. Root-cause analysis

### 3.1 Problem 1: The live gain does not update

**Hypothesis tested: does the effect run in a separate process?** It does not. Three pieces of evidence point to in-process execution:

- `WindowsPackageType=None` (`FiloMediaPlayer.csproj:35`) makes this an unpackaged desktop app. `Windows.Media.Playback.MediaPlayer` runs Media Foundation inside the calling process. Protected (PMP) content would be the exception.
- Activation goes through a reg-free `<file name="WinRT.Host.dll"><activatableClass .../>` in the **exe's** manifest (`app.manifest:18-23`). Reg-free activation loads the DLL into the process that owns the manifest.
- The effect's `Debug.WriteLine` output ("Audio thread sees −7.20 dB", "Gain updated to −7.20 dB") shows up in the debugger attached to the app. Output from another process would not appear there.

**Actual cause: the same process, but two `AssemblyLoadContext`s.**

1. The MAUI app has a `ProjectReference` to the effect project (`FiloMediaPlayer.csproj:141-143`). `PlaybackPage` calls `Filo.AudioEffects.Windows.NormalizationAudioEffect.SetLiveGain(gain)` directly (`PlaybackPage.xaml.cs:1207`), which loads `Filo.AudioEffects.Windows.dll` into the **default ALC**.
2. Media Foundation activates the class by its string ID through `WinRT.Host.dll`. The CsWinRT shim (`src/Authoring/WinRT.Host.Shim/Module.cs`, master) reads the AppContext switch `CSWINRT_LOAD_COMPONENTS_IN_DEFAULT_ALC`. When the switch is missing or false, it loads the component through `private class ActivationLoader : AssemblyLoadContext`, creating one ALC per component path. Inside that ALC, only `WinRT.Runtime` is consolidated: *"Consolidate all WinRT.Runtime loads to the default ALC, or failing that, the first shim ALC"*. Everything else, including the component, is loaded **again**. The CsWinRT NuGet readme states that with `CsWinRTLoadComponentsInDefaultALC` false, components *"load in their own ALC"*.
3. Static fields belong to a type, and a type is identified per ALC. The app writes `_liveGainDb` in copy A. `ProcessFrame` reads `_liveGainDb` in copy B (`NormalizationAudioEffect.cs:104-105, 131-132`).

**Why the logs look the way they do.** The sequence explains every line you saw:

- `SetProperties` runs on the copy-B instance. It reads `GainDb = −7.20` from the `PropertySet`, then `UpdateGainFromConfiguration` calls `SetLiveGain` **inside copy B** (`:96`). That produces "Gain updated to −7.20" and "Audio thread sees −7.20".
- Each later toggle calls `SetLiveGain` in copy A. That produces "Live gain requested = 0.00" in the app, but copy B never changes, so no "Audio thread sees" line follows.

**Why `MapChanged` never fired.** The app never mutates the `PropertySet`. It creates `{ ["GainDb"] = appliedGain }` once (`PlaybackPage.xaml.cs:1156-1160`), stores it in `_normalizationConfiguration` (`:1179`), and never writes to it again. `UpdateWindowsNormalizationGain` (`:1192-1211`) only touches shared memory and the static. The `MapChanged` path in the effect (`NormalizationAudioEffect.cs:70, 75-81`) was therefore never tested. The `PropertySet` is a WinRT object, so it crosses ALC and ABI boundaries correctly. It is the right channel.

### 3.2 Problem 2: The shared-memory experiment

- **Viability:** Because the effect runs in-process, a named `MemoryMappedFile` (`Local\FiloNormalizationGain`) would technically be reachable from both ALCs. The approach was not inherently impossible.
- **Why it is the wrong tool:**
  - It adds a kernel object and a global name, which collides when two players run at once.
  - It needs lifetime and ordering rules: the reader may open the mapping before the writer creates it.
  - It needs manual disposal. The current writer is never disposed.
  - It puts file-mapping I/O and possible exceptions on the real-time audio thread.

  An exception escaping `ProcessFrame` or `SetEncodingProperties` makes Media Foundation close the effect with an error and fail playback. That is the most likely reason the experiment "coincided with playback failures". The mechanism was not the problem; error handling inside the effect was.
- **Conclusion:** Shared memory solves a problem that the `PropertySet` already solves with less code. Remove the remaining writer.

### 3.3 Problem 3a: `0xC00D36B4` / `SourceNotSupported` with the empty list

- `certutil -error 0xC00D36B4` decodes it as *"The data specified for the media type is invalid, inconsistent, or not supported by this object."* This is `MF_E_INVALIDMEDIATYPE`. The toolkit's `OnMediaElementMediaFailed` formats `args.Error.ToString()`, which is the `MediaPlayerError.SourceNotSupported` value, and `args.ExtendedErrorCode`.
- Microsoft's custom-audio-effects guide: *"if the consumer of your effect can't encode audio using the properties you specify, the system will call Close on your effect and will remove your effect from the audio pipeline."* An empty list specifies no properties at all.
- Empty-list behaviour is documented only for **video** (`IBasicVideoEffect.SupportedEncodingProperties`: *"If you return an empty list... the system will default to ARGB32"*). The audio API page has no equivalent statement.
- The effect is added with `effectOptional: false` (`PlaybackPage.xaml.cs:1166`). A required effect that cannot be inserted fails the whole topology instead of being skipped.
- **Control experiment already performed:** with the explicit float list, the pipeline negotiated and `ProcessFrame` ran. Only the list changed, and playback broke. The disappearance of the `EntryPointNotFoundException` is therefore **not** evidence that the effect works. The empty list simply stops the pipeline earlier, at type negotiation.

### 3.4 Problem 3b: `EntryPointNotFoundException` in `IReadOnlyList_..._AudioEncodingProperties.Do_Abi_get_Size`

**What is established:**

- `Do_Abi_get_Size` is the CsWinRT-generated, `UnmanagedCallersOnly` ABI thunk for `IVectorView<AudioEncodingProperties>.Size`. It is compiled into the **component** assembly because `CsWinRTComponent=true` (`Filo.AudioEffects.Windows.csproj:8`).
- The returned object was a plain `List<AudioEncodingProperties>`, and `Count` on that type cannot throw. The exception therefore comes from resolving the generated marshalling code, not from your list logic.
- The same list code worked at `ff64a57`. That rules out "this property shape is unsupported."
- `EntryPointNotFoundException` (HRESULT `0x80131523`, a subclass of `TypeLoadException`) is what the runtime throws when a method or function-pointer target cannot be resolved at bind time. One example is an unresolved static virtual/abstract interface implementation.

**Likely causes, in order:**

1. **Split type identity across ALCs (leading hypothesis).** In the isolated ALC, the component and its own copy of `Microsoft.Windows.SDK.NET.dll` are loaded. The shim names only `WinRT.Runtime` for consolidation, so the second SDK copy is an inference from the source logic. `WinRT.Runtime` is shared with the default ALC, where the app's projection lives. Generic ABI instantiations such as `IReadOnlyList<AudioEncodingProperties>` sit exactly at the junction of all three assemblies. If the runtime binds a marshaller or vtable member against the "other" `AudioEncodingProperties` type, the generated entry point cannot be found. This fits both the failure location and the timing: the failure appeared during a period of rebuilds and experiments, which brought two loaded copies into play.
2. **Stale or incremental build artifacts.** Two observations support this:
   - The app's `deps.json` declares `runtimepack.Microsoft.Windows.SDK.NET.Ref` **10.0.26100.57**, but the `Microsoft.Windows.SDK.NET.dll` in the app output is file version **10.0.26100.55**. That is the component project's copy, with assembly version `10.0.26100.38` in both cases. Two projects are copying different SDK projection builds into the same folder, and whichever copies last wins.
   - The custom `CopyX64WinRTHost` target (`FiloMediaPlayer.csproj:151-155`) hard-codes the `2.3.1` package path and runs after the normal copy.

   A half-updated output, such as a component DLL regenerated while the other side was stale, can produce exactly this class of binding failure.
3. **Debugger first-chance stop.** If "break when thrown" was enabled, Visual Studio may have stopped on a first-chance exception that the CsWinRT thunk then converted to a failing HRESULT. The result is still a failed media-type negotiation, but the root cause is the binding problem in (1) or (2), not the list.

The decisive diagnostic is in §7, step 2: log `AssemblyLoadContext.GetLoadContext(typeof(NormalizationAudioEffect).Assembly)` and list duplicate loaded assemblies. The fix for (1) and the fix for Problem 1 are the same: one copy per assembly.

### 3.5 Unreported defect: switching songs reuses the first song's gain

- The toolkit reuses one native `MediaPlayer` for every source (§1.2). After the first attach, `InspectWindowsPlayer` takes the "same native player" branch (`PlaybackPage.xaml.cs:1148-1153`) and only calls `UpdateWindowsNormalizationGain`, which never touches `_normalizationConfiguration`.
- Effects added with `AddAudioEffect` stay attached to the player across sources. This is inferred: `RemoveAllEffects` exists for removal, and persistence across `Source` changes is not explicitly documented. For each new source, the topology activates a fresh effect instance and calls `SetProperties` with the stored `PropertySet`, which still contains track 1's `GainDb`.
- **Result:** every track plays at the first track's gain, and the toggle does not work either. This needs no ALC theory, because it follows directly from the code.

### 3.6 Minor issues

- `SetEncodingProperties` throws `NotSupportedException` (`NormalizationAudioEffect.cs:53-58`). With an explicit float list, Media Foundation should only offer float, so the throw is acceptable. Still, any exception in the effect is fatal to playback with `effectOptional:false`.
- The `nativePlayer.MediaFailed` lambda (`PlaybackPage.xaml.cs:1169-1176`) captures the page and is never unsubscribed. The leak is small, but it is real.
- A gain step on toggle is applied instantly at a frame boundary, which can produce an audible click. A short ramp prevents this (§6).
- `ProcessFrame` calls `Math.Clamp` per sample in `double`. That is acceptable for CPU, but `float` math with an early exit when the multiplier is 1.0 is cleaner.

---

## 4. Evidence index

| Finding | File : lines | Notes |
|---|---|---|
| Unpackaged app | `FiloMediaPlayer/FiloMediaPlayer.csproj:35` | `WindowsPackageType=None` |
| Direct project reference (default-ALC copy) | `FiloMediaPlayer.csproj:141-143` | |
| Hard-coded WinRT.Host copy | `FiloMediaPlayer.csproj:151-155` | Path pinned to `cswinrt\2.3.1` |
| Reg-free activation | `Platforms/Windows/app.manifest:18-23` | `WinRT.Host.dll`, `threadingModel="both"` |
| Isolated-ALC default | `bin/x64/Debug/.../win-x64/WinRT.Host.runtimeconfig.json` | `"CSWINRT_LOAD_COMPONENTS_IN_DEFAULT_ALC": false` |
| App runtimeconfig lacks the switch | `bin/x64/Debug/.../win-x64/FiloMediaPlayer.runtimeconfig.json` | The runtime is already loaded by the app, so per the .NET hosting docs **the host's runtimeconfig properties are ignored**. The switch has to be in the *app's* runtimeconfig to take effect. |
| CsWinRT shim behaviour | `microsoft/CsWinRT` `src/Authoring/WinRT.Host.Shim/Module.cs` | `ActivationLoader : AssemblyLoadContext`. Only `WinRT.Runtime` is consolidated. |
| CsWinRT component | `Filo.AudioEffects.Windows.csproj:4,8,15` | `net10.0-windows10.0.26100.0`, `CsWinRTComponent`, CsWinRT 2.3.1 |
| Version consistency | Output binaries | `WinRT.Runtime` 2.3.1.54291 in both app and component. `Microsoft.Windows.SDK.NET` file 10.0.26100.55 in both, but the app's deps.json says .57. |
| Static channel | `NormalizationAudioEffect.cs:17-18, 104-105, 131-132, 157-169` | |
| App writes the static | `PlaybackPage.xaml.cs:1207` | |
| PropertySet never updated | `PlaybackPage.xaml.cs:1148-1160, 1179, 1192-1211` | |
| MapChanged handler (never exercised) | `NormalizationAudioEffect.cs:61-100` | |
| Empty encoding list | `NormalizationAudioEffect.cs:48-49` | |
| Original list | `NormalizationAudioEffect.cs:27-47`; `git show ff64a57:Filo.AudioEffects.Windows/NormalizationAudioEffect.cs` | |
| Required effect | `PlaybackPage.xaml.cs:1164-1167` | `effectOptional:false` |
| Shared-memory writer | `PlaybackPage.xaml.cs:11, 36-37, 59-61, 1198-1204, 1213-1237` | |
| Toggle handler | `PlaybackPage.xaml.cs:447-457`; `PlaybackPage.xaml:314-318` | |
| Song change path | `PlaybackPage.xaml.cs:311-421` (`367-381`) | Gain computed, then effect configured, then Source set |
| Volume slider (separate from gain) | `PlaybackPage.xaml.cs:441-444`; `PlaybackPage.xaml:230` | |
| Error decode | `certutil -error 0xC00D36B4` | MF_E_INVALIDMEDIATYPE text |
| Backend gain | `Filo.Processing/normalizer.py:17-26, 63-80`; `ProcessingJobsController.cs:619-623, 655-660`; `MediaController.cs:92-145` | |

---

## 5. Solution comparison

| # | Approach | Real PCM change? | Positive gain? | Live toggle | Video / seek / modes | Effort | Risk |
|---|---|---|---|---|---|---|---|
| **A** | **Fix the existing `IBasicAudioEffect`** (PropertySet channel, restore float list, optional default-ALC load) | Yes | Yes | Yes, via the `PropertySet` | Unchanged; the `MediaElement` stays | Low to medium | Medium (CsWinRT hosting) |
| B | `MediaPlayer.Volume` scaled by the normalization factor | No (renderer volume) | **No** (max 1.0) | Yes | Unchanged | Very low | Low |
| C | AudioGraph (`MediaSourceAudioInputNode.OutgoingGain`) | Yes | Yes | Yes | **Audio only.** Video, seeking, position, and end events must be rebuilt. | High | High |
| D | Server-side normalized renditions (ffmpeg `loudnorm` → second S3 object) | Yes (offline) | Yes | Needs a source swap and seek restore | Works; cross-platform | Medium | Medium (2× storage, swap glitch) |
| E | Default-ALC load plus keep static fields | Yes | Yes | Yes | Unchanged | Very low | Medium (global mutable state, one value for every player, depends on hosting switch) |
| F | Named shared memory | Yes | Yes | Yes | Unchanged | Medium | High (already caused failures; lifetime and naming issues) |

Notes:

- **B** can only attenuate. To keep loudness relationships consistent, every gain would have to be shifted down by the library maximum, or positive gains clamped to 0 dB, which makes quiet tracks stay quiet. It also competes with the user's volume slider: the app would have to compute `userVolume × factor`, and the toolkit writes `player.Volume` back into `MediaElement.Volume` on `VolumeChanged`. Media Foundation does not document whether `MediaPlayer.Volume` is linear amplitude, so this would need measurement. B is not "modifying the actual audio signal" as you specified, but it is a valid fallback.
- **C** is the cleanest pure-audio architecture, but it replaces the `MediaElement` for audio and cannot play video.
- **D** is the most portable option (Android, iOS, macOS), and it may suit a future cross-platform phase. It does not solve the Windows toggle requirement on its own.

---

## 6. Recommended solution

**Approach A: keep and fix the custom effect, with the `PropertySet` as the only live control channel.**

Justification:

- It meets every stated requirement: real PCM change, positive gain, live toggle, per-track gain, and no change to the `MediaElement`, video, seeking, or playback modes.
- Most of the work is already done and has been shown to work: activation, negotiation with the float list, and `ProcessFrame` sample access through `IMemoryBufferByteAccess`.
- The `PropertySet` is the mechanism Microsoft intends. The official AudioCreation `AudioEchoEffect` sample stores the `IPropertySet` in `SetProperties` and reads `"Mix"` from it during processing, with the comment *"all updates should be done through the property set"*. As a WinRT object, it crosses the ALC boundary correctly, which no managed static can do.

Design:

1. **App side (`PlaybackPage`).**
   - Own one `PropertySet` per native player.
   - On every gain change (load, song change, toggle), write `_normalizationConfiguration["GainDb"] = enabled ? trackGain : 0.0`. Write it **before** setting `Source` on song changes, so the new effect instance's `SetProperties` sees the correct value.
   - Remove the static call and the shared memory.
2. **Effect side.**
   - Add an instance field `float _targetMultiplier` (`Volatile`), set from `SetProperties` and `MapChanged`.
   - Optionally, `ProcessFrame` also re-reads `GainDb` from the stored `IPropertySet` as a fallback. One boxed lookup per frame is about 100 calls per second, which is negligible. This follows the Microsoft sample and works even if Media Foundation hands the effect a proxy or copy that does not raise `MapChanged`. That behaviour is undocumented, so the fallback is cheap insurance.
   - Keep a `_currentMultiplier` and **ramp** linearly to the target across one frame (about 10 ms), so toggles do not click.
   - Remove all `static` state.
3. **Encoding.**
   - Restore the explicit list, built **once** in the constructor or a static readonly, as the Microsoft sample does.
   - Consider adding 32 kHz, 88.2 kHz, 96 kHz, and up to 6 or 8 channels. The math is channel-agnostic, and Media Foundation can then avoid a resampler when content arrives at those rates.
   - Do not throw from `SetEncodingProperties`. Log, set a bypass flag, and pass audio through.
4. **Hosting (conditional, see plan step 5).** If any `EntryPointNotFoundException` or duplicate-assembly evidence remains after steps 1 to 4, set `CSWINRT_LOAD_COMPONENTS_IN_DEFAULT_ALC=true` in the **app's** runtimeconfig. Use `<ItemGroup><RuntimeHostConfigurationOption Include="CSWINRT_LOAD_COMPONENTS_IN_DEFAULT_ALC" Value="true" /></ItemGroup>` in `FiloMediaPlayer.csproj`. With this, the shim loads the component and its dependencies into the default ALC, so there is one copy of each type. The CsWinRT readme warns that components and dependencies must be compatible. Here they are the same files, which removes the risk.
5. **Robustness.** Consider `effectOptional: true`. Then a negotiation failure plays un-normalized audio instead of failing playback. Pair it with a visible "normalization unavailable" state, so the failure is not silent.

Tradeoffs:

- **Windows only.** Other platforms need their own implementation, or approach D later.
- The approach relies on the toolkit's internal view shape to reach the native `MediaPlayer`. Wrap that lookup and degrade gracefully if it fails.
- CsWinRT authoring plus an unpackaged reg-free host is an uncommon configuration. Pin versions and keep the build clean.

---

## 7. Implementation plan

Apply the steps in order, with one change per step, and test after each one.

1. **Clean the build state.**
   - Close Visual Studio. Delete `bin/` and `obj/` in `FiloMediaPlayer` and `Filo.AudioEffects.Windows`, including the stale `net10.0-windows10.0.19041.0` folder. Rebuild x64 Debug.
   - Confirm that a single `Microsoft.Windows.SDK.NET.dll` version and `WinRT.Runtime.dll` 2.3.1 are in the output.
   - Consider removing the custom `CopyX64WinRTHost` target if `WinRT.Host.dll`, `WinRT.Host.Shim.dll`, and `WinRT.Host.runtimeconfig.json` already arrive through the project reference. They currently do. If you keep the target, replace the hard-coded `2.3.1` with an MSBuild property.
2. **Add a diagnostic and confirm the ALC hypothesis.** This is temporary.
   - In the effect constructor, log `AssemblyLoadContext.GetLoadContext(typeof(NormalizationAudioEffect).Assembly)?.Name` and `typeof(NormalizationAudioEffect).Assembly.GetHashCode()`.
   - Log the same values in the app before `AddAudioEffect`.
   - Log `AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name is "Filo.AudioEffects.Windows" or "Microsoft.Windows.SDK.NET")` together with each assembly's ALC.
   - Expect two contexts: `Default` in the app and a shim ALC in the effect.
3. **Restore the encoding list.**
   - Replace `NormalizationAudioEffect.cs:48-49` with the cached float list, using the original four formats first.
   - Make `SetEncodingProperties` non-throwing, with a bypass flag.
   - Run and confirm that playback starts and that `ProcessFrame` logs once.
   - If `EntryPointNotFoundException` returns, capture the full exception message, which names the missing method or type, and go to step 5 before continuing.
4. **Switch to the PropertySet channel.**
   - Effect: delete the statics and `SetLiveGain`. Store the `IPropertySet` and use instance `_targetMultiplier` and `_currentMultiplier` with a per-frame ramp. Use `MapChanged` plus an optional per-frame `TryGetValue` fallback. Unsubscribe in `Close`.
   - App: replace the body of `UpdateWindowsNormalizationGain` with a write to `_normalizationConfiguration["GainDb"]`, guarded for null.
   - In `LoadPlaybackMedia`, make sure the write happens before `MediaPlayer.Source = ...`. It does today, but only because `InspectWindowsPlayer` runs first. Keep that order explicit.
   - Remove the `SetLiveGain` call.
5. **Conditional: load in the default ALC.**
   - If step 2 showed duplicates and step 3 or 4 has any type-load or entry-point exceptions, add the `RuntimeHostConfigurationOption` from §6, item 4, to `FiloMediaPlayer.csproj`.
   - Verify that `FiloMediaPlayer.runtimeconfig.json` now contains the switch, and that the step-2 logs show a single `Default` context.
6. **Remove the shared-memory experiment.** Delete `PlaybackPage.xaml.cs:11, 36-37, 59-61, 1198-1204, 1213-1237`.
7. **Tidy the native-player hookup.**
   - Store the `MediaFailed` handler as a method and unsubscribe it when the page disappears or the native player changes.
   - When `InspectWindowsPlayer` sees a *new* native player, for example after navigating away and back, attach the effect to the new player. The current code already does this; keep it.
   - Decide on `effectOptional` (§6, item 5).
8. **Remove the diagnostics.** Strip the step-2 logging and the duplicate "API gain" logs. Keep one concise log line per gain change.

---

## 8. Testing strategy

### 8.1 Show that the PCM is actually modified

- **In-effect measurement (debug builds).** In `ProcessFrame`, compute the RMS of the buffer before and after the multiply, once per second. Log `20·log10(after/before)`. Expect a value equal to the applied gain (−7.20 dB for media 15, within ±0.01 dB) and 0.00 dB when the toggle is off.
- **External measurement.** Upload a reference file: a 1 kHz sine at −20 dBFS, 60 s, 48 kHz float WAV or FLAC, and run loudness analysis on it so it gets a known gain.
  - Record the system output with WASAPI loopback (for example, Audacity → Host "Windows WASAPI" → loopback device). Keep the Windows mixer and the app volume fixed at 100%.
  - Compare RMS or LUFS with normalization on and off. The difference should equal the API gain.
  - Repeat with a track whose gain is **positive** to show that boost works, which Approach B cannot do.
- **Unit tests.** Extract the gain and ramp math into a pure static function on `Span<float>`. Test it for the following:
  - 0 dB is an identity.
  - −6.0206 dB halves amplitude.
  - The ramp reaches the target exactly at the end of the frame.
  - The clamp holds at ±1.0.
  - NaN or infinite gain is ignored.

### 8.2 Toggles and song switching

- Toggle on and off ten times during playback. Each toggle should produce exactly one effect-side log ("target multiplier → x") within about 50 ms, an audible level change, and no clicks. Use the loopback recording to check for clicks.
- Play a playlist of three tracks with clearly different gains, for example −7.2 dB, +3 dB, and −1 dB. Check the following:
  - Each track's effect log shows its own gain on `SetProperties`.
  - Toggling the switch while track 2 is playing affects track 2.
  - Next and Previous, Loop (`OnMediaEnded` → `LoadPlaybackMedia`), and auto-advance all apply the correct gain.
- A media file with `NormalizationGainDb == null` should play at 0 dB.

### 8.3 Regression

- Run all playback modes (Normal, Shuffle All, Random No Repeat, True Random) and check that history and Previous behave as before.
- Check seeking with the slider and with bookmarks. There should be no glitches and no `DiscardQueuedFrames` issues, because the effect is stateless apart from the ramp.
- **Video playback** (mp4/webm) should show synchronized audio with normalization applied, and no `MediaFailed`.
- Navigate away from the page and back. The new handler creates a new native player, and the effect should attach again. Check that no `MediaFailed` handlers pile up.
- Leave playback running for an hour and confirm the following:
  - Memory and handle counts are stable (Task Manager or `dotnet-counters`).
  - Playback of a long track is not affected by the S3 pre-signed URL expiry (2 h).
- Force a failure: temporarily mis-spell the activatable class ID. The app should behave predictably, with an error message or un-normalized playback if the effect is optional.

---

## 9. Risks and compatibility

| Area | Risk | Mitigation |
|---|---|---|
| CsWinRT hosting / ALC | The isolated ALC duplicates types, and a default-ALC switch in the host's runtimeconfig is **ignored** when the runtime is already loaded. | Put the switch in the app's runtimeconfig. Keep app and component on the same CsWinRT and SDK projection versions. |
| WinRT projections / versions | The app's `deps.json` lists SDK.NET.Ref .57 while the output DLL is .55, because two projects copy into one folder. | Clean builds. Consider setting the same `WindowsSdkPackageVersion` in both csproj files so they resolve the same projection. |
| .NET 10 + CsWinRT 2.3.1 | Authoring components on .NET 10 with CsWinRT 2.3.x is a newer combination. Generic ABI instantiations are generated code. | Pin versions. Avoid exposing extra generic collections from the component. Build the encoding list once. |
| Toolkit internals | `PlatformView is Grid` → `MediaPlayerElement` is an implementation detail of CommunityToolkit.Maui.MediaElement. | Fail soft, with logging, when the lookup fails. Re-check after every toolkit upgrade. |
| Media formats | Media Foundation must convert the source to one of the declared float formats. Rates outside the list force resampling, and unusual layouts may fail to negotiate. | Declare more rates and channel counts. Use `effectOptional:true` with a UI indication. |
| Thread safety | `ProcessFrame` runs on a Media Foundation worker thread, while `SetProperties` and `MapChanged` run on the writer's (UI) thread. | Use `Volatile` reads and writes of a single `float`. Do not lock on the audio thread. Ramp to avoid discontinuities. |
| Exceptions in the effect | Any exception thrown from the effect can fail the whole playback session. | Wrap the bodies of `ProcessFrame` and `SetEncodingProperties` in try/catch and fall back to pass-through. |
| Clipping | Positive gain combined with inter-sample peaks. | The backend caps true peak at −1.5 dBTP. Keep the clamp as a safety net. A soft limiter is optional. |
| Packaging | The reg-free manifest only works unpackaged. If the app becomes MSIX-packaged, activation needs `<Extension Category="windows.activatableClass.inProcessServer">` in `Package.appxmanifest` instead. | Document this. Test packaging before release if it is planned. |
| Resource management | MMF handles and `MediaFailed` subscriptions are never released. The `MediaPlayer` is disposed by the toolkit on unload. | Remove the MMF. Unsubscribe handlers. Do not keep references to a disposed native player. |
| Cross-platform | Android, iOS, and macOS currently get no normalization. | This is out of scope. Approach D is a natural later addition. |

---

## 10. Confidence levels

### Confirmed (direct evidence)

- The effect runs in-process. The app owns the reg-free manifest, the app is unpackaged, and the effect's debug output appears in the app's debugger.
- The app's toggle never reaches the effect: it writes only a static field and shared memory, and never the `PropertySet` (`PlaybackPage.xaml.cs:1192-1211`).
- Song switching reuses the first gain: the `PropertySet` is created once and never updated, and the toolkit reuses one native `MediaPlayer` (toolkit source).
- `0xC00D36B4` is `MF_E_INVALIDMEDIATYPE` (certutil), and the current code returns an empty supported-format list.
- The float-list version negotiated and processed audio (your logs at `ff64a57`).
- CsWinRT loads components in an isolated ALC by default (shim source, NuGet readme, generated runtimeconfig).
- The shared-memory writer remains in `PlaybackPage.xaml.cs`, and the effect-side reader is gone.

### Likely (strong inference, not yet observed directly)

- Two copies of `Filo.AudioEffects.Windows` are loaded, one per ALC, which separates the static fields. Step 2 of the plan confirms this in one run.
- Media Foundation creates a new effect instance for each new source and calls `SetProperties` with the stored `PropertySet`.
- The empty list itself causes `0xC00D36B4`. The documented behaviour (the effect is removed or fails when no format matches) and the control experiment both support this, but empty-list behaviour for audio is officially undocumented.
- The `EntryPointNotFoundException` comes from the split type identity in the isolated ALC (duplicate `Microsoft.Windows.SDK.NET` alongside a shared `WinRT.Runtime`) or from a stale or mixed build output.

### Unresolved (needs a test)

- Whether `MapChanged` fires inside the effect when the app mutates its `PropertySet`, meaning whether Media Foundation passes the same object or a proxy or copy. The per-frame `TryGetValue` fallback covers both cases.
- The exact missing member named in the `EntryPointNotFoundException` message. Capture the full `Message` if it recurs.
- Whether `LoadFromAssemblyPath` in the default-ALC mode reuses the assembly that the app's project reference already loaded, or throws because the same name is already loaded. Step 5 verifies this.
- Whether `MediaPlayer.Volume` is linear amplitude. This only matters for fallback approach B.
- Media Foundation's behaviour with sources whose rate or channel layout is outside the declared list (resample versus fail). Test with 96 kHz and 5.1 content if the library contains it.
