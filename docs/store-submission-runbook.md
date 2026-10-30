# First Store submission: what has to happen, in what order

The packaging work is done: `build/build-msix.ps1` produces a `.msix` that was installed and run on
2026-10-30 (see "What the package was checked for" below). What remains is the one-time Partner
Center work that no API or CLI can do, three prerequisites that are not Partner Center work at all,
and a trap in the ordering.

Murmur goes on the same Partner Center account as Loadstone (Individual, publisher display name
**Morten Brudvik**). Everything after this first submission can be automated with `msstore` and
`microsoft/microsoft-store-apppublisher@v1.1`. See "After it is live" at the bottom.

## The trap: reserve the name *before* building the package you submit

**Done, 2026-10-30.** "Murmur" was taken, so the reserved name is **Murmur Voice Typing**, and the real
values are in `packaging/msix/AppxManifest.xml` (both `DisplayName`s equal the reserved name; the tile
`ShortName` and the app's own UI still say "Murmur"):

| Field | Value |
| --- | --- |
| `Package/Identity/Name` | `MortenBrudvik.MurmurVoiceTyping` |
| `Package/Identity/Publisher` | `CN=E6ED6F6D-88CB-42AC-BED3-B0FA24EC28DD` |
| `Package/Properties/PublisherDisplayName` | `Morten Brudvik` |
| Package family name | `MortenBrudvik.MurmurVoiceTyping_x7deb10xce5bj` |
| **Store ID** | `9N2FVKGMJQ5S` (<https://apps.microsoft.com/detail/9N2FVKGMJQ5S>) — needed by `msstore publish` once the app is live |

Partner Center assigns the real `Name` **when the name is reserved**, and the package must already
carry it when it is uploaded: a package with the placeholder is rejected on identity mismatch. So
the order is:

1. Reserve the name in Partner Center.
2. Read the identity values off **Product management → Product identity**.
3. Put them in `C:\code\projects\murmur\packaging\msix\AppxManifest.xml` (check the `Publisher`
   against it too, rather than assuming it matches Loadstone's), then rebuild:
   ```powershell
   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File C:\code\projects\murmur\build\build-msix.ps1
   ```
4. *Then* upload `installer-output\Murmur-<version>-x64.msix`.

`build-msix.ps1` prints a warning while the placeholder is still there. Do not ignore it.

## Prerequisites: three still open

These block a submission and none of them is Partner Center work:

| Missing | Why it blocks | What it needs |
| --- | --- | --- |
| ~~Privacy policy URL~~ **done** | Store policy 10.5.1 makes one mandatory for a desktop app regardless of what is collected, and Murmur uses the microphone | <https://brudvik.com/murmur/privacy> — live on the owner's homepage (`homepage-brudvik-com` repo, `public/murmur/privacy/index.html`); that file is the only copy, edit it there |
| At least one screenshot | Every listing requires one | **Not captured.** 1920×1080 PNG, into `docs/store/screenshots/`. They **must not show the owner's real dictation history**: the History page lists every transcript, and the packaged build reads the same `%LOCALAPPDATA%\Murmur` as the unpackaged one, so capture from a clean data folder or with entries made up for the purpose. The pill (recording and transcribing) and the Settings page make good second and third shots |
| ~~Identity values~~ **done** | See above | Reserved 2026-10-30 as "Murmur Voice Typing" |

## What to do in Partner Center

Sign in at <https://partner.microsoft.com/dashboard>.

### 1. Reserve the name

**Overview → New product → MSIX or PWA app.** Enter `Murmur`, check availability, reserve.

If `Murmur` is taken, **stop and ask**: the name appears in `AppxManifest.xml`, the main window's
title, the tray tooltip and the data folder, and changing it is a code change, not a listing choice. Do not silently
reserve a variant like "Murmur Voice Typing".

A reservation expires after three months if unused.

### 2. Record the identity values: this is the most important output

**Product management → Product identity.** Copy all four **exactly**, character for character; they
are case- and punctuation-sensitive:

- `Package/Identity/Name`
- `Package/Identity/Publisher`
- `Package/Properties/PublisherDisplayName`
- the **Store ID** (12 characters, e.g. `9NBLGGH4R315`): the ProductId that CI automation will need later

Bring these back before doing anything else. Steps 3 onward need the rebuilt package.

### 3. Upload the rebuilt package

**Packages.** Upload:

```
installer-output\Murmur-<version>-x64.msix
```

For a given version, `build-msix.ps1` overwrites the same file, so **the file sitting there right now
is the placeholder build** until it is rebuilt. Upload only the one rebuilt *after* the manifest was
updated with the real identity values. To be certain which build is on disk, check the manifest
inside it before uploading:

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$version = (Get-Content C:\code\projects\murmur\version.json -Raw | ConvertFrom-Json).version
$z = [System.IO.Compression.ZipFile]::OpenRead("C:\code\projects\murmur\installer-output\Murmur-$version-x64.msix")
$e = $z.GetEntry('AppxManifest.xml')
$r = New-Object System.IO.StreamReader($e.Open())
if ($r.ReadToEnd() -match 'PLACEHOLDER') { 'PLACEHOLDER BUILD - do not upload' } else { 'identity set - safe to upload' }
$r.Dispose(); $z.Dispose()
```

`installer-output/` is gitignored, so this file exists only on the build machine. Take the version
from `version.json` rather than assuming 1.0.0. The manifest's `Identity/@Version` is kept in step
with `version.json` by hand (there is no version-manager script); `build-msix.ps1` refuses to pack
when they disagree, and a new submission needs a higher version than the last one.

The package is about 243 MB, most of it the CUDA backend (`ggml-cuda-whisper.dll`, 147 MB
uncompressed), the Vulkan backend and the self-contained .NET runtime. That is well within Store
limits, and MSIX block-level differential updates mean later updates download only what changed.

The package is **unsigned on purpose**. The Store re-signs it. Do not sign it, and do not accept any
prompt to.

### 4. Store listing

- **Name:** Murmur
- **Short description:** Voice typing for Windows: hold a hotkey, talk, and the text lands in the
  app you are using. Speech recognition runs locally on your PC.
- **Description:** draw from the design spec (`docs/superpowers/specs/2026-10-29-murmur-design.md`,
  sections 2 and 5). Suggested bullets:
  - Hold a hotkey (Right Ctrl by default, configurable), talk, release: the text is pasted into
    whatever app has focus: a terminal, a browser, an editor, mail, chat.
  - Tap the hotkey for hands-free dictation; tap again to finish. Esc cancels.
  - Accurate English dictation with OpenAI's Whisper large-v3-turbo model, running on your own PC.
  - Vocabulary: hint words for names and jargon, and replacement rules ("web view two" →
    "WebView2").
  - History of every dictation, searchable, with one-click copy. Nothing is lost, even when a paste
    cannot happen.
  - A small pill shows while you speak and while it transcribes; it never takes focus and disappears
    the moment the text is in.
  - Your clipboard is put back after each paste, and Murmur's writes stay out of the Win+V clipboard
    history.
  - Private: no account, no cloud, no telemetry. Your voice and your text never leave your PC.
- **Required disclosures**, per policy 10.1.1 (important limitations). State each of these in the
  description:
  - **Cannot dictate into apps running as administrator.** While an elevated window has focus,
    Windows (UIPI) keeps the hotkey from reaching Murmur and blocks the paste; if focus moves to
    one mid-dictation, the text is left on the clipboard and in the history instead.
  - **Best speed needs an NVIDIA GPU with the CUDA 13 toolkit installed** (Murmur looks for the
    CUDA 13 runtime on PATH). Without it Murmur uses Vulkan or the CPU, which is slower; on Vulkan
    the first dictation after starting can take many seconds while the GPU code is prepared.
  - **First run downloads a speech model of about 1.6 GB** from huggingface.co (Hugging Face).
    Dictation is unavailable until it has finished; the main window shows the progress.
  - **English only.**
  - **A microphone is required.** Windows asks for permission the first time.
  - **Everything runs locally**: speech recognition, history, settings.
  - Requires the Microsoft Edge WebView2 Runtime for the main window. It is part of Windows 11 and
    current Windows 10; Murmur says so if it is missing.
- **Screenshots:** the ones captured above.
- **Privacy policy URL:** `https://brudvik.com/murmur/privacy`

### 5. Age rating

**Age ratings** → complete the IARC questionnaire. **The owner's answers, not an agent's.** Murmur
has no content of its own, no ads, no purchases, no accounts and no sharing between users; it turns
the user's own speech into text on the device. Answer honestly.

### 6. Restricted capability justification: read this one before pasting

**Submission options → Restricted capabilities.** The package declares `rescap:runFullTrust`, which
is mandatory for any packaged Win32 app and triggers human review.

This text is a statement to Microsoft under the publisher account. **It must be reviewed by the
owner before submission, not pasted by an agent unsupervised.** Unlike Loadstone's, it cannot say
"no input interception": Murmur intercepts input, and the text says so plainly. Every claim was
checked against the code on 2026-10-30 (`Murmur.Core/Input/InputHooks.cs`, `HotkeyMatcher.cs`,
`HotkeyCaptureCoordinator.cs`, `Murmur.UI/InputHookHost.cs`, `Murmur.Core/Insertion/*`,
`Murmur.Core/Audio/AudioRecorder.cs`, `Murmur.Core/Speech/ModelManager.cs`); re-check it if any of
those change before submission.

> Murmur is a packaged Win32 (WPF) voice-typing app that runs at medium integrity, as invoker. To
> detect the user's push-to-talk hotkey it installs a global low-level keyboard hook
> (SetWindowsHookEx, WH_KEYBOARD_LL) for as long as it runs, and a low-level mouse hook (WH_MOUSE_LL)
> only while the hotkey is held, so that a click or scroll made with the hotkey held cancels the
> recording. Both are low-level hooks serviced by Murmur's own thread; no DLL is injected into any
> other process. The hooks are used only to recognise the hotkey. The only input Murmur suppresses
> is: the main key of a key-combination hotkey (for example Space in Ctrl+Space; a hotkey that is a
> single modifier such as Right Ctrl is never suppressed); Esc while a dictation is in progress, which
> cancels it; and, while the user is recording a new hotkey on Murmur's Settings page, the keys
> pressed for it (that capture ends on the first valid key, on Esc, after 10 seconds, or when the
> window loses focus). Mouse input is never suppressed. Keystrokes are never stored, logged or
> transmitted; the only key Murmur saves is the hotkey the user picks, in its local settings file.
> After transcribing, Murmur uses SendInput to send Ctrl+V to the focused window and, when the
> hotkey is a single Alt or Windows key, an unassigned virtual key so that releasing it does not open
> a menu or the Start menu. It pastes through the clipboard: the previous clipboard contents are saved
> and put back about half a second later, and Murmur's own clipboard writes are excluded from
> clipboard history and cloud clipboard. It reads the name and elevation status of the foreground
> window's process (PROCESS_QUERY_LIMITED_INFORMATION) to label the history entry and to detect an
> elevated target, into which it does not paste. Microphone audio is captured (WASAPI) only while the
> hotkey is held or a hands-free recording is running, is transcribed on the device, and never leaves
> it. Murmur's only network request is a one-time download of the speech model from huggingface.co.

### 7. Pricing, availability, properties

**The owner's decisions, not an agent's.** Pricing, markets, release timing, category
(Productivity or Utilities & tools are both defensible) and the age rating answers above.

### 8. Submit

Then wait. A first submission declaring a restricted capability takes longer than a routine update,
because the justification is read by a person. A reviewer will launch Murmur from the Start menu:
it opens its main window, downloads the model on first run, and asks for the microphone the first
time the hotkey is used.

## What the package was checked for (2026-10-30, Windows 11 build 26200, Developer Mode on)

The staged layout was registered with `Add-AppxPackage -Register` and launched from its AUMID
(`explorer.exe shell:AppsFolder\<PackageFamilyName>!Murmur`), twice. To repeat it:

1. Stop any unpackaged Murmur first (`Stop-Process -Name Murmur.UI`). The single-instance mutex is
   shared across both channels, so a packaged copy started next to it only signals it and exits.
2. `Add-AppxPackage -Register C:\code\projects\murmur\packaging\msix\staging\AppxManifest.xml`, then
   `(Get-AppxPackage MortenBrudvik.MurmurVoiceTyping).PackageFamilyName` for the AUMID.
3. Afterwards: stop it, `Get-AppxPackage MortenBrudvik.MurmurVoiceTyping | Remove-AppxPackage`,
   and restart the unpackaged build. A registered layout keeps its staging folder in use, so
   `build-msix.ps1` cannot rebuild until the package is removed.

What was seen:

- **Logs and data are not redirected.** The packaged app wrote to the same
  `%LOCALAPPDATA%\Murmur\Logs\Murmur-<date>.log` as the unpackaged one, and read the same
  `settings.json`, history and model (no second 1.6 GB download). `%LOCALAPPDATA%\Packages\<PFN>\`
  held only registry-virtualisation files. This matches Loadstone's 2026-09-08 measurement. It also
  means **an uninstall leaves `%LOCALAPPDATA%\Murmur` behind, model included**; ordinary Win32
  behaviour, but worth a line in a future FAQ.
- The log says `Running from its MSIX package`, the input hooks installed, and the startup task was
  read and, because the owner's saved settings had Start with Windows on, enabled
  (`StartupTask.RequestEnableAsync` from a thread-pool thread, no prompt; the task's `State` read
  back as 2, Enabled). `AppInstance.GetActivatedEventArgs` reported `Launch` for a Start-menu style
  launch. A real sign-in start (`StartupTask` activation, which keeps Murmur in the tray) needs a
  sign-out and sign-in and was **not** measured.
- **CUDA.** The first run reached Ready on **Vulkan**, not CUDA: Windows' DLL search order for a
  packaged process leaves out PATH, so `ggml-cuda-whisper.dll` could not find the CUDA 13 runtime.
  `CudaRuntimeLoader` now loads `cudart64_13`, `cublasLt64_13` and `cublas64_13` by full path from
  PATH first; the second run reached Ready **on Cuda** (load 2.0 s, warm-up 0.25 s, against a 15 s
  warm-up on Vulkan).
- The microphone worked inside the package (the `microphone` device capability is what allows it).
  Windows keeps the user's consent under the package family name; the first capture took 8 s,
  consistent with the consent prompt being answered.
- Removing the package removed the startup task, the package data folder and the microphone consent
  entry.

Two packaging behaviours to know about:

- A Store copy and an unpackaged copy cannot run at the same time (the shared mutex), and they share
  `%LOCALAPPDATA%\Murmur`. With Start with Windows on in both, the Run key and the startup task both
  launch at sign-in; the second launch signals the first and exits.
- Uninstall runs no application code, so it cannot remove the HKCU Run value an unpackaged build
  may have written. That value belongs to the unpackaged build and is harmless to the Store copy.

## Do not

- **Do not sign the package.** The Store re-signs; a signature whose subject does not match the
  Partner Center publisher fails upload validation.
- **Do not invent identity values.** They come from Partner Center and nowhere else.
- **Do not reserve a different name** without asking: it is a code change.
- **Do not paste a justification other than the text above**, and do not paste that one without
  the owner's review. It describes what the code actually does, and it admits to input
  interception on purpose: an understated justification found out in review costs far more than a
  plain one.
- **Do not publish screenshots that show real dictation history.**
- **Do not change pricing, markets, or the age rating answers** on anyone's behalf.

## Bring back

1. The four identity values, verbatim.
2. The Store ID / ProductId.
3. Anything Partner Center rejected or warned about, quoted exactly rather than summarised.

## After it is live

Only then does automation work: the submission API creates each new submission as *a copy of the
last published one*, so a published baseline has to exist. Once it does:

- `msstore` CLI (`microsoft/msstore-cli`, actively maintained, still labelled preview)
- `microsoft/microsoft-store-apppublisher@v1.1` in GitHub Actions
  (`microsoft/store-submission` is **archived**; do not start there)
- Four secrets: tenant ID, client ID, client secret, seller ID. Needs an Entra app registration with
  the **Manager** role, associated under Account settings → Tenants. The Loadstone setup can be
  reused if it exists by then: it is per account, not per app.
- Ordering gotcha: `msstore publish` deletes the pending draft and recreates it from the last
  published submission, **discarding staged metadata**. Publish first, edit metadata after.
- Updates through the CLI/Action are supported for **free products only**. If Murmur is priced,
  updates stay manual.
- The capability approval carries forward: no need to redo the justification unless a *new*
  restricted capability is declared.
