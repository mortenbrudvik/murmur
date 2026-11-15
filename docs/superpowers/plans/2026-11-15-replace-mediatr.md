# Replace MediatR with Mediator; platform packages on 11.x — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace MediatR 12.5 (last free version) with the MIT-licensed, source-generated Mediator 3.x, and move the
three .NET platform packages to `11.*`, with no change in app behaviour.

**Architecture:** Mediator's source generator runs in `Murmur.UI` and generates `IMediator` plus `AddMediator`, which
registers into a Microsoft DI `ServiceCollection`; Autofac takes those registrations over with
`Autofac.Extensions.DependencyInjection`'s `Populate`, so Autofac stays the only container. Handlers stay transient.
Every handler moves from `Task` to `ValueTask`.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.26100.0`), WPF, Autofac 9, Mediator.Abstractions / Mediator.SourceGenerator
3.0.x, Autofac.Extensions.DependencyInjection 11.x, xUnit v3 (`xunit.v3.mtp-off`), NSubstitute 6, Shouldly 4.

**Spec:** `docs/superpowers/specs/2026-11-15-replace-mediatr-design.md`

**Worktree:** `C:\code\projects\murmur\.claude\worktrees\replace-mediatr`, branch `worktree-replace-mediatr`. Run every
command from the worktree root. Never `cd` to `C:\code\projects\murmur`.

## Global Constraints

- `TreatWarningsAsErrors` is on: every build must end with `0 Warning(s)` and `0 Error(s)`.
- Packages: `Mediator.Abstractions` `3.*`, `Mediator.SourceGenerator` `3.*` (stable only; 3.0.2 today),
  `Autofac.Extensions.DependencyInjection` `11.*`; `System.Windows.Extensions`, `System.Speech`,
  `Microsoft.Extensions.TimeProvider.Testing` `11.*`.
- No package with a commercial or reciprocal licence. No `MediatR*` package, direct or transitive, when done.
- Handlers are registered **Transient** (`options.ServiceLifetime = ServiceLifetime.Transient`), not Mediator's
  singleton default.
- Notifications use Mediator's default `ForeachAwaitPublisher` (in order, an exception propagates).
- Conventions (CLAUDE.md): file-scoped namespaces; `_camelCase` fields; `ConfigureAwait(false)` in Core/library code;
  test names `MethodName_Scenario_ExpectedResult`; all JSON through `JsonUtils`; 4 spaces in C#, 2 in csproj/XML.
- Commit messages end with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Build: `dotnet build Murmur.sln`. Test: `dotnet test Murmur.sln`. One test class:
  `dotnet test tests/<Project> --filter "FullyQualifiedName~<ClassName>"`.

## Review Focus

1. **A void IPC call** (`app.uiReady`, `app.openLink`, `setup.stopMicTest`...) must still answer
   `{"success":true,"data":{}}`. Pinned in Task 2 (`InvokeMethod_VoidRequest_ReturnsAnEmptyObjectAsData`).
2. **`ApiBridge` must return the handler's result, not a `ValueTask` wrapper:** `Task.Run(() => mediator.Send(r))`
   compiles with a `ValueTask` and silently serialises the wrong thing. Pinned in Task 2
   (`InvokeMethod_HandlerCompletesLater_ReturnsItsResultAsData`, `..._ReturnsTheHandlersResultAsData`).
3. **A handler that throws before its first await** must still produce `{success:false,error}`, not an exception out
   of `InvokeMethod`. Pinned in Task 2 (`InvokeMethod_SendThrowsSynchronously_ReturnsErrorWithExceptionMessage`).
4. **Handlers resolved per call** (two IPC calls at once get separate handler instances, as with MediatR's transient
   default). Pinned in Task 4 (`Bootstrap_ResolvesRequestHandlersPerCall`).
5. **A notification's handler resolved from the scope `IMediator` came from**, and both `Send` paths (typed and
   `object`) reaching handlers in Core through the real container. Pinned in Task 4 (`MediatorWiringTests`).

---

### Task 1: Platform packages on 11.x

**Files:**
- Modify: `src/Murmur.Core/Murmur.Core.csproj`
- Modify: `tests/Murmur.Core.Tests/Murmur.Core.Tests.csproj`
- Modify: `tests/Murmur.UI.Tests/Murmur.UI.Tests.csproj`
- Modify: `CLAUDE.md` (Testing section, "Held packages" line)

**Interfaces:** none.

- [ ] **Step 1: Change the versions**

In `src/Murmur.Core/Murmur.Core.csproj`:
```xml
    <PackageReference Include="System.Windows.Extensions" Version="11.*" />
```
In `tests/Murmur.Core.Tests/Murmur.Core.Tests.csproj`:
```xml
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="11.*" />
    <PackageReference Include="System.Speech" Version="11.*" />
```
In `tests/Murmur.UI.Tests/Murmur.UI.Tests.csproj`:
```xml
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="11.*" />
```
Keep each file's CRLF line endings (edit with the Edit tool, not `sed`).

- [ ] **Step 2: Build and test**

Run: `dotnet build Murmur.sln` then `dotnet test Murmur.sln`
Expected: `0 Warning(s)`, `0 Error(s)`; all three test assemblies `Passed!` (1,459 tests).

- [ ] **Step 3: Confirm the resolved versions**

Run: `dotnet list Murmur.sln package | grep -E "System.Windows.Extensions|System.Speech|TimeProvider"`
Expected: every row resolves `11.0.x`.

- [ ] **Step 4: Update CLAUDE.md**

In `CLAUDE.md`, replace the sentence
`The \`System.*\` and \`Microsoft.Extensions.*\` packages float on the .NET major the app targets (\`10.*\`)`
with
`The \`System.*\` and \`Microsoft.Extensions.*\` packages float on \`11.*\`: their 11.0 releases ship \`net10.0\` builds`

- [ ] **Step 5: Commit**

```bash
git add src/Murmur.Core/Murmur.Core.csproj tests/Murmur.Core.Tests/Murmur.Core.Tests.csproj tests/Murmur.UI.Tests/Murmur.UI.Tests.csproj CLAUDE.md
git commit -m "build: System.Windows.Extensions, System.Speech and TimeProvider.Testing on 11.x"
```

---

### Task 2: Pin what `ApiBridge` answers (characterisation tests, on MediatR)

These tests describe today's behaviour and must **pass on MediatR now**. Task 4 changes only their stub types; if
they fail after the swap, the swap is wrong.

**Files:**
- Modify: `tests/Murmur.Interop.Tests/ApiBridgeTests.cs`

**Interfaces:**
- Consumes: `ApiBridge(IMediator mediator, ApiRegistry apiRegistry)`, `Task<string> InvokeMethod(string serviceName,
  string methodName, string jsonParams)`; `ApiRegistry.Register<TRequest>(string, string)`.
- Produces: helper `private static JsonElement ShouldBeSuccess(string response)` and test-only request
  `private class FakeVoidRequest : IRequest { }`, both inside `ApiBridgeTests`; Task 4 edits these tests.

- [ ] **Step 1: Add the tests**

Add to `ApiBridgeTests` (keep `using MediatR;` for now):

```csharp
    [Fact]
    public async Task InvokeMethod_RegisteredRequest_ReturnsTheHandlersResultAsData()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<object?>("result"));
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", "{}");

        ShouldBeSuccess(result).GetString().ShouldBe("result");
    }

    // A handler that waits (CaptureHotkey waits up to 10 s): the bridge must answer with what it finally returns.
    [Fact]
    public async Task InvokeMethod_HandlerCompletesLater_ReturnsItsResultAsData()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(_ => LaterAsync());
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", "{}");

        ShouldBeSuccess(result).GetString().ShouldBe("later");

        static async Task<object?> LaterAsync()
        {
            await Task.Delay(20);
            return "later";
        }
    }

    // OpenLink, QuitApp, RestartApp, UiReady and StopMicTest answer nothing; the web app only reads success.
    [Fact]
    public async Task InvokeMethod_VoidRequest_ReturnsAnEmptyObjectAsData()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<object?>(Unit.Value));
        var registry = new ApiRegistry();
        registry.Register<FakeVoidRequest>("test", "do");
        var bridge = new ApiBridge(mediator, registry);

        var result = await bridge.InvokeMethod("test", "do", "{}");

        var data = ShouldBeSuccess(result);
        data.ValueKind.ShouldBe(JsonValueKind.Object);
        data.EnumerateObject().ShouldBeEmpty();
    }

    // A handler that throws before its first await throws out of Send itself instead of returning a faulted task.
    [Fact]
    public async Task InvokeMethod_SendThrowsSynchronously_ReturnsErrorWithExceptionMessage()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns<Task<object?>>(_ => throw new InvalidOperationException("thrown at once"));
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", "{}");

        ShouldBeError(result).ShouldBe("thrown at once");
    }
```

And next to `ShouldBeError` and `FakeRequest`:

```csharp
    /// <summary>Asserts the bridge's success shape, <c>{"success":true,"data":...}</c>, and returns <c>data</c>.</summary>
    private static JsonElement ShouldBeSuccess(string response)
    {
        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        root.GetProperty("success").GetBoolean().ShouldBeTrue();
        return root.GetProperty("data").Clone();
    }

    private class FakeVoidRequest : IRequest { }
```

- [ ] **Step 2: Run them on MediatR**

Run: `dotnet test tests/Murmur.Interop.Tests --filter "FullyQualifiedName~ApiBridgeTests"`
Expected: all `ApiBridgeTests` pass (10 tests). If `InvokeMethod_VoidRequest_...` fails, read what `data` is and fix
the test to today's actual shape before going on: the test pins today's behaviour.

- [ ] **Step 3: Commit**

```bash
git add tests/Murmur.Interop.Tests/ApiBridgeTests.cs
git commit -m "test(interop): pin what ApiBridge answers: the result, {} for void requests, errors thrown at once"
```

---

### Task 3: `FireAndForget` for `ValueTask`

Mediator's `Publish` returns `ValueTask`; four call sites do `mediator.Publish(...).FireAndForget()`. An overload keeps
them unchanged. `Murmur.Common` must not reference Mediator (`ValueTask` is a BCL type).

**Files:**
- Modify: `src/Murmur.Common/AsyncUtils.cs`
- Test: `tests/Murmur.Core.Tests/Common/AsyncUtilsTests.cs`

**Interfaces:**
- Consumes: existing `internal static Task LogFault(this Task t, ILogger? logger)`.
- Produces: `public static void FireAndForget(this ValueTask t)` and
  `internal static Task LogFault(this ValueTask t, ILogger? logger)` in `Murmur.Common.AsyncUtils`.

- [ ] **Step 1: Write the failing tests**

Add to `AsyncUtilsTests`:

```csharp
    [Fact]
    public async Task LogFault_ValueTaskFaults_LogsTheExceptionOnce()
    {
        var fault = new InvalidOperationException("test fault");

        await ValueTask.FromException(fault).LogFault(_logger);

        var logged = _sink.Events.ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogEventLevel.Error);
        logged.Exception.ShouldBeOfType<AggregateException>().InnerException.ShouldBeSameAs(fault);
    }

    [Fact]
    public async Task LogFault_ValueTaskSucceeds_LogsNothing()
    {
        await ValueTask.CompletedTask.LogFault(_logger);

        _sink.Events.ShouldBeEmpty();
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Murmur.Core.Tests --filter "FullyQualifiedName~AsyncUtilsTests"`
Expected: build error CS1929 (`ValueTask` does not contain a definition for `LogFault`).

- [ ] **Step 3: Implement**

In `src/Murmur.Common/AsyncUtils.cs`, after `FireAndForget(this Task t)`:

```csharp
    /// <summary>
    /// As <see cref="FireAndForget(Task)"/>, for a <see cref="ValueTask"/> (what Mediator's Publish returns).
    /// </summary>
    public static void FireAndForget(this ValueTask t) => _ = t.LogFault(logger: null);
```

and after `LogFault(this Task t, ...)`:

```csharp
    /// <summary>As <see cref="LogFault(Task, ILogger?)"/>, for a <see cref="ValueTask"/>.</summary>
    internal static Task LogFault(this ValueTask t, ILogger? logger) => t.AsTask().LogFault(logger);
```

Fix the existing `<see cref="FireAndForget"/>` in `LogFault`'s summary to `<see cref="FireAndForget(Task)"/>` if the
build reports it ambiguous (CS0419 is a warning, so an error here).

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Murmur.Core.Tests --filter "FullyQualifiedName~AsyncUtilsTests"`
Expected: PASS. Then `dotnet build Murmur.sln`: `0 Warning(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/Murmur.Common/AsyncUtils.cs tests/Murmur.Core.Tests/Common/AsyncUtilsTests.cs
git commit -m "feat(common): FireAndForget for ValueTask"
```

---

### Task 4: Swap MediatR for Mediator

One task because the solution does not compile half-migrated. Order: new failing wiring test, packages, wiring,
production code, tests, docs.

**Files:**
- Create: `tests/Murmur.UI.Tests/MediatorWiringTests.cs`
- Modify (packages): `src/Murmur.Core/Murmur.Core.csproj`, `src/Murmur.Interop/Murmur.Interop.csproj`,
  `src/Murmur.UI/Murmur.UI.csproj`
- Modify (wiring): `src/Murmur.UI/AutofacBootstrapper.cs`
- Modify (bridge): `src/Murmur.Interop/ApiBridge.cs`
- Modify (`using MediatR;` → `using Mediator;` and handler signatures): every file listed in Step 5
- Modify (tests): files listed in Step 7
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: Task 3's `FireAndForget(this ValueTask)`; Task 2's tests.
- Produces: `IMediator` resolvable from `AutofacBootstrapper.Bootstrap()`; handler interfaces registered as
  `IRequestHandler<TRequest, TResponse>` (void requests: `IRequestHandler<TRequest, Unit>`) and
  `INotificationHandler<TNotification>`.

Mediator facts used below (checked in the v3.0.2 source): `ISender.Send(object, CancellationToken)` returns
`ValueTask<object?>`; `IRequest` is `IRequest<Unit>` and its handler returns `ValueTask<Unit>`; `Unit.Value` and
`Unit.ValueTask` exist; `INotificationHandler<T>.Handle` returns `ValueTask`; `MediatorOptions.Assemblies` accepts
`[typeof(...)]`; `ServiceCollection` and `ServiceLifetime` live in `Microsoft.Extensions.DependencyInjection` (the
Abstractions package, which comes in transitively).

- [ ] **Step 1: Write the wiring test (fails: no Mediator yet)**

Create `tests/Murmur.UI.Tests/MediatorWiringTests.cs`:

```csharp
using Autofac;
using Mediator;
using Murmur.Core.Features.App;
using Murmur.Core.Input;
using Murmur.Interop;
using Murmur.WebView;
using NSubstitute;
using Shouldly;

namespace Murmur.UI.Tests;

/// <summary>
/// The generated mediator, reached through the real container: the source generator must have found the handlers in
/// Murmur.Core and Murmur.Interop, and Autofac must build them with their dependencies.
/// </summary>
public sealed class MediatorWiringTests
{
    // ApiBridge sends the request it deserialised as an object.
    [Fact]
    public async Task Bootstrap_SendObject_ReachesTheHandlerInCore()
    {
        using var container = AutofacBootstrapper.Bootstrap();
        var mediator = container.Resolve<IMediator>();

        var answer = await mediator.Send((object)new GetInputStatusRequest(), TestContext.Current.CancellationToken);

        answer.ShouldBeSameAs(container.Resolve<InputStatus>());
    }

    // The About window sends a typed request.
    [Fact]
    public async Task Bootstrap_SendTyped_ReachesTheHandlerInCore()
    {
        using var container = AutofacBootstrapper.Bootstrap();
        var mediator = container.Resolve<IMediator>();

        var answer = await mediator.Send(new GetInputStatusRequest(), TestContext.Current.CancellationToken);

        answer.ShouldBeSameAs(container.Resolve<InputStatus>());
    }

    // The handler lives in Murmur.Interop and is resolved from the scope the mediator came from.
    [Fact]
    public async Task Bootstrap_Publish_ReachesTheHandlerInInterop()
    {
        using var container = AutofacBootstrapper.Bootstrap();
        var events = Substitute.For<ApiEventAggregator>(Substitute.For<IWebViewInterop>());
        await using var scope = container.BeginLifetimeScope(b => b.RegisterInstance(events).As<ApiEventAggregator>());

        await scope.Resolve<IMediator>().Publish(new ShowSettingsNotification(), TestContext.Current.CancellationToken);

        events.Received(1).Publish("app.showSettings", Arg.Any<object>());
    }

    // As with MediatR: one handler instance per call, so two IPC calls at once never share one.
    [Fact]
    public void Bootstrap_ResolvesRequestHandlersPerCall()
    {
        using var container = AutofacBootstrapper.Bootstrap();

        container.Resolve<IRequestHandler<GetInputStatusRequest, InputStatus>>()
            .ShouldNotBeSameAs(container.Resolve<IRequestHandler<GetInputStatusRequest, InputStatus>>());
    }
}
```

`GetInputStatusRequest` and `ShowSettingsNotification` are in `Murmur.Core.Features.App`; `InputStatus` is in
`Murmur.Core.Input`; `IWebViewInterop` is in `Murmur.WebView`; `ApiEventAggregator(IWebViewInterop)` has a `virtual
Publish(string eventName, object data)`, so NSubstitute can stand in for it.

Run: `dotnet build tests/Murmur.UI.Tests -p:SkipFrontendBuild=true`
Expected: FAIL, CS0246 `The type or namespace name 'Mediator' could not be found`.

- [ ] **Step 2: Packages**

`src/Murmur.Core/Murmur.Core.csproj`: replace `<PackageReference Include="MediatR" Version="12.*" />` with
```xml
    <PackageReference Include="Mediator.Abstractions" Version="3.*" />
```
`src/Murmur.Interop/Murmur.Interop.csproj`: the same replacement.

`src/Murmur.UI/Murmur.UI.csproj`: remove `MediatR` and `MediatR.Extensions.Autofac.DependencyInjection`; add
```xml
    <PackageReference Include="Autofac.Extensions.DependencyInjection" Version="11.*" />
    <PackageReference Include="Mediator.Abstractions" Version="3.*" />
    <!-- Generates IMediator and AddMediator at build time; it ships nothing at run time. It belongs only here, in the
         outermost project: it scans Murmur.Core and Murmur.Interop for messages and handlers. -->
    <PackageReference Include="Mediator.SourceGenerator" Version="3.*">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
```
Keep CRLF line endings and 2-space indentation.

- [ ] **Step 3: Wiring**

Replace the body of `src/Murmur.UI/AutofacBootstrapper.cs`:

```csharp
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Murmur.Core.Services;
using Murmur.Interop;

namespace Murmur.UI;

public class AutofacBootstrapper
{
    public static IContainer Bootstrap()
    {
        // Mediator's generated AddMediator only speaks Microsoft DI; Autofac takes the registrations over (Populate), so
        // handlers are still built by Autofac with everything AppModule registers.
        var services = new ServiceCollection();
        services.AddMediator((MediatorOptions options) =>
        {
            // Per call, as MediatR registered them; Mediator's own default is singleton.
            options.ServiceLifetime = ServiceLifetime.Transient;
            // Every assembly that holds requests, notifications or handlers. Must be compile-time constants.
            options.Assemblies = [typeof(AppModule), typeof(ApiBridge), typeof(MurmurPaths)];
        });

        var builder = new ContainerBuilder();
        builder.Populate(services);
        builder.RegisterModule<AppModule>();

        return builder.Build();
    }
}
```

- [ ] **Step 4: `ApiBridge`**

In `src/Murmur.Interop/ApiBridge.cs`, change `using MediatR;` to `using Mediator;` and the dispatch line to:

```csharp
            // AsTask: Task.Run has no ValueTask overload, so without it the await would yield the ValueTask itself and
            // the web app would get that, not the response.
            var result = await Task.Run(() => mediator.Send(request).AsTask()).ConfigureAwait(false);
```

`src/Murmur.Interop/ApiRegistry.cs`: `using MediatR;` → `using Mediator;` (`IBaseRequest` has the same name).

- [ ] **Step 5: Production code**

In every file below, `using MediatR;` → `using Mediator;`, then change `Handle` as shown.

Request handlers with a synchronous result: `Task<T>` → `ValueTask<T>`, `Task.FromResult(` → `ValueTask.FromResult(`:
- `src/Murmur.Core/Features/App/GetInputStatusRequest.cs`
- `src/Murmur.Core/Features/Engine/GetEngineStatusRequest.cs`
- `src/Murmur.Core/Features/History/ClearHistoryRequest.cs`
- `src/Murmur.Core/Features/History/CopyHistoryEntryRequest.cs` (expression body)
- `src/Murmur.Core/Features/History/DeleteHistoryEntryRequest.cs`
- `src/Murmur.Core/Features/History/GetHistoryRequest.cs`
- `src/Murmur.Core/Features/Models/GetModelChoiceRequest.cs` (expression body)
- `src/Murmur.Core/Features/Settings/GetMicrophonesRequest.cs`
- `src/Murmur.Core/Features/Settings/GetSettingsRequest.cs`
- `src/Murmur.Core/Features/Setup/MicTestRequests.cs` (`StartMicTestHandler`, expression body)
- `src/Murmur.Core/Features/Setup/UpdateSetupStateRequest.cs` (expression body)
- `src/Murmur.Core/Features/Vocabulary/GetVocabularyRequest.cs`
- `src/Murmur.Core/Features/Vocabulary/UpdateVocabularyRequest.cs`

```csharp
// before
public Task<EngineStatus> Handle(GetEngineStatusRequest request, CancellationToken ct)
{
    return Task.FromResult(engine.Status);
}
// after
public ValueTask<EngineStatus> Handle(GetEngineStatusRequest request, CancellationToken ct)
{
    return ValueTask.FromResult(engine.Status);
}
```

Async request handlers: `async Task<T>` → `async ValueTask<T>`, body unchanged:
- `src/Murmur.Core/Features/Models/SetModelChoiceRequest.cs`
- `src/Murmur.Core/Features/Settings/UpdateSettingsRequest.cs`

Handlers that return another method's `Task`: wrap it, so the call still runs synchronously up to its first wait (the
hotkey capture is armed before `Handle` returns; `SettingsHandlerTests.CaptureHotkey_*` rely on that):
- `src/Murmur.Core/Features/Settings/CaptureHotkeyRequest.cs`:
  ```csharp
  public ValueTask<HotkeyDefinition?> Handle(CaptureHotkeyRequest request, CancellationToken ct)
  {
      // ...existing lines unchanged...
      return new ValueTask<HotkeyDefinition?>(coordinator.CaptureAsync(Timeout, ct));
  }
  ```
- `src/Murmur.Core/Features/Setup/GetSetupStateRequest.cs`:
  ```csharp
  public ValueTask<SetupState> Handle(GetSetupStateRequest request, CancellationToken ct) =>
      new(coordinator.GetStateAsync());
  ```

Void request handlers (`IRequest`): `Task` → `ValueTask<Unit>`, `return Task.CompletedTask;` → `return Unit.ValueTask;`:
- `src/Murmur.Core/Features/App/OpenLinkRequest.cs`
- `src/Murmur.Core/Features/App/QuitAppRequest.cs`
- `src/Murmur.Core/Features/App/RestartAppRequest.cs`
- `src/Murmur.Core/Features/App/UiReadyRequest.cs`
- `src/Murmur.Core/Features/Setup/MicTestRequests.cs` (`StopMicTestHandler`)

```csharp
// after
public ValueTask<Unit> Handle(UiReadyRequest request, CancellationToken ct)
{
    ui.MarkReady();
    return Unit.ValueTask;
}
```

Notification handlers: `Task` → `ValueTask`, `return Task.CompletedTask;` → `return ValueTask.CompletedTask;`:
- `src/Murmur.Core/Features/Settings/SettingsUpdatedHandler.cs`
- `src/Murmur.Interop/Handlers/EngineStatusEventHandler.cs`
- `src/Murmur.Interop/Handlers/HistoryAddedEventHandler.cs`
- `src/Murmur.Interop/Handlers/InputStatusEventHandler.cs`
- `src/Murmur.Interop/Handlers/MicLevelEventHandler.cs`
- `src/Murmur.Interop/Handlers/ShowSettingsEventHandler.cs`

Usings only (no other change: `await ...Publish(...)`, `.ConfigureAwait(false)` and `.FireAndForget()` work on
`ValueTask` after Task 3):
- `src/Murmur.Core/Audio/MicrophoneLevelMonitor.cs`
- `src/Murmur.Core/Dictation/DictationDelivery.cs`
- `src/Murmur.Core/Setup/UiLauncher.cs`
- `src/Murmur.Core/Features/App/InputStatusChangedNotification.cs`
- `src/Murmur.Core/Features/App/ShowSettingsNotification.cs`
- `src/Murmur.Core/Features/Engine/EngineStatusChangedNotification.cs`
- `src/Murmur.Core/Features/History/HistoryEntryAddedNotification.cs`
- `src/Murmur.UI/About/AboutViewModel.cs`
- `src/Murmur.UI/App.xaml.cs`
- `src/Murmur.UI/AppModule.cs`

Check none is left: `grep -rn "MediatR" src --include=*.cs --include=*.csproj | grep -v /obj/` prints nothing.

- [ ] **Step 6: Build the production projects**

Run: `dotnet build src/Murmur.UI -p:SkipFrontendBuild=true`
Expected: `0 Warning(s)`, `0 Error(s)`. A `MediatorGenerator` warning (MSG…) means the generator found a message with no
handler or two handlers: fix the message, do not suppress the warning.

- [ ] **Step 7: Tests**

`using MediatR;` → `using Mediator;` in:
`tests/Murmur.Core.Tests/Audio/MicrophoneLevelMonitorTests.cs`, `tests/Murmur.Core.Tests/Dictation/DictationControllerTests.cs`,
`tests/Murmur.Core.Tests/Dictation/DictationDeliveryTests.cs`, `tests/Murmur.Core.Tests/Features/SettingsHandlerTests.cs`,
`tests/Murmur.Core.Tests/Setup/UiLauncherTests.cs`, `tests/Murmur.Interop.Tests/ApiBridgeTests.cs`,
`tests/Murmur.Interop.Tests/ApiRegistryTests.cs`, `tests/Murmur.UI.Tests/About/AboutViewModelTests.cs`,
`tests/Murmur.UI.Tests/About/AboutWindowTests.cs`, `tests/Murmur.UI.Tests/AppModuleTests.cs`,
`tests/Murmur.UI.Tests/MainViewModelTests.cs`.

Stubs that return a `Task` where Mediator returns a `ValueTask`:
- `tests/Murmur.Interop.Tests/ApiBridgeTests.cs`: every `.Returns(Task.FromResult<object?>(x))` →
  `.Returns(ValueTask.FromResult<object?>(x))`; `Task.FromException<object?>(e)` → `ValueTask.FromException<object?>(e)`;
  `.Returns(_ => LaterAsync())` → `.Returns(_ => new ValueTask<object?>(LaterAsync()))`;
  `.Returns<Task<object?>>(_ => throw ...)` → `.Returns<ValueTask<object?>>(_ => throw ...)`. `Unit.Value` now
  means `Mediator.Unit`. Rename `InvokeMethod_DispatchesToMediatR_WhenRegistered` to
  `InvokeMethod_DispatchesToMediator_WhenRegistered`.
- `tests/Murmur.Core.Tests/Dictation/DictationControllerTests.cs`, `PendingPublish`: `return pending.Task;` →
  `return new ValueTask(pending.Task);`.
- `tests/Murmur.UI.Tests/About/AboutViewModelTests.cs` (two places): `.Returns(Task.FromException(new
  InvalidOperationException("no browser")), Task.CompletedTask)` →
  `.Returns(ValueTask.FromException<Unit>(new InvalidOperationException("no browser")), Unit.ValueTask)`, and the
  single-argument one → `.Returns(ValueTask.FromException<Unit>(new InvalidOperationException("no browser")))`.

APIs that take only a `Task` (`Should.ThrowAsync`, `WaitAsync`): append `.AsTask()` to the handler call:
- `tests/Murmur.Core.Tests/Features/AppHandlerTests.cs` lines ~61, ~74, ~84
- `tests/Murmur.Core.Tests/Features/ModelChoiceHandlerTests.cs` lines ~73 (`.Handle(...).AsTask().WaitAsync(...)`), ~87
- `tests/Murmur.Core.Tests/Features/SettingsHandlerTests.cs` line ~179
- `tests/Murmur.Core.Tests/Features/VocabularyHandlerTests.cs` line ~110

```csharp
// before
await Should.ThrowAsync<InvalidOperationException>(
    () => new RestartAppHandler(lifecycle).Handle(new RestartAppRequest(), CancellationToken.None));
// after
await Should.ThrowAsync<InvalidOperationException>(
    () => new RestartAppHandler(lifecycle).Handle(new RestartAppRequest(), CancellationToken.None).AsTask());
```

Void handlers are registered as `IRequestHandler<TRequest, Unit>`. In
`tests/Murmur.UI.Tests/AppModuleTests.cs`, `Bootstrap_ResolvesTheSetupModelChoiceAndAppHandlers`:
`IRequestHandler<OpenLinkRequest>` → `IRequestHandler<OpenLinkRequest, Unit>`, and the same for `RestartAppRequest`,
`QuitAppRequest`, `UiReadyRequest`, `StopMicTestRequest`.

The compiler lists anything missed: build until clean.

- [ ] **Step 8: Run everything**

Run: `dotnet build Murmur.sln` then `dotnet test Murmur.sln`
Expected: `0 Warning(s)`; all `Passed!`, 1,469 tests: 1,459 existing + 4 from Task 2 + 2 from Task 3 + 4
`MediatorWiringTests`.
Task 2's tests pass unchanged apart from Step 7's stub types.

- [ ] **Step 9: No MediatR left, anywhere**

Run: `dotnet list Murmur.sln package --include-transitive | grep -i mediatr`
Expected: no output. And `grep -rn "MediatR" src tests --include=*.cs --include=*.csproj | grep -v /obj/`: no output.

- [ ] **Step 10: CLAUDE.md**

- Architecture: `IPC: JS -> \`ApiBridge\` -> \`ApiRegistry\` -> MediatR handlers (one request + handler per call, registered in \`App.xaml.cs\`)`
  → `IPC: JS -> \`ApiBridge\` -> \`ApiRegistry\` -> Mediator handlers (source-generated \`IMediator\`; one request + handler per call, registered with \`ApiRegistry\` in \`App.xaml.cs\`)`
- Architecture: `Autofac \`ContainerBuilder\` via \`AutofacBootstrapper\` (no \`IHost\`); the theme is fixed to dark`
  → `Autofac \`ContainerBuilder\` via \`AutofacBootstrapper\` (no \`IHost\`); Mediator's generated registrations come in through \`Autofac.Extensions.DependencyInjection\` (\`Populate\`), handlers transient; the theme is fixed to dark`
- Testing: replace the whole "Held packages" bullet with:
  `- No commercial packages: MediatR was replaced by Mediator (MIT) because MediatR 13 and later are RPL-1.5 or commercial. The \`System.*\` and \`Microsoft.Extensions.*\` packages float on \`11.*\`: their 11.0 releases ship \`net10.0\` builds`

- [ ] **Step 11: Commit**

```bash
git add -A
git status --short   # only src/, tests/, CLAUDE.md
git commit -m "refactor: Mediator (MIT, source-generated) instead of MediatR"
```
Message body: why (MediatR 13+ RPL-1.5 or commercial), the Autofac `Populate` bridge, transient handlers, the
`ApiBridge` `.AsTask()` pitfall, `IRequestHandler<T, Unit>` for void requests.

---

### Task 5: Verify licences, versions and the running app

**Files:** none changed (unless a check fails).

**Interfaces:** none.

- [ ] **Step 1: Nothing outdated**

Run: `dotnet list Murmur.sln package --outdated`
Expected: every project reports no updates.

- [ ] **Step 2: Every licence is permissive**

For every package `dotnet list Murmur.sln package --include-transitive` lists, read
`~/.nuget/packages/<id>/<version>/<id>.nuspec`: the `<license>` must be MIT, Apache-2.0, BSD-2/3-Clause, MS-PL, or a
file whose text is one of these or a free Microsoft licence (WebView2, Windows SDK BuildTools). For `type="file"`,
read the LICENSE file in the package folder. Expected: `Mediator.Abstractions` and `Mediator.SourceGenerator` → MIT
(Martin Othamar); `Autofac.Extensions.DependencyInjection` → MIT. Anything else: stop and report.

- [ ] **Step 3: Release build and package**

Run: `pwsh -File build/build-msix.ps1` (`C:\Program Files\PowerShell\7\pwsh.exe`)
Expected: `Package creation succeeded`, and no `MediatR*.dll` in `packaging/msix/staging`
(`ls packaging/msix/staging | grep -i mediat` shows only `Mediator.dll` if anything).

- [ ] **Step 4: The running app (needs the owner)**

Murmur allows one instance (`Local\Murmur.SingleInstance`): ask the owner to quit their running Murmur first. Then run
`scripts/dev.ps1` from the worktree. In `%LOCALAPPDATA%\Murmur\Logs\` (today's file), check after the window shows:
- `[IPC ->] app.uiReady` followed by `[IPC <-] app.uiReady -> {"success":true,"data":{}}`;
- `[IPC <-]` lines with `"success":true` and real data for the calls the page makes at load (settings, engine status,
  history);
- `[IPC v] Event: engine.statusChanged` lines;
- no `API call failed` and no `FireAndForget task faulted`.
Then ask the owner to: open Settings and change one setting (an IPC call plus `SettingsUpdated`), open History, and
hold the hotkey and dictate one sentence (history entry pushed as `history.added`). Quit the dev instance afterwards.

- [ ] **Step 5: Report**

Report the outcome of steps 1–4 to the owner. Nothing to commit unless a check required a fix.
