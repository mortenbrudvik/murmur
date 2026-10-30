# CLAUDE.md

## Project Overview

Murmur is a local voice-typing tray app for Windows: hold a hotkey, talk, Whisper transcribes on the GPU, and the text is pasted into the focused app. Architecture: **Hybrid (WPF shell + React in WebView2)**.

Design spec: `docs/superpowers/specs/2026-10-29-murmur-design.md`. Implementation plan: `docs/superpowers/plans/2026-10-29-murmur-v1.md`.

## Build & Run

### .NET

```shell
dotnet build Murmur.sln
dotnet test Murmur.sln
dotnet run --project src/Murmur.UI/
```

Debug builds load the UI from the Vite dev server (`http://localhost:5223/`), so run `npm run dev` (or `scripts/dev.ps1`, which does both) first. Release builds load the bundled `Assets` folder.

### Frontend

```shell
cd src/WebApp
npm ci
npm run dev          # Vite dev server on localhost:5223
npm run build        # Production build
npm test             # Vitest (watch)
npm run test:run     # Vitest (single run)
```

### Skip frontend during .NET build

```shell
dotnet build -p:SkipFrontendBuild=true
```

### Microsoft Store package (MSIX)

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File build/build-msix.ps1   # -> installer-output\Murmur-<version>-x64.msix, unsigned
```

The version lives in `version.json` and `packaging/msix/AppxManifest.xml` (`1.0.0` and `1.0.0.0`), kept in step by hand; the script refuses to pack when they disagree. Submission steps, the identity placeholder and how to sideload the staged layout for a check: `docs/store-submission-runbook.md`. Package identity changes behaviour in two places: start with Windows (`PackagedStartupRegistration` over the manifest's startup task instead of the Run key, chosen in `AppModule.RegisterStartup`) and CUDA (`CudaRuntimeLoader`, because a packaged process does not search PATH for DLLs). Settings, history, the model and logs stay in `%LOCALAPPDATA%\Murmur` in both channels (measured: not redirected).

## Architecture

**Type:** Hybrid WPF + WebView2 + React

- WPF shell/backend with React UI in WebView2
- IPC: JS -> `ApiBridge` -> `ApiRegistry` -> MediatR handlers (one request + handler per call, registered in `App.xaml.cs`)
- Push events: C# -> `ApiEventAggregator` -> `PostWebMessageAsJson` -> React hooks (`useWebViewEvent`)
- Dev mode: Vite HMR on localhost:5223
- Production: virtual host mapping (`murmur.localassets.invalid`) to the local `Assets` folder
- Autofac `ContainerBuilder` via `AutofacBootstrapper` (no `IHost`); the theme is fixed to dark

Projects: `Murmur.UI` -> `Murmur.WebView`, `Murmur.Interop`, `Murmur.Core`; `Murmur.Interop` -> `Murmur.WebView`, `Murmur.Core`, `Murmur.Common`; `Murmur.Core` -> `Murmur.Common`.

**Reference template:** `C:\code\template-projects\desktop-development-template\wpf-webview-react-development-template.md`

## Key Conventions

- File-scoped namespaces
- Private fields: `_camelCase`, public members: PascalCase
- Test naming: `MethodName_Scenario_ExpectedResult`
- No logic in View code-behind - all in ViewModels
- Constructor injection only - no service locator
- `ConfigureAwait(false)` in Core/library code
- No `async void` except event handlers
- `DynamicResource` for all theme colors (never StaticResource or hardcoded hex)
- 4 spaces C#, 2 spaces XAML/XML/csproj
- `Log.ForContext<T>()` for logging; all JSON (IPC and files) goes through `JsonUtils`
- `TreatWarningsAsErrors` is on: builds must have zero warnings

## Testing

- xUnit v3 + NSubstitute + Shouldly (+ `Microsoft.Extensions.TimeProvider.Testing` in `Murmur.Core.Tests`)
- Run: `dotnet test Murmur.sln`
- Frontend: Vitest - run `npm run test:run` in `src/WebApp/`

## Environment

- .NET 10 SDK
- Windows 11 SDK (10.0.26100)
- Node.js 22+ LTS
- WebView2 runtime (usually pre-installed on Windows 10/11)
