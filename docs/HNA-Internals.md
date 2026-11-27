# HNAUnlocker Internals & Reverse-Engineering Reference

This document records the engine behavior, binary patterns, and verified reference build details used by **HNAUnlocker** for **Honkai: Nexus Anima** (`NexusAnima.exe`).

All runtime hooks and addresses are resolved **dynamically** from the on-disk PE binaries and live process memory—no hardcoded RVAs are relied upon at runtime. The concrete RVAs below serve as a reference baseline so future game updates can be diagnosed and updated quickly.

---

## Reference Build

| Property | Verified Value |
|---|---|
| Target Executable | `NexusAnima.exe` (Honkai: Nexus Anima PC Beta `0.75.x`) |
| Unity Engine | `UnityPlayer.dll` — Unity `2022.3.14f1` (DX12) |
| Managed Runtime | IL2CPP (`GameAssembly.dll`) |
| `GameAssembly.dll` Timestamp | `0x6B07007C` |
| `GameAssembly.dll` `SizeOfImage` | `0x9347000` (`147,862,312` bytes on disk) |
| x64 Exception Directory (`DATA_DIRECTORY[3]`) | File-backed inside `.upx0` (`RVA = 0x8D5E3B0`, `Size = 0x4191F0`); unwind records in `.rdata`; hotfix bitmask guards in `.data` |

---

## 1. FPS Unlock (`GameBuild.cs`, `FrameCap.cs`)

### Mechanism
1. `GameBuildResolver` ([gui/Core/GameBuild.cs](../gui/Core/GameBuild.cs)) locates the IL2CPP internal call registration for `UnityEngine.Application::set_targetFrameRate` and `UnityEngine.Application::get_targetFrameRate` inside `GameAssembly.dll` and reads their live native function pointers into `UnityPlayer.dll`.
2. `FrameCapLocator` ([gui/Core/FrameCap.cs](../gui/Core/FrameCap.cs)) follows up to 4 unconditional `jmp rel32` (`0xE9`) thunks in `UnityPlayer.dll` and verifies both native accessors:
   - **Setter** (Unity 2022.3): `89 0D <disp32>` (`mov [rip+disp32], ecx`) followed by `E9` (`jmp`) or `C3` (`ret`).
   - **Getter**: `8B 05 <disp32>` (`mov eax, [rip+disp32]`) followed by `C3` (`ret`).
3. Both accessors must resolve to the exact same writable 4-byte static inside `UnityPlayer.dll`.
4. `UnlockSession` writes the clamped target FPS (`10–480`) to that static via `WriteProcessMemory`, verifies readback, and re-checks every `200 ms` to reapply if the game resets the cap. On stop/exit, the original value is restored.

---

## 2. FOV Adjustment (`GameBuild.cs`, `FovPatch.cs`)

### Mechanism
1. `GameBuildResolver` resolves the IL2CPP icall cache slots in `GameAssembly.dll`'s `.data` section for `UnityEngine.Camera::set_fieldOfView` and `UnityEngine.Camera::get_fieldOfView` and verifies their live `UnityPlayer.dll` targets.
2. `FovPatcher` ([gui/Core/FovPatch.cs](../gui/Core/FovPatch.cs)) allocates an `0x2000`-byte region in the game process (state page + executable wrapper page).
3. The native wrapper adjusts `xmm1` (the incoming vertical FOV float) by the configured delta (`-60°` to `+60°`), records diagnostics (`Calls`, `Camera`, `Input`, `Output`), and tail-jumps to the original `Camera::set_fieldOfView` implementation.
4. Installation is a single atomic 8-byte pointer swap on the `.data` icall cache slot—no `UnityPlayer.dll` code is modified and no native `Camera` struct offset is assumed.

---

## 3. Prevent Character Fading / AntiFade (`CameraFadeResolver.cs`, `AntiFadePatch.cs`, `ExecutableEntryPatch.cs`)

### HNA Rendering Architecture
Unlike Unity titles that fade characters through `MaterialPropertyBlock::SetFloatImpl`, Honkai: Nexus Anima uses a custom C++ `RenderObjectRegistry` (`ABCRP`) paired with a C# `CachedRendererDitherController`.

All character dither/fade channels flow through a single shared helper:

```csharp
CachedRendererDitherController::SetDither(this, int kind, float alpha)
```

- `rcx`: `CachedRendererDitherController* this`
- `edx`: `int kind` (`1..5`)
- `xmm2`: `float alpha` (`0.0f` = invisible, `1.0f` = fully opaque)

### Dither Kind Table

| `kind` (`edx`) | Role | AntiFade Action |
|---|---|---|
| `1` | Attachment / weapon parent-to-child dither copy | Untouched |
| `2` | Timed spawn / despawn / cutscene transitions | Untouched |
| `3` | **Camera elevation / low-angle pitch fade** (`AnimationCurve::Evaluate(pitch)`) | **Forced to `1.0f`** |
| `4` | **Camera proximity / distance fade** (also updates `_MainCharacterInDither` global) | **Forced to `1.0f`** |
| `5` | Secondary / companion dither copy | Untouched |

> **Important distinction (false positive ruled out during RE):**
> `0x4F29260` → `0x4F2BB10` (referencing `[camera + 0x190]`, which is `cameraPos.y`) is **water-surface / underwater camera clip dither**, not character proximity or elevation fade. Do not confuse it with the character-fade cohort.

### Verified 4-Function Cohort (`GameAssembly.dll` `0x6B07007C`)

`CameraFadeResolver` ([gui/Core/CameraFadeResolver.cs](../gui/Core/CameraFadeResolver.cs)) locates and cross-verifies all four functions using instruction patterns, inter-function `call rel32` edges, `1.0f` (`0x3F800000`) literals, struct field offsets, hotfix guard bytes, and x64 unwind tables:

| Role | Reference RVA | Notes |
|---|---|---|
| `Helper` (`CachedRendererDitherController::SetDither`) | `0x8B43B30` | Length `467` bytes, unwind RVA `0x1E2227C`. Entry hotfix guard at `RVA = 0x24F350E`, bitmask `0x80`. Controller field offset `+0x88`. |
| `Elevation` (Kind `3` caller) | `0x306D9F0` | Evaluates pitch curve (`movss xmm1, [rsp+0x44]; call [slot]; movaps xmm6, xmm0`) and calls `Helper` with `edx = 3`. |
| `Distance` (Kind `4` caller) | `0x76EDA10` | Camera-proximity fade; calls `Helper` with `edx = 4` and resets to `1.0f`. |
| `DistanceUpdate` | `0x76F0270` | Camera world-position bounds check calling `Distance`. |

### Hook Mechanics & Thread-Safe Live Patching
- **Displaced Prologue (12 bytes)** at `Helper`:
  ```asm
  56                      push rsi
  57                      push rdi
  53                      push rbx
  48 83 EC 60             sub rsp, 0x60
  0F 29 74 24 50          movaps [rsp+0x50], xmm6
  ```
- **Gateway & Unwind Metadata**:
  - `AntiFadePatcher` ([gui/Core/AntiFadePatch.cs](../gui/Core/AntiFadePatch.cs)) allocates an `0x2000`-byte region within `±2 GiB` of `GameAssembly.dll` (`+0x1000` leaf wrapper, `+0x1100` gateway).
  - The gateway replays the 12-byte prologue and jumps to `Helper + 12`.
  - `RemoteFunctionTable` ([gui/Core/RemoteFunctionTable.cs](../gui/Core/RemoteFunctionTable.cs)) registers the exact 16-byte x64 version-1 unwind record (`01 0C 06 00 0C 68 05 00 07 B2 03 30 02 70 01 60`) via `RtlAddFunctionTable` so SEH/stack unwinding across the gateway remains valid.
- **Leaf Wrapper Logic**:
  1. Checks `EnabledOffset` (`state + 0`).
  2. Checks `Helper`'s writable hotfix guard `(byte & mask) == 0` so live hotfixes are respected.
  3. If enabled and `edx == 3 || edx == 4`, loads `1.0f` into `xmm2` and increments `CameraCalls`; otherwise increments `OtherCalls` and leaves `xmm2` untouched.
  4. Tail-jumps to the gateway.
- **Atomic Entry Swap**:
  - `ExecutableEntryPatch` ([gui/Core/ExecutableEntryPatch.cs](../gui/Core/ExecutableEntryPatch.cs)) suspends all enumerated game threads, verifies no thread's `RIP` falls inside the 12-byte prologue, switches page protection via `VirtualProtectEx`, writes the 8-byte aligned entry word (`E9 <rel32> 0F 1F 00`), calls `FlushInstructionCache`, restores protection, and resumes all threads.
  - On stop/exit, the exact reverse swap restores the original 8 bytes cleanly.

---

## 4. Mobile UI (Dormant in `0.75.x`)

`MobileGameLauncher.cs` and `MobileLaunchSettings.cs` are ported to HNA's registry namespace and kept in the codebase for future releases, with the UI toggle disabled:

- **Registry Root**: `HKCU\SOFTWARE\miHoYo\ABC`
- **Launch Mechanism**: `WindowsEditor (7)` platform override + `-is_cloud 1 -platform_type CLOUD_WEB_TOUCH`
- **Why disabled**: The `0.75.x` PC beta build of `NexusAnima.exe` does not ship the `CloudGame` / `CloudInput` assemblies required for touch/mobile UI mode.

---

## 5. Live Attachment & Anti-Cheat Notes

- **Connect to game** works mid-session because FPS, FOV, and AntiFade all operate externally via `OpenProcess` (`PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_CREATE_THREAD`) without startup DLL injection.
- **Caveat**: If `HoYoProtect` (`HoYoKProtect.sys`) strips `VM_READ`/`VM_WRITE` permissions on an already-running process during a session, late attachment can fail with `Win32 error 5` (Access Denied). Launching the game through **Launch game** or restarting the client before attaching resolves this.
