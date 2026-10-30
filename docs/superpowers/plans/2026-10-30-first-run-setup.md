# First-run Setup and Speech-engine Dependencies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Store user goes from "installed" to "my first dictation worked" with no manual installs: a closable three-step setup in the main window, a resumable and verified model download, a hardware-aware model choice, and a one-time CUDA tip only where CUDA would help.

**Architecture:** Core gains GPU detection (DXGI + `nvcuda.dll`), a pure advice decision, a model catalog and selection, a rewritten `ModelManager` (resume, retry, stall, disk check, SHA-256), and a `WhisperEngine` whose orchestration is testable behind a model-loader seam. New MediatR requests (`setup.*`, `models.*`, `app.*`) carry state to React; `engine.statusChanged` carries download details and GPU advice. The WPF shell adds tray notifications, restart, and a microphone level test.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.26100.0`), WPF + WPF-UI, Autofac, MediatR 12.5.0, Serilog, H.NotifyIcon.Wpf, NAudio, Whisper.net 1.9.1; React 19 + Vite + TypeScript + Vitest; xUnit v3 + Shouldly + NSubstitute + FakeTimeProvider.

**Spec:** `docs/superpowers/specs/2026-10-30-first-run-setup-design.md` (mockups: `docs/superpowers/specs/mockups/first-run.html`)

## Global Constraints

- CLAUDE.md conventions apply: file-scoped namespaces, `_camelCase` fields, `ConfigureAwait(false)` in Core, `Log.ForContext<T>()`, all JSON through `JsonUtils`, test names `MethodName_Scenario_ExpectedResult`, `TreatWarningsAsErrors` (zero warnings), no logic in code-behind, constructor injection only.
- No new NuGet or npm packages. DXGI and `nvcuda.dll` are reached with `LibraryImport`/`GeneratedComInterface`/function pointers (`AllowUnsafeBlocks` is already on in Murmur.Core). MediatR stays at 12.5.0.
- CUDA DLLs are neither bundled nor downloaded. Murmur only links to NVIDIA.
- Model URLs are pinned: `https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/<file>`.
- large-v3-turbo: `ggml-large-v3-turbo.bin`, 1,624,555,275 bytes, SHA-256 `1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69`.
- small.en: `ggml-small.en.bin`, 487,614,201 bytes, SHA-256 `c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d` (from the same commit's LFS pointer, fetched 2026-10-30).
- "Supported" NVIDIA GPU means compute capability ≥ 8.6. "CUDA 13 capable driver" means `cuDriverGetVersion` ≥ 13000.
- Links the web page may open are exactly these three (an enum, never a URL from JS): `https://developer.nvidia.com/cuda-downloads`, `https://www.nvidia.com/Download/index.aspx`, `ms-settings:privacy-microphone`.
- No modal or blocking splash. Every setup step can be closed, and work continues in the background.
- Ship as 1.1.0 (`version.json` `1.1.0`, manifest `1.1.0.0`).
- Every commit message ends with these two lines:
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
  `Claude-Session: https://claude.ai/code/session_01GCFQNk9pYfGknhGY6zaane`

## Review Focus

1. **Saving any setting from the Settings page wipes setup progress or the model choice.** `UpdateSettingsHandler` today builds a fresh `MurmurSettings` from the request's five fields, so it would reset `SetupCompleted`, `SetupStep`, `CudaTipDismissed` and `SpeechModel`. Expected: they survive. Test `Handle_KeepsSetupAndModelFields` (Task 1).
2. **A "Murmur is ready" notification on every sign-in start** (nagging). Expected: ready is announced only after a download in this run, and only when the window is not active. Test `OnStatus_ReadyWithoutADownloadThisRun_NoNotice` (Task 8).
3. **The microphone stays open after leaving step 2** (Windows keeps showing the mic-in-use icon). Expected: the test stops when the step unmounts, when the window hides, and after 120 s at most. Tests `Start_After120Seconds_StopsByItself` (Task 7) and the `stopMicTest` call on unmount (Task 10).
4. **Restart loses the single-instance race.** The new process starts while the old one still holds the mutex, signals it, and exits, so Murmur disappears. Expected: a `--restarted` start waits for the mutex. Test `TryAcquire_MutexReleasedWithinTheWait_Succeeds` (Task 8).
5. **Speed and ETA before the first second of data** render "Infinity minutes left" or "0 MB/s". Expected: no speed and no ETA until known. Tests `formatRemaining(null|Infinity) → null` and `formatSpeed(0) → null` (Task 9).

---

## File Structure

**Murmur.Core**
- `Models/DomainModels.cs` (modify): new enums `SpeechModelId`, `SetupStep`, `GpuAdviceState`, `LinkTarget`, `MicTestState`; records `DownloadProgress`, `GpuAdvice`, `SetupState`, `ModelChoice`, `SpeechModelOption`, `MicTestResult`; new `MurmurSettings` fields; reshaped `EngineStatus` (Task 5).
- `Services/ISettingsService.cs`, `Services/SettingsService.cs` (modify): atomic `Update`.
- `Services/IDiskSpace.cs`, `Services/DiskSpace.cs` (create).
- `Speech/SpeechModelCatalog.cs` (create): pinned model metadata and option labels.
- `Speech/IModelManager.cs` (create), `Speech/ModelManager.cs` (rewrite), `Speech/ModelDownloadErrors.cs` (create).
- `Speech/ModelSelection.cs` (create): recommend, resolve and persist the active model.
- `Hardware/GpuReport.cs`, `Hardware/IGpuInfo.cs`, `Hardware/GpuInfo.cs`, `Hardware/GpuAdvisor.cs` (create).
- `Speech/CudaRuntimeSearch.cs`, `Speech/CudaRuntimeLocator.cs` (create); `Speech/CudaRuntimeLoader.cs` (modify).
- `Speech/IWhisperModelLoader.cs`, `Speech/WhisperModelLoader.cs`, `Speech/DownloadReportThrottle.cs` (create); `Speech/WhisperEngine.cs`, `Speech/ISpeechEngine.cs` (modify).
- `Setup/SetupCoordinator.cs`, `Setup/LaunchOptions.cs` (create).
- `Services/ILinkOpener.cs`, `Services/IAppLifecycle.cs`, `Services/AppLinks.cs` (create).
- `Audio/MicrophoneLevelMonitor.cs` (create).
- `Features/Setup/*`, `Features/Models/*`, `Features/App/*` (create); `Features/Settings/UpdateSettingsRequest.cs` (modify).

**Murmur.Interop:** `Handlers/MicLevelEventHandler.cs` (create).

**Murmur.UI:** `Tray/TrayIconHost.cs` (modify), `Tray/TrayNotifier.cs` (create), `ShellLinkOpener.cs`, `AppLifecycle.cs`, `LaunchArguments.cs` (create), `SingleInstanceGuard.cs`, `App.xaml.cs`, `AppModule.cs`, `MainWindow.xaml.cs` (modify).

**WebApp (`src/WebApp/src`):** `types/domain.ts` (modify); `features/settings/formatDownload.ts`, `CudaTip.tsx`, `CudaTip.css`, `SpeechModelSection.tsx`, `useModelChoice.ts`, `appActions.ts` (create); `engineStatusText.ts`, `EngineStatusLine.tsx`, `SettingsPage.tsx` (modify); `features/setup/SetupPage.tsx`, `SetupPage.css`, `ModelStep.tsx`, `MicrophoneStep.tsx`, `TryItStep.tsx`, `useSetupState.ts`, `useMicLevel.ts` (create); `App.tsx` (modify).

**Release:** `version.json`, `packaging/msix/AppxManifest.xml`, `README.md`, `docs/store-submission-runbook.md` (modify).

---

### Task 1: Settings fields, atomic settings update, model catalog

**Files:**
- Modify: `src/Murmur.Core/Models/DomainModels.cs`, `src/Murmur.Core/Services/ISettingsService.cs`, `src/Murmur.Core/Services/SettingsService.cs`, `src/Murmur.Core/Features/Settings/UpdateSettingsRequest.cs`
- Create: `src/Murmur.Core/Speech/SpeechModelCatalog.cs`
- Test: `tests/Murmur.Core.Tests/Speech/SpeechModelCatalogTests.cs` (create), `tests/Murmur.Core.Tests/Services/JsonStoresTests.cs`, `tests/Murmur.Core.Tests/Features/SettingsHandlerTests.cs` (modify)

**Interfaces:**
- Produces (`DomainModels.cs`; JSON enums are camelCase strings through `JsonUtils`):
  ```csharp
  public enum SpeechModelId { LargeV3Turbo, SmallEn }
  public enum SetupStep { SpeechModel, Microphone, TryIt }
  public enum GpuAdviceState { NotApplicable, CudaInUse, InstallCuda, UpdateDriver }
  public enum LinkTarget { CudaDownload, NvidiaDriver, MicrophonePrivacy }
  public enum MicTestState { Listening, AccessDenied, Failed }

  public sealed record MurmurSettings(
      HotkeyDefinition Hotkey, string? MicrophoneId = null, bool SoundCues = true, bool StartWithWindows = false,
      bool KeepAudio = false,
      SpeechModelId? SpeechModel = null,   // null until first resolved (Task 3)
      SetupStep? SetupStep = null,         // null = first-run decision not made yet (Task 6)
      bool SetupCompleted = false,
      bool CudaTipDismissed = false);

  /// Retries = consecutive failed attempts of the current download (0 while bytes flow).
  public sealed record DownloadProgress(string ModelName, long BytesReceived, long TotalBytes, double BytesPerSecond,
      double? SecondsRemaining, int Retries)
  { public bool Reconnecting => Retries > 0; }      // serialized, the web reads it

  public sealed record GpuAdvice(GpuAdviceState State, string? GpuName)
  { public static GpuAdvice None { get; } = new(GpuAdviceState.NotApplicable, null); }

  public sealed record SetupState(bool ShowSetup, SetupStep Step, bool CudaTipDismissed, string? OpenPage);
  public sealed record SpeechModelOption(SpeechModelId Id, string Label, long SizeBytes);
  public sealed record ModelChoice(SpeechModelId Selected, SpeechModelId Recommended, IReadOnlyList<SpeechModelOption> Options);
  public sealed record MicTestResult(MicTestState State, string? Error = null);
  ```
- Produces (`ISettingsService`): `MurmurSettings Update(Func<MurmurSettings, MurmurSettings> change)`. It runs load, change and save under one lock, applies the existing invalid-hotkey repair from `Load`, and returns the saved settings.
- Produces (`Speech/SpeechModelCatalog.cs`):
  ```csharp
  public sealed record SpeechModelInfo(SpeechModelId Id, string DisplayName, string FileName, long SizeBytes, string Sha256, Uri Url);
  public static class SpeechModelCatalog
  {
      public static SpeechModelInfo LargeV3Turbo { get; }
      public static SpeechModelInfo SmallEn { get; }
      public static SpeechModelInfo Get(SpeechModelId id);
      public static IReadOnlyList<SpeechModelOption> Options { get; } // large first
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
// SpeechModelCatalogTests
[Fact] public void Get_LargeV3Turbo_IsPinnedToTheCommit()
{
    var m = SpeechModelCatalog.Get(SpeechModelId.LargeV3Turbo);
    m.DisplayName.ShouldBe("Whisper large-v3-turbo");
    m.FileName.ShouldBe("ggml-large-v3-turbo.bin");
    m.SizeBytes.ShouldBe(1_624_555_275);
    m.Sha256.ShouldBe("1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69");
    m.Url.ShouldBe(new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo.bin"));
}
[Fact] public void Get_SmallEn_IsPinnedToTheCommit()
{
    var m = SpeechModelCatalog.Get(SpeechModelId.SmallEn);
    m.DisplayName.ShouldBe("Whisper small (English)");
    m.FileName.ShouldBe("ggml-small.en.bin");
    m.SizeBytes.ShouldBe(487_614_201);
    m.Sha256.ShouldBe("c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d");
    m.Url.ShouldBe(new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-small.en.bin"));
}
[Fact] public void Options_LabelsMatchTheSpec()
{
    SpeechModelCatalog.Options.Select(o => (o.Id, o.Label)).ShouldBe([
        (SpeechModelId.LargeV3Turbo, "Best accuracy (large, 1.6 GB)"),
        (SpeechModelId.SmallEn, "Faster (small, 488 MB)")]);
}

// JsonStoresTests
[Fact] public void SettingsService_LoadVersion10File_DefaultsTheNewFields()
// write {"hotkey":{"virtualKey":163,"modifiers":"none","displayName":"Right Ctrl"},"microphoneId":null,
//        "soundCues":true,"startWithWindows":false,"keepAudio":false} to SettingsFile, then Load():
//   SpeechModel.ShouldBeNull(); SetupStep.ShouldBeNull(); SetupCompleted.ShouldBeFalse(); CudaTipDismissed.ShouldBeFalse()
[Fact] public void SettingsService_SaveThenLoad_RoundTripsTheNewFields()
// SpeechModel SmallEn, SetupStep TryIt, SetupCompleted true, CudaTipDismissed true survive a round trip
[Fact] public async Task SettingsService_UpdateFromManyThreads_LosesNoChange()
// 50 parallel Update(s => s with { MicrophoneId = ((int.Parse(s.MicrophoneId ?? "0")) + 1).ToString() })
//   → Load().MicrophoneId.ShouldBe("50")

// SettingsHandlerTests (Review Focus 1)
[Fact] public async Task Handle_KeepsSetupAndModelFields()
// stored: Previous settings with SpeechModel SmallEn, SetupStep Microphone, SetupCompleted true, CudaTipDismissed true;
// handle an UpdateSettingsRequest that changes only SoundCues → the saved settings still carry all four values
```

The existing `SettingsHandlerTests` stub `Load`/`Save`. Make the `ISettingsService` substitute's `Update` apply the function to the stored settings and record the result: `_settings.Update(Arg.Any<Func<MurmurSettings, MurmurSettings>>()).Returns(ci => saved = ci.Arg<Func<MurmurSettings, MurmurSettings>>()(stored))`.

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/Murmur.Core.Tests --filter "FullyQualifiedName~SpeechModelCatalogTests|FullyQualifiedName~JsonStoresTests|FullyQualifiedName~SettingsHandlerTests"`
Expected: a compile failure (`SpeechModelCatalog`, `SpeechModelId`, `Update` missing).

- [ ] **Step 3: Implement**

Add the types above. `SettingsService.Update` locks a private `Lock`. `UpdateSettingsHandler` calls `settingsService.Update(current => current with { Hotkey = …, MicrophoneId = …, SoundCues = …, StartWithWindows = …, KeepAudio = … })`. Keep its existing hotkey and blank-microphone rules, and publish `SettingsUpdatedNotification` with the returned settings.

- [ ] **Step 4: Run the tests and watch them pass**

Run: the Step 2 command, then `dotnet build Murmur.sln -p:SkipFrontendBuild=true`.
Expected: all PASS, and the build reports 0 warnings.

- [ ] **Step 5: Commit**

`git add` the files above, then `git commit -m "feat(core): settings fields for setup and model choice, atomic settings update, pinned model catalog"` with the trailer lines.

---

### Task 2: Robust model download

**Files:**
- Create: `src/Murmur.Core/Speech/IModelManager.cs`, `src/Murmur.Core/Speech/ModelDownloadErrors.cs`, `src/Murmur.Core/Services/IDiskSpace.cs`, `src/Murmur.Core/Services/DiskSpace.cs`
- Rewrite: `src/Murmur.Core/Speech/ModelManager.cs`
- Modify: `src/Murmur.Core/Services/MurmurPaths.cs` (add `ModelVerificationFile` = `models/verified.json`), `src/Murmur.UI/AppModule.cs` (register `DiskSpace`, `IModelManager`)
- Test: `tests/Murmur.Core.Tests/Speech/ModelManagerTests.cs` (rewrite)

**Interfaces:**
- Consumes: `SpeechModelInfo`, `DownloadProgress` (Task 1).
- Produces:
  ```csharp
  public interface IDiskSpace { long GetAvailableFreeSpace(string directory); }
  public interface IModelManager
  {
      string PathFor(SpeechModelInfo model);
      bool HasExpectedFile(SpeechModelInfo model);        // exists with the catalog size; no hashing
      Task<string> EnsureModelAsync(SpeechModelInfo model, IProgress<DownloadProgress> progress, CancellationToken ct);
      void Delete(SpeechModelInfo model);                 // model file, .part, verification-cache entry; never throws for a missing file
  }
  public sealed class ModelManager(MurmurPaths paths, HttpClient http, TimeProvider time, IDiskSpace disk) : IModelManager
  {
      public static TimeSpan StallTimeout { get; }        // 30 s
      public static TimeSpan RetryDelay(int retry);       // 1→2 s, 2→5 s, 3→15 s, ≥4→60 s
  }
  public sealed class InsufficientDiskSpaceException(long requiredBytes, long availableBytes, string directory) : IOException
  public sealed class ModelVerificationException(string message) : IOException
  ```

`EnsureModelAsync` works as follows:
1. **A present file** is used once it is verified. The verification cache is a `JsonFileStore` at `ModelVerificationFile`, keyed by file name and holding `{ size, lastWriteUtc, sha256 }`. A hit with the same size and timestamp and the pinned SHA counts as verified without hashing. Anything else re-hashes the file. On a mismatch the file is deleted and downloaded again.
2. **A `.part` whose length equals the model size** is verified directly, without a request. A `.part` longer than the model is deleted.
3. **Disk check:** free space must be at least `(size − partLength) × 1.1`, rounded up. Otherwise throw `InsufficientDiskSpaceException` with that requirement.
4. **Request:** `GET` with `Range: bytes=<partLength>-` when a part exists. A 206 response appends. A 200 response truncates and starts over. A 416 response deletes the part and retries at once without counting a retry. Any other non-success status follows the rules in step 5.
5. **Transient failures** retry forever, waiting `RetryDelay(retry)` on the injected `TimeProvider`: `HttpRequestException` without a status, 5xx, 408, 429, `IOException` while reading the response stream, and a stall. Every retry reports progress with `Retries = n` before it waits. A 4xx other than 408 and 429 throws `HttpRequestException` and does not retry. An `IOException` from writing the target file is not transient: let it propagate.
6. **Stall:** before every `ReadAsync`, call `CancelAfter(StallTimeout)` on a `CancellationTokenSource` built with the injected `TimeProvider` and linked to `ct`. If it fires while `ct` is not cancelled, that is a stall.
7. **Verify:** hash the finished `.part` (SHA-256, lowercase hex). On a match, move it over the model file and record it in the cache. On a mismatch, delete it and download once more from zero. A second mismatch throws `ModelVerificationException`.
8. **Progress** reports after every write: `BytesReceived` (including the part), `TotalBytes` = catalog size, `BytesPerSecond` averaged over the bytes received in the last ≥ 1 s of this attempt (measured on the `TimeProvider`, 0 until the first second has passed), `SecondsRemaining` = remaining ÷ speed (null while speed is 0), and `Retries` = 0 once bytes flow again.
9. **Cancellation keeps the `.part`** so the next start resumes. This changes behaviour: the old test `Cancelled_Throws_LeavesNoFiles` is replaced. Log `"Resuming the {Model} download at {Bytes:N0} bytes"` when a part is reused.

- [ ] **Step 1: Write the failing tests**

Reuse the existing `FakeHandler` and temp-root pattern. The test model is `new SpeechModelInfo(SpeechModelId.SmallEn, "Test", "test.bin", body.Length, Sha256Hex(body), new Uri("https://example.test/test.bin"))`. Use `FakeTimeProvider` and a substitute `IDiskSpace` that returns `long.MaxValue` unless a test says otherwise.

```csharp
[Fact] public async Task EnsureModelAsync_Missing_DownloadsVerifiesAndRenames()      // file == body, no .part, last progress BytesReceived == size
[Fact] public async Task EnsureModelAsync_PartFileExists_SendsRangeAndAppends()      // .part = body[..400]; request has Range "bytes=400-"; 206 + body[400..] → file == body
[Fact] public async Task EnsureModelAsync_ServerIgnoresRange_StartsOver()            // .part = garbage[..400]; 200 + body → file == body
[Fact] public async Task EnsureModelAsync_RangeNotSatisfiable_StartsOverWithoutWaiting() // 416 then 200 → file == body, no retry reported
[Fact] public async Task EnsureModelAsync_CompletePartFile_VerifiesWithoutARequest()  // .part == body → handler never called, file == body
[Fact] public async Task EnsureModelAsync_PartLargerThanModel_StartsOver()            // .part longer than size → request has no Range header
[Fact] public async Task EnsureModelAsync_TransientFailure_RetriesAfterTheBackoff()   // 1st send throws HttpRequestException; progress shows Retries 1;
                                                                                       // nothing sent again until time.Advance(2 s); then succeeds
[Fact] public async Task EnsureModelAsync_ServiceUnavailable_IsRetried()              // 503 then 200 → succeeds
[Fact] public async Task EnsureModelAsync_NotFound_ThrowsWithoutRetrying()            // 404 → HttpRequestException, handler called once
[Fact] public async Task EnsureModelAsync_NoBytesFor30Seconds_AbortsAndResumes()      // stream yields 100 bytes then waits on its token;
                                                                                       // Advance(30 s) then Advance(2 s) → 2nd request Range "bytes=100-"
[Fact] public async Task EnsureModelAsync_NotEnoughDiskSpace_Throws()                 // free 500, size 1000, no part → InsufficientDiskSpaceException
                                                                                       //   RequiredBytes 1100, AvailableBytes 500; no request sent
[Fact] public async Task EnsureModelAsync_ChecksumMismatchOnce_DownloadsAgain()       // 1st body corrupt, 2nd correct → file == body, 2 requests
[Fact] public async Task EnsureModelAsync_ChecksumMismatchTwice_Throws()              // always corrupt → ModelVerificationException; no model, no .part
[Fact] public async Task EnsureModelAsync_VerifiedEarlier_DoesNotRehash()             // after a download, overwrite the file with same-length garbage and
                                                                                       // restore its LastWriteTimeUtc → returns the path, no request
[Fact] public async Task EnsureModelAsync_FileChangedSinceVerified_RehashesAndRedownloads() // garbage with a new timestamp → one request, file == body
[Fact] public async Task EnsureModelAsync_Cancelled_KeepsThePartFile()                // cancel mid-stream → OperationCanceledException, .part exists
[Fact] public async Task EnsureModelAsync_Progress_CarriesSpeedAndEta()               // stream advances the fake clock 1 s per 100-byte chunk →
                                                                                       // a report with BytesPerSecond ≈ 100 and SecondsRemaining == (size-received)/100
[Theory, InlineData(1, 2), InlineData(2, 5), InlineData(3, 15), InlineData(4, 60), InlineData(20, 60)]
public void RetryDelay_FollowsTheBackoff(int retry, int seconds) => ModelManager.RetryDelay(retry).ShouldBe(TimeSpan.FromSeconds(seconds));
[Fact] public void HasExpectedFile_WrongSize_IsFalse()
[Fact] public void Delete_RemovesModelPartAndCacheEntry()
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/Murmur.Core.Tests --filter "FullyQualifiedName~ModelManagerTests"`
Expected: a compile failure (new signatures).

- [ ] **Step 3: Implement `ModelManager` as described above, plus `DiskSpace`** (`new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace`). Until Task 5, `WhisperEngine` takes `IModelManager`, passes `SpeechModelCatalog.LargeV3Turbo`, and turns `DownloadProgress` into its old whole-percent reporting, just enough to compile and keep working. Register `DiskSpace` as `IDiskSpace` and `ModelManager` as `IModelManager` (SingleInstance) in `AppModule`.

- [ ] **Step 4: Run the tests and watch them pass**

Run: the Step 2 command, then `dotnet test Murmur.sln`.
Expected: all PASS, 0 warnings.

- [ ] **Step 5: Commit**

`feat(core): resumable, retrying, verified model download`

---

### Task 3: GPU detection, advice decision, model selection

**Files:**
- Create: `src/Murmur.Core/Hardware/GpuReport.cs`, `IGpuInfo.cs`, `GpuInfo.cs`, `GpuAdvisor.cs`; `src/Murmur.Core/Speech/ModelSelection.cs`
- Modify: `src/Murmur.UI/AppModule.cs` (register `GpuInfo` as `IGpuInfo` and `ModelSelection`, SingleInstance)
- Test: `tests/Murmur.Core.Tests/Hardware/GpuAdvisorTests.cs`, `GpuInfoTests.cs`; `tests/Murmur.Core.Tests/Speech/ModelSelectionTests.cs`

**Interfaces:**
- Consumes: `IModelManager.HasExpectedFile`, `ISettingsService.Update`, `SpeechModelCatalog` (Tasks 1–2).
- Produces:
  ```csharp
  public sealed record GpuAdapter(string Name, int VendorId, long DedicatedVideoMemory, long Luid); // software adapters never listed
  public sealed record CudaDevice(long Luid, int ComputeMajor, int ComputeMinor);
  public sealed record CudaDriver(int Version, IReadOnlyList<CudaDevice> Devices);
  public sealed record GpuReport(IReadOnlyList<GpuAdapter> Adapters, CudaDriver? Cuda)  // Cuda null: nvcuda.dll missing or cuInit failed
  { public static GpuReport Empty { get; } = new([], null); }
  public interface IGpuInfo { GpuReport Report { get; } }   // detected once, lazily, thread-safe; never throws (Empty on failure, logged)

  public static class GpuAdvisor
  {
      public const int NvidiaVendorId = 0x10DE;
      public const int MinCudaDriverVersion = 13000;
      public static GpuAdapter? FindSupportedNvidia(GpuReport report);   // NVIDIA adapter whose CUDA device has CC ≥ 8.6
      public static bool CanUseCuda(GpuReport report);                    // supported adapter AND driver ≥ 13000
      public static GpuAdvice Decide(GpuReport report, bool runtimeFound, string? loadedRuntime);
  }

  public sealed class ModelSelection(ISettingsService settings, IGpuInfo gpu, IModelManager models)
  {
      public const long UsableGpuMemory = 3_758_096_384;     // 3.5 GiB, see below
      public SpeechModelId Recommended { get; }
      public SpeechModelId ResolveActive();                   // persists the result when settings had none
      public static SpeechModelId Recommend(GpuReport report);
      public static SpeechModelId Resolve(SpeechModelId? saved, SpeechModelId recommended, bool largePresent);
  }
  ```

Matching CUDA devices to DXGI adapters goes by LUID. When there is exactly one NVIDIA adapter and exactly one CUDA device, they match even if the LUIDs differ.

`Decide` works through these rules in order:
1. `loadedRuntime` starts with `"Cuda"` → `CudaInUse` (with the name).
2. No supported adapter → `NotApplicable`.
3. Driver below 13000 → `UpdateDriver`.
4. Runtime not found → `InstallCuda`.
5. Otherwise `NotApplicable`. This covers "found, not loaded yet" and "found but the engine ended on Vulkan/CPU".

`GpuName` is the supported adapter's DXGI description, for every state except `NotApplicable` (null there).

`Resolve` returns `saved` when it has one. Otherwise it returns large when the large file is present (a 1.0 upgrader keeps its model), and `recommended` in every other case.

`Recommend` returns large when any listed adapter has `DedicatedVideoMemory` ≥ `UsableGpuMemory`, otherwise small. **Ruling:** the threshold is 3.5 GiB rather than 4 because nominal 4 GB cards report a little under 4 GiB. Document this in the XML doc.

`GpuInfo` enumerates DXGI adapters with `CreateDXGIFactory1` → `IDXGIFactory1.EnumAdapters1` → `IDXGIAdapter1.GetDesc1`, skips `DXGI_ADAPTER_FLAG_SOFTWARE` (2), and uses `GeneratedComInterface` with IIDs `770aae78-f26f-4dba-a829-253c83d1b387` (IDXGIFactory1) and `29038f61-3839-4626-91fd-086879011a05` (IDXGIAdapter1). It reads `nvcuda.dll` with `NativeLibrary.TryLoad`, then calls these exports through `delegate* unmanaged[Stdcall]`: `cuInit`, `cuDriverGetVersion`, `cuDeviceGetCount`, `cuDeviceGet`, `cuDeviceGetAttribute` (75 = CC major, 76 = CC minor), and `cuDeviceGetLuid(byte* luid8, uint* nodeMask, int device)`. A non-zero `CUresult` from `cuInit` → `Cuda = null`. It logs one line with the adapters, the driver version and the compute capabilities.

**DEBUG-only simulation** (for the end-to-end check in Task 11): `#if DEBUG`, a file named `simulate-no-gpu` in the data root makes `Report` return `GpuReport.Empty` and log a warning. `GpuInfo` takes `MurmurPaths` for this.

- [ ] **Step 1: Write the failing tests**

```csharp
// GpuAdvisorTests: helper Nvidia(name, cc major, minor, luid) builds the adapter (12 GB) and the CUDA device
public static TheoryData<string, GpuReport, bool, string?, GpuAdviceState, string?> Cases => new()
{
    { "no GPU",                   GpuReport.Empty,                           false, null,     NotApplicable, null },
    { "AMD only",                 Report(Amd("Radeon RX 7800 XT")),          false, "Vulkan", NotApplicable, null },
    { "RTX 3060, no runtime",     Report(Rtx3060, driver: 13000),            false, "Vulkan", InstallCuda,   "NVIDIA GeForce RTX 3060" },
    { "RTX 3060, still downloading", Report(Rtx3060, driver: 13000),         false, null,     InstallCuda,   "NVIDIA GeForce RTX 3060" },
    { "RTX 4070, old driver",     Report(Rtx4070, driver: 12080),            false, "Vulkan", UpdateDriver,  "NVIDIA GeForce RTX 4070" },
    { "old driver, runtime found", Report(Rtx4070, driver: 12080),           true,  "Vulkan", UpdateDriver,  "NVIDIA GeForce RTX 4070" },
    { "RTX 2080 (Turing 7.5)",    Report(Rtx2080, driver: 13000),            false, "Vulkan", NotApplicable, null },
    { "GTX 1080 (Pascal 6.1)",    Report(Gtx1080, driver: 13000),            true,  "Vulkan", NotApplicable, null },
    { "NVIDIA, no nvcuda",        ReportWithoutCuda(Rtx3060),                false, "Vulkan", NotApplicable, null },
    { "CUDA loaded",              Report(Rtx3090, driver: 13000),            true,  "Cuda",   CudaInUse,     "NVIDIA GeForce RTX 3090" },
    { "runtime found, Vulkan",    Report(Rtx3090, driver: 13000),            true,  "Vulkan", NotApplicable, null },
    { "runtime found, not loaded yet", Report(Rtx3090, driver: 13000),       true,  null,     NotApplicable, null },
};
[Theory, MemberData(nameof(Cases))]
public void Decide_FollowsTheDecisionTable(string _, GpuReport report, bool runtimeFound, string? loaded, GpuAdviceState state, string? name)
    => GpuAdvisor.Decide(report, runtimeFound, loaded).ShouldBe(new GpuAdvice(state, name));
[Fact] public void FindSupportedNvidia_TwoNvidiaAdapters_MatchesByLuid()       // GTX 1080 (luid 1) + RTX 3060 (luid 2) → RTX 3060
[Fact] public void CanUseCuda_SupportedGpuWithOldDriver_IsFalse()
[Fact] public void CanUseCuda_UnsupportedGpu_IsFalse()

// ModelSelectionTests
[Theory]
[InlineData(4_293_918_720L, SpeechModelId.LargeV3Turbo)]   // a "4 GB" card
[InlineData(12_884_901_888L, SpeechModelId.LargeV3Turbo)]
[InlineData(2_147_483_648L, SpeechModelId.SmallEn)]
[InlineData(134_217_728L, SpeechModelId.SmallEn)]          // integrated graphics
public void Recommend_UsesDedicatedVideoMemory(long memory, SpeechModelId expected)
[Fact] public void Recommend_NoAdapters_IsSmall()
[Theory]
[InlineData(SpeechModelId.SmallEn, SpeechModelId.LargeV3Turbo, true, SpeechModelId.SmallEn)]       // saved wins
[InlineData(null, SpeechModelId.SmallEn, true, SpeechModelId.LargeV3Turbo)]                        // 1.0 upgrader keeps large
[InlineData(null, SpeechModelId.SmallEn, false, SpeechModelId.SmallEn)]
[InlineData(null, SpeechModelId.LargeV3Turbo, false, SpeechModelId.LargeV3Turbo)]
public void Resolve_FollowsTheRules(SpeechModelId? saved, SpeechModelId recommended, bool largePresent, SpeechModelId expected)
[Fact] public void ResolveActive_NothingSaved_PersistsTheChoice()          // settings.Update called once; the result has SpeechModel set
[Fact] public void ResolveActive_AlreadySaved_DoesNotWrite()

// GpuInfoTests (real hardware)
[Fact] public void Report_OnThisPc_DoesNotThrow()
[Fact] public void Report_WithAnNvidiaGpu_HasTheDriverAndComputeCapability()
// Assert.SkipUnless(report.Adapters.Any(a => a.VendorId == 0x10DE), "no NVIDIA GPU");
// report.Cuda.ShouldNotBeNull(); report.Cuda.Version.ShouldBeGreaterThan(0); devices non-empty with ComputeMajor > 0
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/Murmur.Core.Tests --filter "FullyQualifiedName~GpuAdvisorTests|FullyQualifiedName~ModelSelectionTests|FullyQualifiedName~GpuInfoTests"`
Expected: a compile failure.

- [ ] **Step 3: Implement the types above.**

- [ ] **Step 4: Run the tests and watch them pass**

Run: the Step 2 command.
Expected: PASS. On the owner's PC `Report_WithAnNvidiaGpu_…` runs; it is skipped elsewhere.

- [ ] **Step 5: Commit**

`feat(core): GPU detection, CUDA advice decision and hardware-aware model selection`

---

### Task 4: CUDA runtime search beyond PATH

**Files:**
- Create: `src/Murmur.Core/Speech/CudaRuntimeSearch.cs`, `src/Murmur.Core/Speech/CudaRuntimeLocator.cs`
- Modify: `src/Murmur.Core/Speech/CudaRuntimeLoader.cs`
- Test: `tests/Murmur.Core.Tests/Speech/CudaRuntimeSearchTests.cs` (create), `tests/Murmur.Core.Tests/Speech/CudaRuntimeLoaderTests.cs` (adapt)

**Interfaces:**
- Produces:
  ```csharp
  internal sealed record CudaSearchSources(string? ProcessPath, string? MachinePath, string? UserPath, string? CudaPath,
      string? ProgramFiles, Func<string, IEnumerable<string>> ListDirectories);
  internal static class CudaRuntimeSearch
  {
      internal static IReadOnlyList<string> Directories(CudaSearchSources sources);
      internal static CudaSearchSources FromSystem();
  }
  internal static class CudaRuntimeLoader
  {
      internal static string? FindRuntimeDirectory(IEnumerable<string> directories, Func<string, bool> fileExists);
      public static bool TryLoad(string directory);     // the three DLLs in order; logs; never throws
  }
  public interface ICudaRuntimeLocator { string? FindRuntimeDirectory(); }   // CudaRuntimeLocator: FromSystem + File.Exists
  ```

`Directories` lists candidates in this order, then de-duplicates case-insensitively (ignoring a trailing `\`, keeping the first) and drops entries that are not fully qualified:
1. Process `PATH` entries.
2. Machine `Path` entries.
3. User `Path` entries.
4. `CudaPath\bin\x64`, then `CudaPath\bin`.
5. Each `ProgramFiles\NVIDIA GPU Computing Toolkit\CUDA\v13.*` directory, newest first (compare the number after `v13.` numerically), each as `\bin\x64` then `\bin`.

Entries are split on `;`, trimmed, and stripped of quotes. This is the old `FindRuntimeDirectory` PATH parsing, moved here.

`FromSystem` reads:
- `PATH` and `CUDA_PATH` from the process. When `CUDA_PATH` is absent, it falls back to the machine environment key.
- Machine `Path` from `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment`.
- User `Path` from `HKCU\Environment`.

Registry values are expanded with `Environment.ExpandEnvironmentVariables`.

**DEBUG-only simulation:** `#if DEBUG`, a `simulate-no-cuda-runtime` file in the data root makes `CudaRuntimeLocator.FindRuntimeDirectory` return null and log a warning. The locator takes `MurmurPaths`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Directories_FollowTheSpecOrder()
// ProcessPath @"C:\p1", MachinePath @"C:\m1", UserPath @"C:\u1", CudaPath @"C:\cuda", ProgramFiles @"C:\PF",
// ListDirectories returns [@"C:\PF\NVIDIA GPU Computing Toolkit\CUDA\v13.0", @"...\v13.2", @"...\v12.8"] →
// [ @"C:\p1", @"C:\m1", @"C:\u1", @"C:\cuda\bin\x64", @"C:\cuda\bin",
//   @"C:\PF\...\v13.2\bin\x64", @"C:\PF\...\v13.2\bin", @"C:\PF\...\v13.0\bin\x64", @"C:\PF\...\v13.0\bin" ]
[Fact] public void Directories_RemoveDuplicatesKeepingTheFirst()   // @"C:\x\" in PATH and @"c:\X" in user Path → one entry, the PATH one
[Fact] public void Directories_AcceptQuotedEntriesAndDropRelativeOnes()
[Fact] public void Directories_AllSourcesMissing_IsEmpty()
[Fact] public void Directories_ListingProgramFilesThrows_SkipsIt()  // ListDirectories throws DirectoryNotFoundException → other sources still listed
// CudaRuntimeLoaderTests: existing cases rewritten to pass directory lists to FindRuntimeDirectory; TryLoad_UnloadableFiles_ReturnsFalse
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/Murmur.Core.Tests --filter "FullyQualifiedName~CudaRuntime"`
Expected: a compile failure.

- [ ] **Step 3: Implement.** Until Task 5 rewrites `WhisperEngine.CreateFactory`, have it use `CudaRuntimeLoader.FindRuntimeDirectory(CudaRuntimeSearch.Directories(CudaRuntimeSearch.FromSystem()), File.Exists)` followed by `TryLoad`. Register `CudaRuntimeLocator` as `ICudaRuntimeLocator`.

- [ ] **Step 4: Run the tests and watch them pass**

Run: the Step 2 command, then `dotnet test Murmur.sln`.
Expected: PASS.

- [ ] **Step 5: Commit**

`feat(core): find the CUDA 13 runtime in the registry Path, CUDA_PATH and the toolkit folder`

---

### Task 5: Engine — model loader seam, GPU gating, rich status, model switching

**Files:**
- Create: `src/Murmur.Core/Speech/IWhisperModelLoader.cs`, `WhisperModelLoader.cs`, `DownloadReportThrottle.cs`
- Modify: `src/Murmur.Core/Models/DomainModels.cs` (`EngineStatus`), `src/Murmur.Core/Speech/ISpeechEngine.cs`, `src/Murmur.Core/Speech/WhisperEngine.cs`, `src/Murmur.UI/Tray/TrayIconHost.cs` (`FormatToolTip`), `src/Murmur.UI/AppModule.cs`
- Test: `tests/Murmur.Core.Tests/Speech/WhisperEngineTests.cs` (create), `DownloadReportThrottleTests.cs` (create), `WhisperEngineIntegrationTests.cs` (adapt), `tests/Murmur.UI.Tests/Tray/TrayIconHostTests.cs` (adapt)

**Interfaces:**
- Consumes: `IModelManager` (Task 2); `IGpuInfo`, `GpuAdvisor`, `ModelSelection` (Task 3); `ICudaRuntimeLocator`, `CudaRuntimeLoader.TryLoad` (Task 4).
- Produces:
  ```csharp
  public sealed record EngineStatus(EngineState State, string ModelName, string? Runtime = null, string? Error = null,
      DownloadProgress? Download = null, GpuAdvice? Gpu = null);          // the old double? DownloadProgress is removed

  public interface ILoadedSpeechModel : IDisposable
  {
      string? Runtime { get; }
      Task<TranscriptionResult> TranscribeAsync(float[] samples16kMono, string prompt, CancellationToken ct);
  }
  public interface IWhisperModelLoader
  {
      /// Sets RuntimeOptions.RuntimeLibraryOrder (WhisperModelLoader.RuntimeOrder), preloads the CUDA runtime from
      /// cudaRuntimeDirectory when canUseCuda, loads the model and warms it up with a second of zeros.
      Task<ILoadedSpeechModel> LoadAsync(string modelPath, bool canUseCuda, string? cudaRuntimeDirectory, CancellationToken ct);
  }
  public sealed class WhisperModelLoader : IWhisperModelLoader
  { public static RuntimeLibrary[] RuntimeOrder(bool canUseCuda); }   // [Cuda, Vulkan, Cpu] or [Vulkan, Cpu]

  public interface ISpeechEngine   // adds:
  {
      /// Persists the choice before the returned task's first await; uses the token given to InitializeAsync.
      Task SwitchModelAsync(SpeechModelId model);
  }
  public sealed class WhisperEngine(IModelManager models, ModelSelection selection, IGpuInfo gpu,
      ICudaRuntimeLocator cuda, IWhisperModelLoader loader) : ISpeechEngine, IDisposable

  internal sealed class DownloadReportThrottle(TimeProvider time)
  { public bool ShouldReport(DownloadProgress progress); }   // first report, whole percent changed, Retries changed, or ≥ 1 s since the last
  ```

`WhisperEngine` works as follows (the transcription code moves into `WhisperModelLoader`'s loaded model; the digital-silence check stays in `WhisperEngine.TranscribeAsync`, before the loaded model is called):

- **Initialize:**
  1. Take `gpu.Report`. Compute `canUseCuda = GpuAdvisor.CanUseCuda(report)`. Look for the CUDA runtime only when `canUseCuda` is true.
  2. Compute `advice = GpuAdvisor.Decide(report, runtimeDir != null, null)`.
  3. Resolve the model with `selection.ResolveActive()` and take `ModelName` from the catalog's display name.
  4. Download through `models.EnsureModelAsync`, reporting `Downloading` with `Download` and `Gpu` (throttled).
  5. Report `Loading`, then call `loader.LoadAsync`.
  6. Report `Ready` with `Runtime` and `Gpu = GpuAdvisor.Decide(report, runtimeDir != null, runtime)`.
  7. When the runtime was found but `runtime` does not start with `"Cuda"`, log a warning naming both.
- **Errors:** `Error` keeps `Gpu`.
  - `InsufficientDiskSpaceException` → `"Not enough disk space for the speech model: Murmur needs {X} free on {drive} and there is {Y}. Free some space and restart Murmur."`, where X and Y are in decimal GB with one decimal (1,800,000,000 → `"1.8 GB"`, matching the web's `formatBytes`).
  - `ModelVerificationException` → `"The speech model download was damaged twice. Restart Murmur to try again."`
  - The other existing hints stay.
- **`SwitchModelAsync(id)`:**
  - When `id` is already active and `Ready`: persist the choice and do nothing else.
  - When no model is loaded yet (`Downloading`, `Loading` or `Error`): cancel the running initialization and wait for it, delete the abandoned model's `.part` (`models.Delete`), then initialize with `id`.
  - When `Ready`: stay `Ready` on the old model while reporting `Download` for the new one. Then report `Loading`, load, swap, and report `Ready` with the new name. Finally dispose the old model and `models.Delete` the old catalog entry.
  - If the switch fails, report `Ready` on the old model with `Error` set to `"Could not switch to {name}: {message}"`, and persist the old model as the choice again.
- **Transcriptions and switches never overlap:** `TranscribeAsync` holds a `SemaphoreSlim` while it uses the loaded model, and a switch takes the same semaphore before disposing the old model.

`TrayIconHost.FormatToolTip`:
- `Downloading` → `"Murmur — downloading speech model {percent:P0}"`, from `BytesReceived/TotalBytes`.
- `Download.Reconnecting` → `"Murmur — reconnecting to download the speech model…"`.
- `Ready` → unchanged, even while a switch download runs.

- [ ] **Step 1: Write the failing tests**

Fakes: `IModelManager` substitute (`EnsureModelAsync` reports two progress values, then returns a path), `IGpuInfo` returning a chosen report, `ICudaRuntimeLocator`, and a `FakeLoader` / `FakeLoadedModel` that records `LoadAsync` arguments and `Dispose`, and whose `TranscribeAsync` can be blocked on a `TaskCompletionSource`. `ModelSelection` is built over a substitute `ISettingsService` that holds a `MurmurSettings` in a field.

```csharp
[Fact] public async Task InitializeAsync_ModelMissing_ReportsDownloadingLoadingReady()
// statuses: Downloading (Download.TotalBytes == catalog size, ModelName "Whisper large-v3-turbo"), Loading, Ready(Runtime "Vulkan")
[Fact] public async Task InitializeAsync_UnsupportedGpu_LoadsWithoutCudaAndNeverSearches()
// GTX 1080 report → loader got canUseCuda false, cudaRuntimeDirectory null; locator.DidNotReceive().FindRuntimeDirectory()
[Fact] public async Task InitializeAsync_SupportedGpuWithoutRuntime_AdvisesInstallCudaWhileDownloading()
// RTX 3060 report, locator → null → the Downloading status has Gpu == (InstallCuda, "NVIDIA GeForce RTX 3060")
[Fact] public async Task InitializeAsync_RuntimeFoundButVulkanLoaded_AdvisesNothing()
[Fact] public async Task InitializeAsync_CudaLoaded_AdvisesCudaInUse()
[Fact] public async Task InitializeAsync_DiskFull_ReportsTheSpaceNeeded()
// EnsureModelAsync throws InsufficientDiskSpaceException(1_800_000_000, 900_000_000, @"C:\") →
// Error.ShouldContain("needs 1.8 GB free on C:\\ and there is 0.9 GB")
[Fact] public async Task InitializeAsync_NoSavedChoiceAndNoGpu_UsesTheSmallModel()   // Empty report → Ready ModelName "Whisper small (English)"
[Fact] public async Task SwitchModelAsync_WhileReady_StaysReadyUntilTheNewModelIsLoaded()
// statuses after switch: Ready+Download(small), Loading, Ready("Whisper small (English)"); old model disposed;
// models.Delete(LargeV3Turbo) received after the new LoadAsync
[Fact] public async Task SwitchModelAsync_DuringTheFirstDownload_CancelsItAndLoadsTheNewModel()
// EnsureModelAsync(large) waits on its token → switch → it saw cancellation, models.Delete(large), then Ready(small)
[Fact] public async Task SwitchModelAsync_WaitsForARunningTranscriptionBeforeDisposingTheOldModel()
// block FakeLoadedModel.TranscribeAsync; start a switch; old.Disposed false; release; switch completes; old.Disposed true
[Fact] public async Task SwitchModelAsync_DownloadFails_StaysReadyOnTheOldModelAndRestoresTheChoice()

// DownloadReportThrottleTests
[Fact] public void ShouldReport_SamePercentWithinASecond_IsFalse()
[Fact] public void ShouldReport_PercentChanged_IsTrue()
[Fact] public void ShouldReport_RetriesChanged_IsTrue()
[Fact] public void ShouldReport_OneSecondLater_IsTrue()

// WhisperModelLoader
[Fact] public void RuntimeOrder_WithoutCuda_LeavesCudaOut() // [Vulkan, Cpu]; with CUDA [Cuda, Vulkan, Cpu]

// TrayIconHostTests: FormatToolTip cases for downloading 38 % ("Murmur — downloading speech model 38%"), reconnecting, and Ready-with-Download
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test Murmur.sln --filter "FullyQualifiedName~WhisperEngineTests|FullyQualifiedName~DownloadReportThrottleTests|FullyQualifiedName~TrayIconHostTests"`
Expected: a compile failure.

- [ ] **Step 3: Implement.** Register `WhisperModelLoader` as `IWhisperModelLoader`. Keep `WhisperEngine` registered `ExternallyOwned`. Update every `EngineStatus` construction (`GetEngineStatusHandler` and tests) to the new shape. Adapt `WhisperEngineIntegrationTests` so they build the engine with the real components.

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test Murmur.sln`
Expected: PASS. On the owner's PC the CUDA integration test runs and asserts `Gpu.State == CudaInUse`.

- [ ] **Step 5: Commit**

`feat(core): engine reports download details and GPU advice, gates CUDA on supported GPUs, switches models`

---

### Task 6: First-run decision and the IPC surface

**Files:**
- Create: `src/Murmur.Core/Setup/SetupCoordinator.cs`, `src/Murmur.Core/Setup/LaunchOptions.cs`, `src/Murmur.Core/Services/ILinkOpener.cs`, `src/Murmur.Core/Services/IAppLifecycle.cs`, `src/Murmur.Core/Services/AppLinks.cs`
- Create: `src/Murmur.Core/Features/Setup/GetSetupStateRequest.cs`, `UpdateSetupStateRequest.cs`; `src/Murmur.Core/Features/Models/GetModelChoiceRequest.cs`, `SetModelChoiceRequest.cs`; `src/Murmur.Core/Features/App/OpenLinkRequest.cs`, `RestartAppRequest.cs`
- Create: `src/Murmur.UI/ShellLinkOpener.cs`, `src/Murmur.UI/AppLifecycle.cs`
- Modify: `src/Murmur.UI/App.xaml.cs` (registrations, start-up order), `src/Murmur.UI/AppModule.cs`
- Test: `tests/Murmur.Core.Tests/Setup/SetupCoordinatorTests.cs`, `tests/Murmur.Core.Tests/Features/SetupHandlerTests.cs`, `ModelChoiceHandlerTests.cs`, `AppHandlerTests.cs`; `tests/Murmur.Interop.Tests/RequestJsonContractTests.cs` (add the new requests the way the existing ones are)

**Interfaces:**
- Consumes: `ModelSelection`, `IModelManager.HasExpectedFile`, `ISpeechEngine.SwitchModelAsync`, `ISettingsService.Update`.
- Produces:
  ```csharp
  public sealed class LaunchOptions { public string? OpenPage { get; set; } }      // set by App from the arguments (Task 8)
  public sealed class SetupCoordinator(ISettingsService settings, ModelSelection selection, IModelManager models, LaunchOptions launch)
  {
      public Task Initialized { get; }                     // completes after Initialize, even when it failed
      public void Initialize();                            // once, off the UI thread, before the engine starts
      public Task<SetupState> GetStateAsync();             // awaits Initialized; OpenPage is handed out once
      public SetupState Update(SetupStep? step, bool? cudaTipDismissed, bool? completed);
  }
  public interface ILinkOpener { void Open(string uri); }
  public interface IAppLifecycle { void RequestRestart(); }
  public static class AppLinks { public static string For(LinkTarget target); }

  // IPC (service, method) → request → response
  ("setup", "getState")  GetSetupStateRequest()                                          → SetupState
  ("setup", "update")    UpdateSetupStateRequest(SetupStep? Step, bool? CudaTipDismissed, bool? Completed) → SetupState
  ("models", "getChoice") GetModelChoiceRequest()                                        → ModelChoice
  ("models", "setChoice") SetModelChoiceRequest(SpeechModelId Model)                     → ModelChoice  // SwitchModelAsync(...).FireAndForget()
  ("app", "openLink")    OpenLinkRequest(LinkTarget Target)                              → Unit
  ("app", "restart")     RestartAppRequest()                                             → Unit
  ```

`Initialize` decides once and persists the result through `settings.Update`:
- Already `SetupCompleted` → nothing changes.
- `SetupStep` is null and `models.HasExpectedFile(active model)` (the 1.0 upgrader) → set `SetupCompleted = true`.
- `SetupStep` is null in any other case → set `SetupStep = SpeechModel`.
- Otherwise it is a first run in progress: leave it, so it resumes.

`ShowSetup = !SetupCompleted`.

**Ruling:** "present and valid" uses the expected file size for this decision. The full SHA-256 check still runs before any load (Task 2). If a right-sized but corrupt 1.0 file turns up, that user skips setup and sees the re-download in the banner instead.

`AppLinks` maps `CudaDownload` → `https://developer.nvidia.com/cuda-downloads`, `NvidiaDriver` → `https://www.nvidia.com/Download/index.aspx`, and `MicrophonePrivacy` → `ms-settings:privacy-microphone`.

`ShellLinkOpener` calls `Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })` and logs failures without rethrowing. `AppLifecycle` raises `public event Action? RestartRequested` (App handles it in Task 8; until then, log it).

In `App.Start`, step 10 becomes a fire-and-forget `StartSpeechAsync`: `await Task.Run(setup.Initialize)` first, then `await engine.InitializeAsync(_appCts.Token)`. Register the six requests next to the existing ones.

- [ ] **Step 1: Write the failing tests**

```csharp
// SetupCoordinatorTests (settings substitute backed by a field; HasExpectedFile configurable)
[Fact] public async Task Initialize_FreshProfileWithoutModel_StartsSetupAtTheSpeechModelStep() // ShowSetup true, Step SpeechModel, persisted
[Fact] public async Task Initialize_Version10UserWithModel_MarksSetupCompleted()              // SetupStep null + file present → ShowSetup false, SetupCompleted persisted
[Fact] public async Task Initialize_FirstRunInProgress_ResumesTheSavedStep()                  // Step Microphone, model present → ShowSetup true, Step Microphone
[Fact] public async Task Initialize_Completed_NeverShowsSetup()
[Fact] public async Task GetStateAsync_BeforeInitialize_WaitsForIt()
[Fact] public async Task GetStateAsync_OpenPage_IsHandedOutOnce()                             // "settings" then null
[Fact] public void Update_PersistsOnlyTheGivenFields()                                         // Update(step: TryIt) keeps CudaTipDismissed

// handlers
[Fact] public async Task GetModelChoice_ReturnsSelectedRecommendedAndOptions()
[Fact] public async Task SetModelChoice_StartsTheSwitchAndReturnsTheNewSelection()              // engine.Received().SwitchModelAsync(SmallEn)
[Theory]
[InlineData(LinkTarget.CudaDownload, "https://developer.nvidia.com/cuda-downloads")]
[InlineData(LinkTarget.NvidiaDriver, "https://www.nvidia.com/Download/index.aspx")]
[InlineData(LinkTarget.MicrophonePrivacy, "ms-settings:privacy-microphone")]
public async Task OpenLink_OpensTheWhitelistedUri(LinkTarget target, string uri)
[Fact] public async Task Restart_RequestsARestart()
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test Murmur.sln --filter "FullyQualifiedName~SetupCoordinatorTests|FullyQualifiedName~SetupHandlerTests|FullyQualifiedName~ModelChoiceHandlerTests|FullyQualifiedName~AppHandlerTests|FullyQualifiedName~RequestJsonContractTests"`
Expected: a compile failure.

- [ ] **Step 3: Implement.** `LaunchOptions`, `SetupCoordinator`, `ShellLinkOpener` and `AppLifecycle` are SingleInstance. `AppModuleTests` must still resolve everything.

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test Murmur.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

`feat: first-run decision and setup, model-choice, link and restart requests`

---

### Task 7: Microphone level test

**Files:**
- Create: `src/Murmur.Core/Audio/MicrophoneLevelMonitor.cs`, `src/Murmur.Core/Features/Setup/MicTestRequests.cs` (`StartMicTestRequest`, `StopMicTestRequest`, `MicLevelNotification`), `src/Murmur.Interop/Handlers/MicLevelEventHandler.cs`
- Modify: `src/Murmur.UI/App.xaml.cs` (register `("setup","startMicTest")`, `("setup","stopMicTest")`), `src/Murmur.UI/AppModule.cs`, `src/Murmur.UI/MainWindow.xaml.cs` (stop when hidden)
- Test: `tests/Murmur.Core.Tests/Audio/MicrophoneLevelMonitorTests.cs`, `tests/Murmur.Interop.Tests/EventHandlerTests.cs` (add the event)

**Interfaces:**
- Produces:
  ```csharp
  public sealed class MicrophoneLevelMonitor(Func<IAudioRecorder> createRecorder, IMediator mediator, TimeProvider time) : IDisposable
  {
      public static TimeSpan MaxDuration { get; }          // 120 s
      public MicTestResult Start(string? deviceId);         // stops a running test first
      public void Stop();                                   // idempotent
      public static double LevelFromDbfs(double dbfs);      // -60 → 0, 0 → 1, linear, clamped
      public static MicTestState Classify(Exception ex);    // AccessDenied when the chain has UnauthorizedAccessException or HResult 0x80070005
  }
  public record StartMicTestRequest(string? MicrophoneId) : IRequest<MicTestResult>;
  public record StopMicTestRequest : IRequest;
  public sealed class MicLevelNotification(double level) : INotification;   // → event "setup.micLevel" with { level }
  ```

The monitor owns its own recorder (`() => new AudioRecorder()` in `AppModule`), so it never collides with a dictation. It computes the level from `SpeechDetector.RmsDbfs` of each chunk and publishes at most once per 50 ms. It stops by itself after `MaxDuration`. `MainWindow` takes the monitor and calls `Stop()` from its existing `IsVisibleChanged` handler.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory, InlineData(-90, 0), InlineData(-60, 0), InlineData(-30, 0.5), InlineData(0, 1), InlineData(6, 1)]
public void LevelFromDbfs_MapsLinearly(double dbfs, double level)
[Fact] public void Start_DeviceOpens_ReturnsListeningAndPublishesLevels()      // fake recorder raises SamplesAvailable → MicLevelNotification published
[Fact] public void Start_SamplesFasterThan50Ms_PublishesAtMostOncePer50Ms()    // two chunks with no time advance → one publish
[Fact] public void Start_AccessDenied_ReturnsAccessDenied()                    // Start throws AudioDeviceException(inner UnauthorizedAccessException)
[Fact] public void Start_OtherDeviceError_ReturnsFailedWithTheMessage()
[Fact] public void Start_WhileRunning_StopsTheFirstRecorder()
[Fact] public void Start_After120Seconds_StopsByItself()                        // Review Focus 3: Advance(120 s) → recorder.Received().Cancel()
[Fact] public void Stop_Twice_IsHarmless()
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test Murmur.sln --filter "FullyQualifiedName~MicrophoneLevelMonitorTests|FullyQualifiedName~EventHandlerTests"`
Expected: a compile failure.

- [ ] **Step 3: Implement** the monitor, the two request handlers, the notification and the Interop handler publishing `"setup.micLevel"` (following `EngineStatusEventHandler`).

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test Murmur.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

`feat: microphone level test for setup`

---

### Task 8: Tray notifications, restart, launch arguments

**Files:**
- Create: `src/Murmur.UI/Tray/TrayNotifier.cs`, `src/Murmur.UI/LaunchArguments.cs`
- Modify: `src/Murmur.UI/Tray/TrayIconHost.cs`, `src/Murmur.UI/SingleInstanceGuard.cs`, `src/Murmur.UI/App.xaml.cs`, `src/Murmur.UI/AppLifecycle.cs`
- Test: `tests/Murmur.UI.Tests/Tray/TrayNotifierTests.cs`, `tests/Murmur.UI.Tests/LaunchArgumentsTests.cs`, `tests/Murmur.UI.Tests/SingleInstanceGuardTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  internal sealed record TrayNotice(string Title, string Message);
  internal sealed class TrayNotifier
  { public TrayNotice? OnStatus(EngineStatus status, bool windowActive, string hotkeyName); }
  public sealed partial class TrayIconHost  // adds
  { public void ShowNotification(string title, string message); }   // H.NotifyIcon ShowNotification, Info icon; balloon click → OpenRequested
  internal sealed record LaunchArguments(bool Minimized, bool Restarted, string? Page)
  { public static LaunchArguments Parse(IReadOnlyList<string> args); }   // --minimized, --restarted, --page <history|vocabulary|settings>; case-insensitive; unknown ignored
  public bool TryAcquire(TimeSpan wait);   // SingleInstanceGuard; TryAcquire() == TryAcquire(TimeSpan.Zero)
  ```

`TrayNotifier` keeps its state for the whole process:
- **Ready:** a status with `Downloading` or a `Download` marks that a download happened. When `Ready` follows with no `Download`, the window is not active, and a download happened, return `("Murmur is ready", $"Hold {hotkeyName} to talk.")`. Clear the mark whether or not it notified.
- **Error:** the first `Error` while the window is not active returns `("Murmur needs your attention", status.Error)`. This happens once per run.
- **Offline:** the first `Download.Retries >= 4` while the window is not active returns `("Murmur can't download its speech model", "Check your internet connection. Murmur keeps trying.")`. This also happens once per run.

**Ruling:** ready is announced only after a download, so an ordinary sign-in start stays silent.

In `App`:
1. Parse the arguments and set `LaunchOptions.OpenPage`.
2. Call `_guard.TryAcquire(args.Restarted ? TimeSpan.FromSeconds(10) : TimeSpan.Zero)`.
3. Treat a sign-in start as `args.Minimized || packaged startup activation`.
4. Wire `engine.StatusChanged` through the dispatcher to `TrayNotifier`. The window is active when `_mainWindow.IsActive`, and the hotkey name comes from `ISettingsService.Load()`.
5. Handle `AppLifecycle.RestartRequested`: `Process.Start(Environment.ProcessPath!, ["--restarted", "--page", "settings"])`, then `Quit()`. If the start throws, log it and keep running.

- [ ] **Step 1: Write the failing tests**

```csharp
// TrayNotifierTests
[Fact] public void OnStatus_ReadyAfterADownloadWithTheWindowInactive_AnnouncesReady()   // ("Murmur is ready", "Hold Right Ctrl to talk.")
[Fact] public void OnStatus_ReadyWithoutADownloadThisRun_NoNotice()                      // Review Focus 2
[Fact] public void OnStatus_ReadyWhileTheWindowIsActive_NoNotice()
[Fact] public void OnStatus_SecondErrorInTheSameRun_NoNotice()
[Fact] public void OnStatus_FourthRetry_WarnsOnceAboutTheConnection()
[Fact] public void OnStatus_ReadyAfterASwitchDownload_AnnouncesAgain()

// LaunchArgumentsTests
[Fact] public void Parse_RestartedWithPage_ReadsBoth()        // ["--restarted","--page","settings"] → (false, true, "settings")
[Fact] public void Parse_UnknownPage_IsIgnored()              // ["--page","evil"] → Page null
[Fact] public void Parse_Minimized_IsCaseInsensitive()

// SingleInstanceGuardTests (unique names per test)
[Fact] public void TryAcquire_MutexReleasedWithinTheWait_Succeeds()   // Review Focus 4: another thread holds it, releases after 200 ms; TryAcquire(5 s) true
[Fact] public void TryAcquire_MutexStillHeld_FailsAfterTheWait()      // TryAcquire(300 ms) false
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/Murmur.UI.Tests --filter "FullyQualifiedName~TrayNotifierTests|FullyQualifiedName~LaunchArgumentsTests|FullyQualifiedName~SingleInstanceGuardTests"`
Expected: a compile failure.

- [ ] **Step 3: Implement.** The mutex wait in `TryAcquire(TimeSpan)` uses the existing `AbandonedMutexException` handling.

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test Murmur.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

`feat(ui): tray notifications, restart into Settings, launch arguments`

---

### Task 9: Web — status types, download text, CUDA tip, Settings card

**Files:**
- Modify: `src/WebApp/src/types/domain.ts`, `src/WebApp/src/features/settings/engineStatusText.ts`, `EngineStatusLine.tsx`, `SettingsPage.tsx`, `SettingsPage.css`
- Create: `src/WebApp/src/features/settings/formatDownload.ts`, `CudaTip.tsx`, `CudaTip.css`, `SpeechModelSection.tsx`, `useModelChoice.ts`, `appActions.ts`
- Test: `src/WebApp/src/features/settings/__tests__/formatDownload.test.ts`, `CudaTip.test.tsx`, `engineStatusText.test.ts`, `SettingsPage.test.tsx`, `useEngineStatus.test.ts` (fixtures to the new shape)

**Interfaces:**
- Consumes: the C# shapes from Tasks 1, 5 and 6, as camelCase JSON.
- Produces (`domain.ts`):
  ```ts
  export type SpeechModelId = "largeV3Turbo" | "smallEn";
  export type SetupStep = "speechModel" | "microphone" | "tryIt";
  export type GpuAdviceState = "notApplicable" | "cudaInUse" | "installCuda" | "updateDriver";
  export type LinkTarget = "cudaDownload" | "nvidiaDriver" | "microphonePrivacy";
  export type MicTestState = "listening" | "accessDenied" | "failed";
  export interface GpuAdvice { state: GpuAdviceState; gpuName: string | null }
  export interface DownloadProgress { modelName: string; bytesReceived: number; totalBytes: number; bytesPerSecond: number;
    secondsRemaining: number | null; retries: number; reconnecting: boolean }
  export interface EngineStatus { state: EngineState; modelName: string; runtime: string | null; error: string | null;
    download: DownloadProgress | null; gpu: GpuAdvice | null }
  export interface SetupState { showSetup: boolean; step: SetupStep; cudaTipDismissed: boolean; openPage: Page | null }
  export interface SpeechModelOption { id: SpeechModelId; label: string; sizeBytes: number }
  export interface ModelChoice { selected: SpeechModelId; recommended: SpeechModelId; options: SpeechModelOption[] }
  export interface MicTestResult { state: MicTestState; error: string | null }
  ```
- Produces (`formatDownload.ts`, decimal units so the copy reads "1.6 GB" and "488 MB"):
  ```ts
  export function formatBytes(bytes: number): string;                 // < 1e9 → "612 MB" (rounded); ≥ 1e9 → "1.6 GB" (one decimal)
  export function formatSpeed(bytesPerSecond: number): string | null; // "18 MB/s"; null when ≤ 0 or not finite
  export function formatRemaining(seconds: number | null): string | null;
  //   null/non-finite → null; < 60 → "less than a minute left"; < 90 → "about 1 minute left";
  //   < 3600 → `about ${Math.round(s / 60)} minutes left`; otherwise "about N hour(s) left"
  export function describeDownload(d: DownloadProgress): { amount: string; detail: string | null; percent: number };
  //   amount "612 MB of 1.6 GB"; detail "Reconnecting…" when reconnecting, else speed and remaining joined by " · ", or null
  ```
- Produces (`appActions.ts`): `openLink(target: LinkTarget): Promise<void>` → `apiClient.call("app","openLink",{ target })`, and `restartMurmur(): Promise<void>` → `apiClient.call("app","restart",{})`.
- Produces (`CudaTip.tsx`): `CudaTip({ advice, variant, onDismiss }: { advice: GpuAdvice | null; variant: "setup" | "settings"; onDismiss?: () => void })`. It renders nothing unless the state is `installCuda` or `updateDriver`.
  - **installCuda, setup variant:** bold line `Your ${gpuName} can run Murmur about twice as fast.`, then `Murmur is using your graphics card through Vulkan. Install NVIDIA's free CUDA 13 toolkit (about 3 GB) and restart Murmur to use CUDA.`, then the buttons **Get CUDA 13 ↗** and **Not now** (calls `onDismiss`). **Restart Murmur** appears only after Get CUDA was clicked.
  - **installCuda, settings variant:** `CUDA isn't installed. Your ${gpuName} can run Murmur about twice as fast with CUDA 13.` + **Get CUDA 13 ↗** + **Restart Murmur**.
  - `gpuName` is the DXGI description, which already starts with "NVIDIA" ("NVIDIA GeForce RTX 3060"), so the text reads as in the mockup.
  - **updateDriver, both variants:** `Update your NVIDIA driver to use CUDA.` + **Update NVIDIA driver ↗**. The settings variant adds **Restart Murmur**.
- Produces (`useModelChoice.ts`): `useModelChoice(): { choice: ModelChoice | null; select(id: SpeechModelId): Promise<void> }`.

`describeEngineStatus` changes:
- `downloading` → `Downloading speech model… ${percent}%`, with the percent taken from `download` bytes, or `Reconnecting…` while reconnecting.
- `ready` → `Ready · ${runtimeLabel}`, where `Cuda→CUDA`, `Vulkan→Vulkan`, `Cpu→CPU` and anything else is shown as is.
- `NO_GPU_NOTE` and `showsNoGpuNote` are removed: the advice replaces them.

The Settings engine card shows `status.modelName` in bold, then `EngineStatusLine`, then `<CudaTip variant="settings">`, then `SpeechModelSection`. That section is a radio group of `choice.options` labels; the `recommended` option gets the suffix ` — recommended for this PC`. Selecting another option calls `select`. While `status.state === "ready"` and `status.download` is set, it shows that download's progress line under the group.

- [ ] **Step 1: Write the failing tests**

```ts
// formatDownload.test.ts
expect(formatBytes(1_624_555_275)).toBe("1.6 GB");
expect(formatBytes(487_614_201)).toBe("488 MB");
expect(formatBytes(612_000_000)).toBe("612 MB");
expect(formatSpeed(18_400_000)).toBe("18 MB/s");
expect(formatSpeed(0)).toBeNull();                       // Review Focus 5
expect(formatRemaining(null)).toBeNull();
expect(formatRemaining(Infinity)).toBeNull();            // Review Focus 5
expect(formatRemaining(45)).toBe("less than a minute left");
expect(formatRemaining(70)).toBe("about 1 minute left");
expect(formatRemaining(300)).toBe("about 5 minutes left");
expect(describeDownload({ ...d, bytesReceived: 612_000_000, totalBytes: 1_624_555_275, bytesPerSecond: 18_000_000, secondsRemaining: 56 }))
  .toEqual({ amount: "612 MB of 1.6 GB", detail: "18 MB/s · less than a minute left", percent: 38 });
expect(describeDownload({ ...d, retries: 2, reconnecting: true }).detail).toBe("Reconnecting…");
expect(describeDownload({ ...d, bytesPerSecond: 0, secondsRemaining: null }).detail).toBeNull();

// engineStatusText.test.ts
// ready + runtime "Cuda" → "Ready · CUDA"; "Cpu" → "Ready · CPU"; downloading 38 % from bytes → "Downloading speech model… 38%"

// CudaTip.test.tsx
// installCuda/setup, gpuName "NVIDIA GeForce RTX 3060" →
//   screen.getByText("Your NVIDIA GeForce RTX 3060 can run Murmur about twice as fast.")
// "Get CUDA 13" → apiClient.call("app","openLink",{ target: "cudaDownload" }); then "Restart Murmur" is visible
// "Not now" → onDismiss called
// updateDriver → "Update NVIDIA driver" → openLink nvidiaDriver
// notApplicable, cudaInUse, null → container is empty
// settings variant installCuda → "Restart Murmur" visible at once → apiClient.call("app","restart",{})

// SettingsPage.test.tsx
// models.getChoice → {selected:"largeV3Turbo", recommended:"largeV3Turbo", options:[…]} →
//   "Best accuracy (large, 1.6 GB) — recommended for this PC" checked; clicking "Faster (small, 488 MB)" →
//   apiClient.call("models","setChoice",{ model: "smallEn" })
// engine status with gpu installCuda → the settings-variant tip is shown
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `cd src/WebApp && npm run test:run`
Expected: the new tests FAIL.

- [ ] **Step 3: Implement** the files above, styled to the mockup's Settings card: the tip has a blue outline with a ⚡ icon, and colours come from the existing CSS variables in `index.css`.

- [ ] **Step 4: Run the tests and watch them pass**

Run: `cd src/WebApp && npm run test:run && npm run build && npm run lint`
Expected: all PASS, the type check is clean, and lint is clean.

- [ ] **Step 5: Commit**

`feat(web): download details, CUDA tip and speech-model choice on the Settings card`

---

### Task 10: Web — Setup page and App routing

**Files:**
- Create: `src/WebApp/src/features/setup/SetupPage.tsx`, `SetupPage.css`, `ModelStep.tsx`, `MicrophoneStep.tsx`, `TryItStep.tsx`, `useSetupState.ts`, `useMicLevel.ts`
- Modify: `src/WebApp/src/App.tsx`
- Test: `src/WebApp/src/features/setup/__tests__/SetupPage.test.tsx`, `useMicLevel.test.ts`; `src/WebApp/src/__tests__/App.test.tsx`

**Interfaces:**
- Consumes: `CudaTip`, `describeDownload`, `openLink` (Task 9), `useEngineStatus`, `useSettings`, `useMicrophones`, `useHotkeyName` (existing), `useWebViewEvent`.
- Produces:
  ```ts
  export function useSetupState(): { state: SetupState | null; update(patch: { step?: SetupStep; cudaTipDismissed?: boolean; completed?: boolean }): Promise<void> };
  export function useMicLevel(microphoneId: string | null): { level: number; result: MicTestResult | null };
  //   setup.startMicTest { microphoneId } on mount and when the id changes; setup.stopMicTest on unmount; listens "setup.micLevel"
  export default function SetupPage(props: { state: SetupState; engineStatus: EngineStatus | null;
    update: ReturnType<typeof useSetupState>["update"]; onDone: () => void }): JSX.Element;
  ```

Copy comes verbatim from the spec and mockup:
- **Step chips:** `1 · Speech model`, `2 · Microphone`, `3 · Try it`. A finished step shows `✓ Speech model` / `✓ Microphone`.
- **Step 1 (`ModelStep`):**
  - Heading `Getting Murmur ready`. Body: `Murmur turns speech into text on this PC. It needs its speech model once — about ${formatBytes(total)}.` The dash part is left out when no size is known.
  - While downloading, a card shows `Downloading speech model`, the amount, a progress bar, the detail, and `Resumes if interrupted`. Otherwise it shows `Loading the speech model…`, `✓ Speech model ready`, or the error.
  - Below the card, `<CudaTip variant="setup">` appears unless `cudaTipDismissed` is set. **Not now** calls `update({ cudaTipDismissed: true })`.
  - Footer: `You can close this window — Murmur keeps downloading.` **Next** is always enabled and calls `update({ step: "microphone" })`.
- **Step 2 (`MicrophoneStep`):**
  - Heading `Check your microphone`, line `Say something — the bars should move.`, the microphone picker (the same list and save as Settings), and 8 bars driven by `level`.
  - `listening` → `✓ Microphone access allowed`.
  - `accessDenied` → `Windows is blocking microphone access for Murmur.` + button **Open microphone settings** → `openLink("microphonePrivacy")`.
  - `failed` → the error.
  - Hint (always shown): `Bluetooth headsets take about a second to switch on their microphone — wait for the start sound before you talk.`
  - Footer: `Problems? Windows Settings → Privacy → Microphone`. **Next** → `update({ step: "tryIt" })`.
- **Step 3 (`TryItStep`):**
  - Heading `Try it`.
  - Until the engine is `ready`: `Waiting for the speech model…`.
  - When ready: `Click the box, hold ${hotkey}, say a sentence, and let go.` and a practice `<textarea>`. A `history.added` event while mounted shows `✓ That's it — Murmur types wherever your cursor is.`
  - Hint: `Tap ${hotkey} once for hands-free; tap again to finish. Esc cancels. Change the key in Settings.`
  - Footer: `Murmur lives in the tray from now on.` **Done** is always enabled: `update({ completed: true })`, then `onDone()`.

`App.tsx`:
- Read `useSetupState()`. Render nothing until it is known.
- `showSetup` → `<SetupPage>` fills the window, with no sidebar and no banner.
- `onDone` → page `history`.
- An `openPage` value navigates once on load.

- [ ] **Step 1: Write the failing tests**

```ts
// SetupPage.test.tsx (apiClient mocked as in useEngineStatus.test.ts)
it("starts at the saved step")                                // state.step "microphone" → "Check your microphone"
it("Next on step 1 saves the microphone step")                // update called with { step: "microphone" }
it("shows download amount, speed and time left")              // download 612 MB/1.6 GB @18 MB/s, 56 s → "612 MB of 1.6 GB", "18 MB/s · less than a minute left"
it("hides the CUDA tip once dismissed")                       // cudaTipDismissed true + installCuda → no "Get CUDA 13"
it("offers microphone settings when access is denied")        // startMicTest → { state: "accessDenied" } → button → openLink microphonePrivacy
it("stops the microphone test when leaving step 2")           // Review Focus 3: unmount → apiClient.call("setup","stopMicTest",{})
it("waits for the speech model on step 3")                    // engine loading → "Waiting for the speech model…"
it("celebrates the first dictation")                          // fire the history.added callback → "✓ That's it — Murmur types wherever your cursor is."
it("uses the configured hotkey name")                         // hotkey "F13" → "Click the box, hold F13, say a sentence, and let go."
it("Done completes setup")                                    // update({ completed: true }) then onDone
// App.test.tsx
it("shows setup without the sidebar on first run")
it("shows the sidebar when setup is complete")
it("opens Settings when asked at launch")                     // openPage "settings" → Settings heading visible
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `cd src/WebApp && npm run test:run`
Expected: the new tests FAIL.

- [ ] **Step 3: Implement**, styled to match `mockups/first-run.html`.

- [ ] **Step 4: Run the tests and watch them pass**

Run: `cd src/WebApp && npm run test:run && npm run build && npm run lint`, then `dotnet build Murmur.sln`.
Expected: all PASS, 0 warnings.

- [ ] **Step 5: Commit**

`feat(web): three-step first-run setup`

---

### Task 11: Version 1.1.0, listing text, end-to-end check

**Files:**
- Modify: `version.json` (`{ "version": "1.1.0" }`), `packaging/msix/AppxManifest.xml` (`Version="1.1.0.0"`), `README.md` (Requirements), `docs/store-submission-runbook.md` (§4 disclosures)

**Copy changes:**
- **README Requirements:** the download is "about 1.6 GB, or 488 MB on PCs without a capable graphics card". The CUDA bullet becomes: "an NVIDIA RTX 30-series or newer GPU with the CUDA 13 toolkit installed; Murmur finds the toolkit by itself and says when installing it would help".
- **Runbook disclosures:**
  - The CUDA sentence stays, without "(Murmur looks for the CUDA 13 runtime on PATH)".
  - The first-run bullet becomes: "First run shows a short setup and downloads a speech model of about 1.6 GB (488 MB on PCs without a capable graphics card) from huggingface.co; it resumes if interrupted."
  - Under "Good to know", add the one-time setup and the smaller model.

- [ ] **Step 1: Bump the version** in both files, and update the README and the runbook.

- [ ] **Step 2: Run the full automated suite**

Run: `dotnet test Murmur.sln` and `cd src/WebApp && npm run test:run`
Expected: all PASS.

- [ ] **Step 3: End-to-end on the owner's PC**

This uses a Debug build with `scripts/dev.ps1`. Quit Murmur, then rename `%LOCALAPPDATA%\Murmur` to `Murmur.e2e-backup`. Restore it at the end (Murmur quit, the test folder renamed to `Murmur.e2e-fresh`, the backup renamed back), and never delete either folder. Check each item in the log (`%LOCALAPPDATA%\Murmur\Logs`) and in the window.
1. **Fresh profile:** setup shows step 1 with live amount, speed and ETA. With CUDA installed there is no CUDA tip.
2. **Resume:** kill Murmur at about 30 % (`Stop-Process -Name Murmur.UI`) and start it again. The log shows "Resuming the … download at …", and the bar continues from about 30 %.
3. **Notification:** with the window minimized when the download finishes, a "Murmur is ready" notification appears.
4. **Try it:** dictate into the practice box, the check mark appears, and Done shows History.
5. **Upgrader:** restore the backup. Setup does not show. A Settings save keeps `setupCompleted`, `speechModel` and `setupStep` in `settings.json`.
6. **CUDA tip:** create `simulate-no-cuda-runtime` in the data root and restart. Settings shows "Ready · Vulkan" and the tip with "NVIDIA GeForce RTX 3090". Delete the file and click **Restart Murmur**. The window reopens on Settings showing "Ready · CUDA".
7. **No GPU:** with `simulate-no-gpu` on a fresh profile, "Whisper small (English)" is chosen and there is no tip. Remove the file.
8. **Model switch:** Settings → Faster (small). The progress line appears while dictation still works on large. It then shows "Ready · CUDA" with "Whisper small (English)", and `ggml-large-v3-turbo.bin` is gone. Switch back (this downloads 1.6 GB again).
9. **Packaged:** build the MSIX (`build/build-msix.ps1`), register the staged layout as the runbook describes, and click **Restart Murmur**. The new process logs "Running from its MSIX package" and reopens on Settings. If it starts unpackaged instead, record it in the ledger, and **Ruling:** the restart starts `shell:AppsFolder\MortenBrudvik.MurmurVoiceTyping_x7deb10xce5bj!App` through `explorer.exe` (opening on History).
10. **Owner-only:** pulling the network mid-download shows "Reconnecting…" and recovers. Ask the owner to do this; do not change firewall or adapter settings.

- [ ] **Step 4: Commit**

`chore: version 1.1.0 and listing text for first-run setup`
