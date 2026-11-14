# Splash and About Dialog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Murmur a native animated splash on launches that open the window, and an About dialog in the main window opened from the sidebar and the tray.

**Architecture:** The About dialog is a React modal fed by one new MediatR request (`app` / `getAbout`) and the existing engine status; its links go through new fixed `LinkTarget`s. The splash is a small borderless WPF window drawn by one custom element on a single clock; a WPF-free gate in Core decides when it closes, from a new `app` / `ready` call the web UI makes after its first real render. The assembly version comes from `version.json`.

**Tech Stack:** .NET 10, WPF + WPF-UI, WebView2, MediatR, Autofac, React + TypeScript + Vitest, xUnit v3 + NSubstitute + Shouldly, `Microsoft.Extensions.TimeProvider.Testing` (Core tests only).

**Spec:** `docs/superpowers/specs/2026-11-14-splash-about-design.md` (mock-up: `docs/superpowers/specs/mockups/splash-about.html`). Read both before starting; the mock-up's script is the reference for the animation.

**Workspace:** The owner asked for this to be built in a git worktree. Create it with superpowers:using-git-worktrees on a branch named `feat/splash-about`, and copy the spec, the mock-up and this plan into it as the first commit (they are untracked in the main checkout). Commit per task on that branch. Do not push or merge to `main` without the owner saying so.

## Global Constraints

- `TreatWarningsAsErrors` is on: every build has zero warnings. Verify with `dotnet build Murmur.sln -p:SkipFrontendBuild=true`.
- File-scoped namespaces; private fields `_camelCase`; tests named `MethodName_Scenario_ExpectedResult`; constructor injection only; `ConfigureAwait(false)` in Core; no `async void` except event handlers; no logic in view code-behind; `DynamicResource` for theme colours in XAML; `Log.ForContext<T>()`; all JSON through `JsonUtils`.
- The web UI never passes an address or a path to the shell. Every link is a `LinkTarget`.
- A sign-in start shows no splash and no window, exactly as today. Hooks and the speech engine start exactly as today.
- Fixed copy, verbatim: tagline `Hold a key, talk, and the words land where you were typing. Nothing you say leaves this PC.`; channels `Microsoft Store` and `Built from source`; footer `© <build year> Morten Brudvik`.
- Fixed addresses: `https://github.com/mortenbrudvik/murmur`, `https://brudvik.com/murmur/privacy`, `https://github.com/mortenbrudvik/murmur/blob/main/LICENSE`, `https://brudvik.com`.
- Splash numbers: window 460 by 280 DIPs; tile 96 wide, left 182, top 46; minimum 2.5 s; timeout 8 s; fade-out 150 ms.
- Commit messages follow the repo's style (`feat(ui): …`, `feat(core): …`, `feat(web): …`, `build: …`, `test(…): …`) and end with the session's `Co-Authored-By` line.

## Review Focus

1. **The web UI never calls `app.ready`** (Vite server down in Debug, a script error): the splash must still close at 8 s and the main window must appear. Pinned in Task 6 (`NeverReady_ClosesAtTheTimeout`) and checked by hand in Task 10.
2. **The user clicks the tray or launches Murmur a second time while the splash is up:** the splash closes at once and the window shows; nothing is shown twice. Pinned in Task 6 (`Skip_AfterReady…`, `Closed_IsRaisedOnlyOnce`) and wired in Task 9.
3. **The user has switched to another app before the splash closes:** the main window opens without taking focus. Pinned in Task 9 (`ShouldActivate_…`).
4. **History text with irregular whitespace, or an empty history:** word count is right and the row reads "Nothing yet". Pinned in Task 2 and Task 4.
5. **Engine not on CUDA, or failed:** About never names a graphics card it cannot vouch for. Pinned in Task 4 (`describeRunningOn` table).

---

### Task 1: The assembly version comes from `version.json`

**Files:**
- Modify: `Directory.Build.props`, `src/Murmur.Core/Services/MurmurPaths.cs`, `src/Murmur.UI/AppModule.cs` (`RegisterCore`), `CLAUDE.md` (the sentence on where the version lives)
- Create: `src/Murmur.Core/Services/AppInfo.cs`
- Test: `tests/Murmur.Core.Tests/Services/AppInfoTests.cs`

**Interfaces:**
- Produces: `MurmurPaths.Root` (`string`, the data root). `IAppInfo { string Version; string Channel; string DataFolder; string WindowsBuild; }` and `AppInfo(IPackageIdentity identity, MurmurPaths paths) : IAppInfo`, with `internal static string FormatVersion(string? informationalVersion)` and `internal static string FormatWindows(Version os)`. Registered `As<IAppInfo>().SingleInstance()`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("1.1.0", "1.1.0")]
[InlineData("1.1.0+3f2a9c1", "1.1.0")]
[InlineData("1.1.0.0", "1.1.0")]
[InlineData(null, "0.0.0")]
public void FormatVersion_KeepsThreeParts(string? informational, string expected) =>
    AppInfo.FormatVersion(informational).ShouldBe(expected);

[Theory]
[InlineData(26200, "Windows 11, build 26200")]
[InlineData(19045, "Windows 10, build 19045")]
public void FormatWindows_NamesTheReleaseByBuild(int build, string expected) =>
    AppInfo.FormatWindows(new Version(10, 0, build)).ShouldBe(expected);

[Theory]
[InlineData(true, "Microsoft Store")]
[InlineData(false, "Built from source")]
public void Channel_FollowsPackageIdentity(bool packaged, string expected) { /* IPackageIdentity substitute */ }

[Fact]
public void Version_IsTheOneInVersionJson()
{
    // Walk up from AppContext.BaseDirectory to the folder holding version.json, read its "version".
    new AppInfo(identity, new MurmurPaths(@"C:\x")).Version.ShouldBe(versionFromFile);
}

[Fact]
public void DataFolder_IsThePathsRoot() =>
    new AppInfo(identity, new MurmurPaths(@"C:\data\Murmur")).DataFolder.ShouldBe(@"C:\data\Murmur");
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/Murmur.Core.Tests --filter "FullyQualifiedName~AppInfoTests"` — Expected: does not compile (`AppInfo` missing).

- [ ] **Step 3: Set the version in `Directory.Build.props`**

Add `Version` read from `$(MSBuildThisFileDirectory)version.json` with a `Regex.Match` property function on `"version"\s*:\s*"([^"]+)"`, and `IncludeSourceRevisionInInformationalVersion` = `false`.

- [ ] **Step 4: Add `MurmurPaths.Root` and implement `AppInfo`**

`Version` comes from this assembly's `AssemblyInformationalVersionAttribute` through `FormatVersion`. Windows 11 is build 22000 and above.

- [ ] **Step 5: Register `AppInfo` and update `CLAUDE.md`**

In `CLAUDE.md`, the MSIX paragraph says the version lives in `version.json` and the manifest; add that `Directory.Build.props` gives every assembly the `version.json` value.

- [ ] **Step 6: Run the tests and the build**

Run: `dotnet test tests/Murmur.Core.Tests --filter "FullyQualifiedName~AppInfoTests"` — Expected: PASS. Then `dotnet build Murmur.sln -p:SkipFrontendBuild=true` — Expected: 0 warnings.

- [ ] **Step 7: Commit** — `build: take the assembly version from version.json`

---

### Task 2: History totals

**Files:**
- Modify: `src/Murmur.Core/Models/DomainModels.cs`, `src/Murmur.Core/Services/IHistoryStore.cs`, `src/Murmur.Core/Services/HistoryStore.cs`
- Test: `tests/Murmur.Core.Tests/Services/HistoryStoreTests.cs`

**Interfaces:**
- Produces: `public sealed record HistoryTotals(int Dictations, long Words, DateTimeOffset? Since);` and `HistoryTotals IHistoryStore.Totals();`. A word is a run of non-whitespace characters. `Since` is the oldest entry's timestamp, null when empty.

- [ ] **Step 1: Write the failing tests** (use the file's existing store and clock helpers)

```csharp
[Fact] public void Totals_EmptyHistory_IsZeroWithNoSince()   // (0, 0, null)
[Fact] public void Totals_CountsWordsAsRunsOfNonWhitespace()
// Add("  hello   world\n\tagain ") and Add("one") -> Dictations 2, Words 4
[Fact] public void Totals_Since_IsTheOldestEntrysTimestamp()
[Fact] public void Totals_AfterDelete_NoLongerCountsThatEntry()
[Fact] public void Totals_AfterClear_IsZero()
```

- [ ] **Step 2: Run** `dotnet test tests/Murmur.Core.Tests --filter "FullyQualifiedName~HistoryStoreTests.Totals"` — Expected: does not compile.
- [ ] **Step 3: Implement `Totals()`** under the store's lock, over `EnsureLoaded()`. Update any `IHistoryStore` test doubles that are hand-written classes.
- [ ] **Step 4: Run the same filter** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(core): word and dictation totals for the history`

---

### Task 3: About data and the new links

**Files:**
- Modify: `src/Murmur.Core/Models/DomainModels.cs` (`LinkTarget`, `AboutInfo`), `src/Murmur.Core/Services/AppLinks.cs`, `src/Murmur.Core/Features/App/OpenLinkRequest.cs`, `src/Murmur.UI/App.xaml.cs` (registry)
- Create: `src/Murmur.Core/Features/App/GetAboutRequest.cs`
- Test: `tests/Murmur.Core.Tests/Features/AppHandlerTests.cs`, `tests/Murmur.Interop.Tests/RequestJsonContractTests.cs`

**Interfaces:**
- Consumes: `IAppInfo` (Task 1), `IHistoryStore.Totals()` (Task 2).
- Produces:
  - `LinkTarget` gains `Repository, PrivacyPolicy, License, HomePage, DataFolder`.
  - `OpenLinkHandler(ILinkOpener opener, MurmurPaths paths)`: for `DataFolder` it creates `paths.Root` if missing and opens that path; `AppLinks.For(DataFolder)` throws like an unknown target.
  - `public sealed record AboutInfo(string Version, string Channel, string DataFolder, string WindowsBuild, int Dictations, long Words, DateTimeOffset? Since);`
  - `public record GetAboutRequest : IRequest<AboutInfo>;` with `GetAboutHandler(IAppInfo app, IHistoryStore history)`, registered as `registry.Register<GetAboutRequest>("app", "getAbout")`.

- [ ] **Step 1: Write the failing tests**

```csharp
// AppHandlerTests: extend the existing theory with
[InlineData(LinkTarget.Repository, "https://github.com/mortenbrudvik/murmur")]
[InlineData(LinkTarget.PrivacyPolicy, "https://brudvik.com/murmur/privacy")]
[InlineData(LinkTarget.License, "https://github.com/mortenbrudvik/murmur/blob/main/LICENSE")]
[InlineData(LinkTarget.HomePage, "https://brudvik.com")]

[Fact] public async Task OpenLink_DataFolder_CreatesAndOpensThePathsRoot()
// temp root that does not exist -> Directory.Exists(root) is true and opener.Received(1).Open(root)

[Fact] public async Task GetAbout_ReturnsAppInfoAndHistoryTotals()
// IAppInfo and IHistoryStore substitutes -> every AboutInfo field equals its source

// RequestJsonContractTests
[Fact] public void AboutInfo_SerializesCamelCaseWithNullSince()
// JsonUtils.Serialize(new AboutInfo("1.1.0","Microsoft Store",@"C:\d","Windows 11, build 26200",0,0,null))
// contains "\"windowsBuild\":" and "\"since\":null"
[Fact] public void OpenLinkRequest_DeserializesTheNewTargets()   // {"target":"homePage"} -> LinkTarget.HomePage
```

- [ ] **Step 2: Run** `dotnet test tests/Murmur.Core.Tests --filter "FullyQualifiedName~AppHandlerTests"` — Expected: does not compile.
- [ ] **Step 3: Implement** the enum values, `AppLinks`, the handler change, `GetAboutRequest`, and the registration. Fix the existing `new OpenLinkHandler(opener)` call sites in tests.
- [ ] **Step 4: Run** `dotnet test Murmur.sln --no-restore` — Expected: PASS, including `AppModuleTests` (the handler must resolve from the container).
- [ ] **Step 5: Commit** — `feat(core): about info request and fixed links for the About dialog`

---

### Task 4: The About dialog (web)

**Files:**
- Modify: `src/WebApp/src/types/domain.ts`, `src/WebApp/src/components/Sidebar.tsx`, `src/WebApp/src/App.tsx`, `src/WebApp/src/App.css`
- Create: `src/WebApp/src/features/about/aboutText.ts`, `useAbout.ts`, `HumHeader.tsx`, `AboutDialog.tsx`, `AboutDialog.css`
- Test: `src/WebApp/src/features/about/__tests__/aboutText.test.ts`, `AboutDialog.test.tsx`; extend `src/WebApp/src/__tests__/App.test.tsx`

**Interfaces:**
- Consumes: `app` / `getAbout` (Task 3), the new `LinkTarget`s as `"repository" | "privacyPolicy" | "license" | "homePage" | "dataFolder"`, `openLink` from `features/settings/appActions.ts`.
- Produces:
  - `interface AboutInfo { version: string; channel: string; dataFolder: string; windowsBuild: string; dictations: number; words: number; since: string | null }`
  - `describeRunningOn(status: EngineStatus | null): string`, `describeDictated(info: AboutInfo, locale?: string): { main: string; detail: string | null }`, `buildDetails(info: AboutInfo, status: EngineStatus | null): string`
  - `useAbout(open: boolean): AboutInfo | null` — fetches each time `open` becomes true.
  - `<AboutDialog open engineStatus onClose />`, `<Sidebar page onNavigate onAbout />`.
  - `App` keeps `aboutOpen` state; Task 7 adds the tray's way in.

- [ ] **Step 1: Write the failing tests for `aboutText.ts`**

```ts
// describeRunningOn
ready + runtime "Cuda" + gpu.gpuName "NVIDIA GeForce RTX 3090"  -> "NVIDIA GeForce RTX 3090 · CUDA"
ready + runtime "Cuda" + gpuName null                           -> "CUDA"
ready + runtime "Vulkan" + gpu.gpuName "NVIDIA GeForce RTX 3060" -> "Vulkan"
ready + runtime "Cpu"                                           -> "CPU"
downloading / loading / notStarted / null                       -> "Starting…"
error                                                           -> "Not running"
// describeDictated (locale "en-US")
{ dictations: 0, words: 0, since: null }  -> { main: "Nothing yet", detail: null }
{ dictations: 1204, words: 18420, since: "2026-10-03T08:00:00+00:00" }
   -> { main: "18,420 words", detail: "in 1,204 dictations since October 2026" }
{ dictations: 1, words: 1, since: … }     -> { main: "1 word", detail: "in 1 dictation since October 2026" }
// buildDetails: exactly the four lines of spec §6.5, with "Starting" and "Not running" per §6.2.1,
// and "Model: Not chosen yet" while modelName is empty
```

- [ ] **Step 2: Run** `npm run test:run -- aboutText` in `src/WebApp` — Expected: FAIL (module missing).
- [ ] **Step 3: Implement `aboutText.ts`** (runtime labels as in `engineStatusText.ts`; reuse its label function by exporting it rather than copying).
- [ ] **Step 4: Run** the same command — Expected: PASS.

- [ ] **Step 5: Write the failing dialog tests** (mock `apiClient` as the other tests do; `getAbout` resolves a fixture)

```ts
it("is not in the document while closed")
it("shows name, 'Version 1.1.0 · Microsoft Store', the tagline and the four rows")   // role="dialog", aria-modal, labelled by "Murmur"
it("shows 'Not chosen yet' and 'Starting…' before the engine reports")
it("updates the model and running-on rows when the engine status changes while open")
it("shows the data folder path the shell reports")
it("focuses Close on open, closes on Esc, on Close and on a click on the backdrop")
it("keeps Tab inside the dialog")
it("opens each link by its target")
// Source on GitHub -> "repository", Privacy policy -> "privacyPolicy", MIT License -> "license",
// Morten Brudvik -> "homePage", Open folder -> "dataFolder"; each via apiClient.call("app","openLink",{target})
it("copies the details and says 'Copied'")    // navigator.clipboard.writeText called with buildDetails(...)
it("says 'Could not copy' when the clipboard refuses")
```

In `App.test.tsx`: the sidebar's About button opens the dialog and focus returns to it on close; `Page` is unchanged.

- [ ] **Step 6: Run** `npm run test:run -- AboutDialog` — Expected: FAIL.
- [ ] **Step 7: Implement** `useAbout`, `HumHeader`, `AboutDialog`, the sidebar item and the `App` wiring.

Markup and styling follow the mock-up's `.about` block, in the app's own tokens (`index.css`). The links are buttons styled as links (the WebView blocks navigation to other hosts). `HumHeader` is the mock-up's `hum()` drawing at 460 by 104 with a 64 px tile; it animates only while open and not under `prefers-reduced-motion`. The sidebar's About sits after a flex spacer; in the narrow layout the spacer collapses. The footer year is `__BUILD_YEAR__` defined in `vite.config.ts` from the build date.

- [ ] **Step 8: Run** `npm run test:run` and `npm run build` in `src/WebApp` — Expected: all PASS, build clean.
- [ ] **Step 9: Commit** — `feat(web): About dialog opened from the sidebar`

---

### Task 5: The UI tells the shell it has drawn

**Files:**
- Create: `src/Murmur.Core/Setup/UiReadiness.cs`, `src/Murmur.Core/Features/App/UiReadyRequest.cs`
- Modify: `src/Murmur.UI/AppModule.cs`, `src/Murmur.UI/App.xaml.cs` (registry), `src/WebApp/src/App.tsx`
- Test: `tests/Murmur.Core.Tests/Setup/UiReadinessTests.cs`, `tests/Murmur.Core.Tests/Features/AppHandlerTests.cs`, `src/WebApp/src/__tests__/App.test.tsx`

**Interfaces:**
- Produces:
  - `public sealed class UiReadiness { public bool IsReady { get; } public event Action? BecameReady; public void MarkReady(); }` — thread-safe; `BecameReady` is raised once, on the calling thread; a handler added after it is ready is not called.
  - `public record UiReadyRequest : IRequest;` with `UiReadyHandler(UiReadiness ui)`, registered as `("app", "ready")`. `UiReadiness` registered `SingleInstance()`.
  - Web: `App` calls `apiClient.call("app", "ready", {})` once, in an effect that runs after the first render in which `setup` is not null. A failed call is ignored.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void MarkReady_RaisesBecameReadyOnce_AndSetsIsReady()      // two MarkReady calls -> one event
[Fact] public async Task MarkReady_FromManyThreads_RaisesOnce()
[Fact] public async Task UiReady_MarksTheUiReady()                        // handler
```
```ts
it("tells the shell it is ready once the setup state has arrived, and only once")
it("does not report ready while the setup state is still loading")
```

- [ ] **Step 2: Run** both suites — Expected: FAIL.
- [ ] **Step 3: Implement** the class, the request, the registrations and the effect.
- [ ] **Step 4: Run** `dotnet test Murmur.sln --no-restore` and `npm run test:run` — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(core): the web UI reports its first render to the shell`

---

### Task 6: The splash gate and the status wording (Core, no WPF)

**Files:**
- Create: `src/Murmur.Core/Setup/SplashGate.cs`, `src/Murmur.Core/Speech/EngineStatusText.cs`
- Modify: `src/WebApp/src/features/settings/engineStatusText.ts` (a comment naming the C# twin)
- Test: `tests/Murmur.Core.Tests/Setup/SplashGateTests.cs`, `tests/Murmur.Core.Tests/Speech/EngineStatusTextTests.cs`

**Interfaces:**
- Produces:
  - `public enum SplashCloseReason { Ready, Skipped, TimedOut }`
  - `public sealed class SplashGate : IDisposable` with `public static readonly TimeSpan DefaultMinimum` (2.5 s), `DefaultTimeout` (8 s); `SplashGate(TimeProvider time, TimeSpan minimum, TimeSpan timeout)` (the clock starts at construction); `void MarkReady()`; `void Skip()`; `event Action<SplashCloseReason>? Closed` raised exactly once, on whichever thread caused it.
  - `public enum EngineStatusTone { Progress, Info, Ready, Error }`, `public sealed record EngineStatusLine(string Text, EngineStatusTone Tone);`, `public static EngineStatusLine EngineStatusText.Describe(EngineStatus status)` — the same text as `describeEngineStatus` in `engineStatusText.ts`.

- [ ] **Step 1: Write the failing gate tests** (`FakeTimeProvider`)

```csharp
[Fact] public void ReadyBeforeTheMinimum_ClosesAtTheMinimum_AsReady()   // ready at 1 s: nothing until 2.5 s
[Fact] public void ReadyAfterTheMinimum_ClosesAtOnce_AsReady()          // advance 3 s, MarkReady
[Fact] public void Skip_ClosesAtOnce_AsSkipped()                         // at 0.2 s
[Fact] public void NeverReady_ClosesAtTheTimeout_AsTimedOut()            // nothing at 7.9 s, closed at 8 s
[Fact] public void Skip_AfterReadyButBeforeTheMinimum_ClosesAsSkipped()
[Fact] public void Closed_IsRaisedOnlyOnce()                             // ready, skip, advance past timeout -> one event
[Fact] public void Dispose_BeforeClosing_NeverRaisesClosed()
```

- [ ] **Step 2: Write the failing wording tests** — one case per branch of `describeEngineStatus`, with the strings copied from `engineStatusText.test.ts`: `"Loading model…"`, `"Switching to Whisper small.en…"`, `"Downloading speech model… 38%"`, `"Reconnecting…"`, `"Ready · CUDA"`, `"Ready · CPU"`, `"Ready · Vulkan"`, `"Ready"`, the error text and its fallback `"The speech model could not be loaded."`. Percent is floor of whole bytes times 100 over the total, 0 when the total is 0.
- [ ] **Step 3: Run** `dotnet test tests/Murmur.Core.Tests --filter "FullyQualifiedName~SplashGateTests|FullyQualifiedName~EngineStatusTextTests"` — Expected: does not compile.
- [ ] **Step 4: Implement both.** The gate uses `time.CreateTimer` for the minimum and the timeout.
- [ ] **Step 5: Run** the same filter — Expected: PASS.
- [ ] **Step 6: Commit** — `feat(core): splash close rules and the engine status wording`

---

### Task 7: About from the tray

**Files:**
- Create: `src/Murmur.Core/Setup/AboutLauncher.cs`, `src/Murmur.Core/Features/App/ShowAboutNotification.cs`, `src/Murmur.Interop/Handlers/ShowAboutEventHandler.cs`
- Modify: `src/Murmur.UI/Tray/TrayIconHost.cs`, `src/Murmur.UI/AppModule.cs`, `src/Murmur.UI/App.xaml.cs`, `src/WebApp/src/App.tsx`
- Test: `tests/Murmur.Core.Tests/Setup/AboutLauncherTests.cs`, `tests/Murmur.Interop.Tests/EventHandlerTests.cs`, `tests/Murmur.UI.Tests/Tray/TrayIconHostTests.cs`, `src/WebApp/src/__tests__/App.test.tsx`

**Interfaces:**
- Consumes: `UiReadiness` (Task 5), `App` `aboutOpen` state (Task 4).
- Produces:
  - `public record ShowAboutNotification : INotification;` → `ShowAboutEventHandler` publishes the event `"app.showAbout"` with an empty object.
  - `public sealed class AboutLauncher(UiReadiness ui, IMediator mediator) { public void Show(); }` — publishes now when the UI is ready; otherwise remembers the request and publishes once when `BecameReady` fires. Several early requests give one event.
  - `TrayIconHost.AboutRequested` (`event Action?`), raised by a menu item **About Murmur** placed after "Open Murmur" and before the separator. `App` handles it with `ShowMainWindow()` then `AboutLauncher.Show()`.
  - Web: `useWebViewEvent("app.showAbout", …)` in `App` opens the dialog, also over the setup page.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Show_UiReady_PublishesAtOnce()
[Fact] public void Show_UiNotReady_PublishesWhenItBecomesReady()
[Fact] public void Show_TwiceBeforeReady_PublishesOnce()
[Fact] public async Task ShowAbout_PublishesTheAppShowAboutEvent()      // EventHandlerTests
[Fact] public void Show_Menu_HasOpenAboutSeparatorQuit_InThatOrder()    // TrayIconHostTests, on the STA host
```
```ts
it("opens About when the shell asks, also during first-run setup")
```

- [ ] **Step 2: Run** the suites — Expected: FAIL.
- [ ] **Step 3: Implement.** In `App.tsx` the `app.showAbout` listener is registered from the first mount, before `app.ready` is sent, so an early request is not lost. Render `AboutDialog` in both the setup frame and the shell.
- [ ] **Step 4: Run** `dotnet test Murmur.sln --no-restore` and `npm run test:run` — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(ui): About Murmur in the tray menu`

---

### Task 8: Spike — load the WebView before the main window is seen

This task is an experiment with a written result. No tests; its deliverable is `MainWindow.Preload`/`Reveal` working by the best option that holds, and a short note.

**Files:**
- Modify: `src/Murmur.WebView/WebViewHandler.cs`, `src/Murmur.UI/MainViewModel.cs`, `src/Murmur.UI/MainWindow.xaml.cs`
- Create: `docs/superpowers/specs/2026-11-14-splash-spike-notes.md`

**Interfaces:**
- Produces: `public Task WebViewHandler.StartAsync()` — idempotent; the existing `Loaded` hook calls it, so a start before `Loaded` does not initialise twice. `public void MainWindow.Preload()` — starts the WebView without the window being seen. `public void MainWindow.Reveal(bool activate)` — makes the window visible, centred, restored if minimised, activated only when asked. Task 9 uses only these two.

- [ ] **Step 1: Try option 1.** `new WindowInteropHelper(window).EnsureHandle()`, then `StartAsync()`, window never shown. Run a Release build (`dotnet run -c Release --project src/Murmur.UI`) with a temporary log line when `app.ready` arrives. Pass: `app.ready` is logged while no Murmur window is on screen.
- [ ] **Step 2: If it fails, try option 2.** Show the window off-screen (`Left = -32000`, `ShowActivated = false`, `ShowInTaskbar = false`), then on reveal restore the taskbar flag and centre it on the primary work area. Pass: as step 1, with no flash and no taskbar button before the reveal, and Mica intact after it.
- [ ] **Step 3: If that fails, use option 3.** `Preload()` shows the window normally without activating it; `Reveal` only activates.
- [ ] **Step 4: Check both reveals** on the option that passed: `Reveal(true)` takes focus; `Reveal(false)`, called while Notepad has focus, leaves Notepad focused and typing.
- [ ] **Step 5: Write the notes file**: which option, what was observed for each one tried, and anything Task 9 must know. Remove the temporary log line.
- [ ] **Step 6: Run** `dotnet test Murmur.sln --no-restore` — Expected: PASS (nothing else changed behaviour: `ShowMainWindow` still shows and activates).
- [ ] **Step 7: Commit** — `feat(ui): start the main window's WebView before the window is shown`

---

### Task 9: The splash window and its hand-over

**Files:**
- Create: `src/Murmur.UI/Splash/SplashTimeline.cs`, `HumGeometry.cs`, `SplashCanvas.cs`, `SplashViewModel.cs`, `SplashWindow.xaml`, `SplashWindow.xaml.cs`, `SplashHandoff.cs`
- Modify: `src/Murmur.UI/App.xaml.cs`
- Test: `tests/Murmur.UI.Tests/Splash/SplashTimelineTests.cs`, `HumGeometryTests.cs`, `SplashViewModelTests.cs`, `SplashHandoffTests.cs`; `tests/Murmur.UI.Tests/AppStartupTests.cs`

**Interfaces:**
- Consumes: `SplashGate`, `EngineStatusText` (Task 6), `UiReadiness` (Task 5), `IAppInfo.Version` (Task 1), `MainWindow.Preload`/`Reveal` (Task 8).
- Produces:
  - `internal static class SplashTimeline` — `double Morph(double t)`, `TileReveal(double t)`, `StatusOpacity(double t)`, `int TypedLetters(double t)`, `bool CaretVisible(double t)`, `const double End = 4.3`. Values from spec §5.1 and the mock-up's `render`.
  - `internal static class HumGeometry` — `Point[] RestPoints(int count)` (the icon's line in splash coordinates, flat ends at x = 0 and 460) and `void PointsAt(double t, Point[] rest, Point[] into)`.
  - `internal sealed class SplashCanvas : FrameworkElement` with a `Time` dependency property; `OnRender` draws line, glow, tile, name and caret for that time.
  - `internal sealed partial class SplashViewModel : ObservableObject` — `Version`, `StatusText`, `StatusTone`, `void Apply(EngineStatus status)`.
  - `SplashWindow(SplashViewModel vm)` with `event Action? SkipRequested` and `Task FadeOutAndCloseAsync()`.
  - `internal static class SplashHandoff { public static bool ShouldActivate(uint foregroundProcessId, uint ownProcessId); }`
  - `internal static bool App.ShouldShowSplash(LaunchArguments launch, IPackageIdentity identity)` = not a sign-in start.

- [ ] **Step 1: Write the failing tests**

```csharp
// SplashTimelineTests
Morph(0.9) == 0; Morph(1.65) == 1; Morph(1.275) is 0.5 within 0.01
TileReveal(1.45) == 0; TileReveal(1.9) == 1
StatusOpacity(0.3) == 0; StatusOpacity(0.6) == 1
TypedLetters(1.94) == 0; TypedLetters(1.95) == 1; TypedLetters(2.5) == 6; TypedLetters(9) == 6
CaretVisible(1.7) false; CaretVisible(2.0) true (still typing)

// HumGeometryTests
RestPoints(320): first point x == 0, last x == 460, both at the baseline (y within 0.5 of 46 - 4*s + 43*s, s = 96.0/56)
RestPoints: the highest point is the top of the humps, inside the tile (y between 46 and 142, x between 182 and 278)
PointsAt(0, …): first and last points are on the baseline (the wave is tapered to zero at the edges)
PointsAt(SplashTimeline.End, …) equals RestPoints within 0.01

// SplashViewModelTests
Apply(loading)  -> StatusText "Loading model…", StatusTone Info
Apply(ready on Cuda) -> "Ready · CUDA", Ready

// SplashHandoffTests
ShouldActivate(42, 42) true; ShouldActivate(7, 42) false; ShouldActivate(0, 42) false

// AppStartupTests
ShouldShowSplash: manual start true; restarted true; --minimized false
```

- [ ] **Step 2: Run** `dotnet test tests/Murmur.UI.Tests --filter "FullyQualifiedName~Splash|FullyQualifiedName~AppStartupTests"` — Expected: does not compile.
- [ ] **Step 3: Implement the pure parts** (`SplashTimeline`, `HumGeometry`, `SplashViewModel`, `SplashHandoff`, `ShouldShowSplash`) and run the filter — Expected: PASS.

The icon's line in icon units is `M0 43 H15 a4 4 0 0 0 4 -4 V28.5 a6.5 6.5 0 0 1 13 0 V43 V28.5 a6.5 6.5 0 0 1 13 0 V39 a4 4 0 0 0 4 4 H64`; scale by `s = 96/56`, offset so the tile's corner (4, 4) lands on (182, 46), and extend the two `H` ends to x = 0 and x = 460. Sample it evenly by length. The speech wave is the mock-up's `speech(x, t)`.

- [ ] **Step 4: Build `SplashCanvas` and `SplashWindow`.**

Window: `WindowStyle="None"`, `AllowsTransparency="True"`, transparent background, `ShowInTaskbar="False"`, `Topmost="True"`, `WindowStartupLocation="CenterScreen"`, 460 by 280 plus a shadow margin; a `Border` with background `#14161D`, corner radius 12, 1 DIP border `#1AFFFFFF`. `SplashCanvas.Time` is animated 0 → 4.3 linearly over 4.3 s and holds; when `SystemParameters.ClientAreaAnimation` is false it is set to 4.3 at once. Status line and version are `TextBlock`s bound to the view model, faded in by `StatusOpacity`. Pulsing dots as in `PillWindow.xaml` for the Progress and Info tones; a green dot for Ready; error colour for Error. Colours: line gradient `#8FB3FF` → `#B59BFF`; tile gradient `#56A0FF`, `#7A78FF` at 0.55, `#AE62F2`; text `#E9ECF5`; muted `#9AA3B8`; caret and dots `#8FB3FF`; ok `#3ECF8E`; error `#FF6B6B`. The window's code-behind only forwards a mouse click and any key to `SkipRequested` and runs the fade.

- [ ] **Step 5: Wire it into `App.Start`.**
  - After the single-instance check (step 3) and before the container: if `ShouldShowSplash(launch, new PackageIdentity())`, create the view model and window, show it, and create a `SplashGate(TimeProvider.System, SplashGate.DefaultMinimum, SplashGate.DefaultTimeout)`. The splash must not become `Application.MainWindow`.
  - Once the container exists: set the view model's version; `UiReadiness.BecameReady` → `gate.MarkReady()`; `SkipRequested` → `gate.Skip()`; engine `StatusChanged` and the engine's current status → `vm.Apply` on the dispatcher.
  - At step 13: with a splash, call `_mainWindow.Preload()` instead of `ShowMainWindow()`.
  - `gate.Closed` → on the dispatcher: `_mainWindow.Reveal(SplashHandoff.ShouldActivate(foreground pid, own pid))`, then `FadeOutAndCloseAsync()`, then dispose the gate.
  - `ShowMainWindow()` called while the splash is up (tray, second launch) → `gate.Skip()` and nothing else. `Quit` and `FailStartup` close the splash and dispose the gate.

- [ ] **Step 6: Run** `dotnet build Murmur.sln -p:SkipFrontendBuild=true` (0 warnings) and `dotnet test Murmur.sln --no-restore` — Expected: PASS.
- [ ] **Step 7: Commit** — `feat(ui): animated splash on a launch that opens the window`

---

### Task 10: Whole-feature check

**Files:** none new; fixes only.

- [ ] **Step 1: Run everything.** `dotnet build Murmur.sln` (0 warnings), `dotnet test Murmur.sln`, and in `src/WebApp`: `npm run test:run`, `npm run build`.
- [ ] **Step 2: Run the Release app** (`dotnet run -c Release --project src/Murmur.UI`) and check by hand, noting each result:
  1. The splash plays as in the mock-up, the status line is there from the start, the main window appears already drawn at about 2.5 s.
  2. A click on the splash closes it at once; so does a key.
  3. Started with `--minimized`: no splash, no window, tray icon only. Opening from the tray then shows the window with no splash.
  4. Alt-Tab to Notepad during the splash: the main window opens behind without taking focus.
  5. Debug build with the Vite server stopped: the splash closes at 8 s.
  6. About from the sidebar and from the tray (also with the window never opened): every row is true for this PC; all five links open; Copy details pastes the four lines; Esc closes.
  7. Windows "Animation effects" off: the splash shows its final frame; the About header does not move.
- [ ] **Step 3: Fix what the checks found**, each with a test where one is possible, and commit per fix.
- [ ] **Step 4: Tell the owner** what was verified, what could not be (anything in step 2 that needs his eyes, the Store build's channel text), and that the branch is ready for his review. Do not merge or push.
