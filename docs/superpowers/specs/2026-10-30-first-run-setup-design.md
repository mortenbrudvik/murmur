# Murmur — First-run setup and speech-engine dependencies (design)

**Date:** 2026-10-30
**Status:** Approved 2026-10-30
**Builds on:** `2026-10-29-murmur-design.md` (v1, shipped to Store certification as "Murmur Voice Typing" 1.0.0)
**Mockups:** `docs/superpowers/specs/mockups/first-run.html` — steps 1–3 and the Settings card (the owner approved the look).

---

## 1. Intent and why

Murmur 1.0 was built and tested on the owner's PC, which has the CUDA 13 toolkit and a fast GPU. A person who
installs it from the Microsoft Store has neither the toolkit nor any idea that a 1.6 GB model must download first.
Today they get:

- a silent 1.6 GB download, visible only as a thin banner in the main window and a tray tooltip, that restarts from
  zero if interrupted and hangs if the connection stalls;
- a silent fall-back to Vulkan (or the CPU) when CUDA is missing, with no hint that a free install would make it
  about twice as fast;
- on a PC with no usable GPU, the 1.6 GB model on the processor: several seconds per sentence, which feels broken.

**Goal of this work:** an end user goes from "installed" to "my first dictation worked" with no manual installs,
clear progress, and a gentle, one-time offer of the CUDA speed-up when — and only when — it applies.

**Success:** a first-time user on a fresh PC finishes setup without reading docs, and an NVIDIA owner without CUDA
learns, once, where to get it.

## 2. Measured facts this design rests on (owner's RTX 3090, 2026-10-30)

| | CUDA | Vulkan |
|---|---|---|
| 5-second sentence | ~0.17 s | ~0.3 s (up to 0.6 s) |
| Ready after Murmur starts | ~2 s | ~13 s |

The CUDA runtime Murmur needs is three DLLs from the CUDA 13 toolkit: `cudart64_13.dll` (0.5 MB), `cublas64_13.dll`
(49 MB), `cublasLt64_13.dll` (435 MB). NVIDIA's EULA allows redistributing them, but **this design neither bundles
nor downloads them** (owner's decision): bundling would add ~485 MB to every user's download, and downloading native
code after install is a Microsoft Store policy risk (10.2.2, dynamic inclusion of code). Murmur links to NVIDIA's
installer instead.

## 3. Goals

1. **No manual installs needed.** Vulkan (bundled) or the CPU always works; CUDA is used automatically when present.
2. **First-run setup** in Murmur's own window — three steps: *Speech model*, *Microphone*, *Try it* (§5).
3. **CUDA detection with a download link** (owner's decision, §6): only for NVIDIA GPUs where CUDA would help.
4. **Hardware-aware model choice** (owner's decision): GPU → large-v3-turbo; processor only → small English model
   (§7). Switchable in Settings.
5. **A robust download**: resume, retry, stall detection, disk-space check, checksum verification (§8).

## 4. Non-goals and anti-goals

**Non-goals:** bundling or downloading CUDA DLLs; models other than the two in §7; non-English models; a "Restart
needed" flow for anything other than CUDA; the 1.0.1 bug list (pill delay on Bluetooth, Right Ctrl+C opening the mic,
etc.) — separate work.

**Anti-goals (failure even if it "works"):**
- A blocking splash or modal the user must sit through. Setup is closable at every step; work continues in the
  background.
- Nagging: the CUDA tip appears once in setup ("Not now" dismisses it there) and then only on the Settings engine
  card — never a popup on each start.
- Re-downloading 1.6 GB after an interruption, leaving partial or corrupt model files that are later loaded, or
  filling the disk.
- Claiming a speed-up that won't happen: no CUDA tip without an NVIDIA GPU, and a "driver update" tip instead when
  the driver can't run CUDA 13.
- Losing the owner's existing setup: an upgrade from 1.0 with a valid model already present skips setup entirely.

## 5. First-run setup (mockup `first-run.html`)

Shown in the main window (React page, sidebar hidden) on first run — when setup has never been completed **and**
the active model is not present and valid. Not shown to upgraders whose model is already valid (mark setup complete).
A sign-in start (`--minimized` or the Store startup task) stays in the tray even during first run; the download
still starts, the tray tooltip shows its progress, and the setup page appears the next time the user opens the window.

1. **Speech model** — "Getting Murmur ready": model name and size, progress bar, MB downloaded / total, speed,
   time left, "Resumes if interrupted". Below it, the CUDA tip when §6 says so. Footer: "You can close this window —
   Murmur keeps downloading." **Next** is enabled immediately (the user can do steps 2–3 while it downloads; step 3
   waits for the model, showing "Waiting for the speech model…").
2. **Microphone** — mic picker (same list as Settings), live level meter, permission state. If Windows denies
   microphone access: explain and offer a button that opens `ms-settings:privacy-microphone`. The Bluetooth hint line
   from the mockup ("wait for the start sound") always shows; it's cheap and true.
3. **Try it** — a practice text box inside the window; "hold Right Ctrl (the configured hotkey), say a sentence, let
   go". Success = a dictation completes while this step is open (a `history.added` event); show the check mark and
   the hold / tap / Esc basics. **Done** marks setup complete and shows History.

**When the model becomes ready and the main window is not in the foreground**, show a Windows notification through
the existing tray icon: "Murmur is ready — hold Right Ctrl to talk" (the configured hotkey's name). Download
failures that need the user (disk full, no network after retries) also notify once.

Setup progress (current step, whether the CUDA tip was dismissed, setup complete) persists in settings so a restart
resumes where the user left off.

## 6. CUDA detection and the tip

At start, before the engine loads, determine a **GPU advice** state:

| State | Condition | What the user sees |
|---|---|---|
| `CudaInUse` | engine loaded the CUDA runtime | Engine card: "Ready · CUDA". No tip. |
| `InstallCuda` | **supported** NVIDIA GPU, driver supports CUDA 13, runtime DLLs not found | Tip: "Your NVIDIA <GPU name> can run Murmur about twice as fast" + **Get CUDA 13** (opens `https://developer.nvidia.com/cuda-downloads`) + **Restart Murmur** (Settings only, or after the link was clicked) |
| `UpdateDriver` | **supported** NVIDIA GPU, driver too old for CUDA 13 | Tip: "Update your NVIDIA driver to use CUDA" + **Update NVIDIA driver** (opens `https://www.nvidia.com/Download/index.aspx`) |
| `NotApplicable` | no NVIDIA GPU, an NVIDIA GPU Murmur's CUDA build can't run, `nvcuda.dll` missing, or CUDA runtime found but the engine still ended on Vulkan/CPU | No tip (the last case writes a log line) |

**"Supported" NVIDIA GPU = compute capability 8.6 or newer** (RTX 30, 40, 50 series and newer). Verified by the builder
check against the shipped `ggml-cuda-whisper.dll` (Whisper.net.Runtime.Cuda.Windows 1.9.1, inspected with `cuobjdump`):
it carries native code only for `sm_86`, `sm_89`, `sm_120a`, `sm_121a`; `sm_75`/`sm_80`/`sm_90` only as PTX (driver JIT,
unmeasured, possibly a very slow first load), and nothing for Maxwell/Pascal/Volta (GTX 9xx/10xx), whose drivers still
report CUDA 13. Turing (RTX 20 / GTX 16) is excluded until someone measures it. Re-check this list whenever the
Whisper.net version changes.

**CUDA is only attempted on a supported GPU.** Without one, `RuntimeOptions.RuntimeLibraryOrder` drops `Cuda` (Vulkan,
then CPU), so a PC with an older NVIDIA card *and* a CUDA toolkit installed never ends up on a CUDA backend that has no
kernels for its GPU.

Detection details (builder may refine, the states must not change):
- **NVIDIA GPU and its name:** enumerate DXGI adapters; NVIDIA vendor id `0x10DE`; skip software adapters.
- **Driver's CUDA version and the GPU's compute capability:** load `nvcuda.dll` (ships with the NVIDIA driver in
  System32), `cuInit(0)`, `cuDriverGetVersion` (≥ 13000 means CUDA 13 capable), and per device
  `cuDeviceGetAttribute` for compute capability major/minor, matched to the DXGI adapter by LUID (`cuDeviceGetLuid`)
  when there is more than one. Missing `nvcuda.dll` → `NotApplicable` (the compute capability can't be known).
- **Runtime present:** extend `CudaRuntimeLoader`'s search beyond the process PATH: also the machine and user `Path`
  from the registry (an install made while Murmur runs is visible to the next start even if Explorer's environment
  is stale), `CUDA_PATH` from the registry, and `%ProgramFiles%\NVIDIA GPU Computing Toolkit\CUDA\v13.*\bin\x64` and
  `\bin`. The engine's actual runtime (`RuntimeOptions.LoadedLibrary`) decides `CudaInUse`.
- **Restart Murmur:** relaunches Murmur and quits this instance (one click; the window reopens on Settings). CUDA can
  only be picked up at process start.

The tip shows in setup step 1 (dismissible with "Not now", remembered) and permanently on the Settings engine card
until the state is `CudaInUse` or `NotApplicable`.

## 7. Model choice

| Model | File | Size | Used when |
|---|---|---|---|
| Best accuracy | `ggml-large-v3-turbo.bin` | 1,624,555,275 bytes (~1.6 GB) | a usable GPU is present (default) |
| Faster | `ggml-small.en.bin` | 487,614,201 bytes (~488 MB) | processor only (default) |

- **"Usable GPU"**: a non-software DXGI adapter with at least 4 GB of dedicated video memory (large-v3-turbo needs
  roughly 2–3 GB). Anything else → small English model. *(Builder may tune the threshold; document it.)*
- **Settings → Speech model**: "Best accuracy (large, 1.6 GB)" / "Faster (small, 488 MB)" with the automatic choice
  marked "recommended for this PC". Switching downloads the other model with the same progress UI (on the Settings
  card), then loads it; the previous model is deleted once the new one is loaded, to save disk.
- Both come from Hugging Face, **pinned to a commit** so the pinned checksums stay valid:
  `https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/<file>` (the commit
  `resolve/main` pointed at on 2026-10-30). SHA-256 for large-v3-turbo:
  `1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69` (equals the owner's existing 1.0 model file, so his
  install skips setup). The builder pins small.en's from the same commit's LFS metadata. Language stays English; the
  vocabulary prompt works for both.
- The engine status gains the active model's display name ("Whisper large-v3-turbo" / "Whisper small (English)").

## 8. Robust download

- **Resume:** keep `<model>.part`; on restart send `Range: bytes=<partSize>-`; if the server ignores the range
  (200 instead of 206), start over.
- **Retry:** transient failures (network, 5xx, timeouts) retry with backoff (e.g. 2 s, 5 s, 15 s, then every 60 s)
  without user action; progress shows "Reconnecting…".
- **Stall detection:** no bytes for 30 s → abort that attempt and retry (resuming).
- **Disk space:** before starting, require free space ≥ remaining bytes + 10 %; otherwise status Error with the
  amount needed, and a notification.
- **Verify:** SHA-256 of the finished file must match a pinned value per model (take Hugging Face's LFS sha256 for
  each file at build time and pin it in code). Mismatch → delete and download again once; a second mismatch is an
  Error.
- **Never load** a `.part` or an unverified file. An already present model is verified once (cached result keyed on
  size + timestamp) so start-up does not re-hash 1.6 GB every time.
- Progress events carry bytes downloaded, total, speed and ETA (the UI formats them).

## 9. Where things live (guidance, not prescription)

- Core: GPU detection (`IGpuInfo`), CUDA runtime search (extend `CudaRuntimeLoader`), model catalog + selection,
  download engine (extend `ModelManager`), engine status extended with GPU advice + model + download details.
- IPC: setup state get/update, model choice get/set, "open URL" (CUDA/driver/privacy settings), "restart Murmur",
  push events for download progress (existing `engine.statusChanged` carries it).
- UI: tray notification on ready/failure (H.NotifyIcon notification); restart.
- WebApp: Setup page (3 steps) + Settings engine card (tip, model choice, restart).
- Packaging: none beyond the version bump; no new capabilities (internetClient already declared).

## 10. Release

Ships as **1.1.0** (bump `version.json` and the manifest together). Store listing: add the one-time setup and the
smaller model to the description's "Good to know"; the CUDA sentence stays.

## 11. Done when

- A fresh user profile (no settings, no model) shows setup, downloads with live speed/ETA, survives killing Murmur
  mid-download (resumes, does not restart from 0), verifies the file, notifies when ready, and the practice dictation
  succeeds.
- Pulling the network mid-download shows "Reconnecting…" and recovers when the network returns; a full disk is
  reported with the amount needed.
- On the owner's PC (CUDA installed): no tip, "Ready · CUDA". With the CUDA directories hidden: `InstallCuda` tip with
  the RTX name; after "Restart Murmur" with CUDA restored: "Ready · CUDA".
- On a PC without NVIDIA: no tip. On a processor-only PC (or simulated): small model chosen by default.
- An existing 1.0 install with a valid model never sees setup.
- Unit tests for: advice-state decision table, runtime search order, model choice threshold, download resume/retry/
  stall/checksum/disk-space logic (fake HTTP), setup-state persistence; web tests for the setup steps and tip
  variants.

## 12. Decisions record

**Owner:** setup instead of a splash (approved mockups); Vulkan by default, no CUDA bundling or downloading; CUDA
detection with a download link; automatic model choice by hardware with a Settings override.

**My calls (owner can override):** setup lives in the main window and is closable; sign-in starts stay in the tray;
CUDA tip once in setup + permanently on the Settings card; "usable GPU" = ≥ 4 GB dedicated VRAM; small model is
`small.en`; previous model deleted after a switch; resume/retry/stall/verify behaviour and timings; version 1.1.0;
driver-update tip when the driver can't run CUDA 13; **CUDA tip and CUDA backend only for compute capability ≥ 8.6**
(Turing excluded until measured; older NVIDIA cards get no tip and never try CUDA); no tip, just a log line, when CUDA
DLLs are present but the engine still lands on Vulkan; model URLs pinned to Hugging Face commit `5359861c…`.
