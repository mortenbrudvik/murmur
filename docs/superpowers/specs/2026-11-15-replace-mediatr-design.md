# Murmur — Replace MediatR with Mediator; platform packages on 11.x (design)

**Date:** 2026-11-15
**Status:** Approved in conversation 2026-11-15; this written spec awaits the owner's review
**Builds on:** `2026-10-29-murmur-design.md` (the IPC architecture), commit `9db4b87` (the last package update)

---

## 1. Intent and why

The owner wants every NuGet package current and none of them commercial. Two groups were held back in `9db4b87`:

- **MediatR** stays on 12.5.0. That version is Apache-2.0, but it is the last free one: 13.0 and later are dual
  licensed RPL-1.5 (reciprocal, at odds with Murmur's MIT licence) or commercial. It will never be updated again.
- **`System.Windows.Extensions`, `System.Speech` and `Microsoft.Extensions.TimeProvider.Testing`** float on `10.*`,
  the .NET major the app targets. Their 11.0.0 releases contain real `net10.0` builds.

The owner chose to replace MediatR with **Mediator** (`martinothamar/Mediator`, MIT, source-generated) and to move the
three platform packages to `11.*`.

**Success:** no MediatR package anywhere (direct or transitive); `dotnet list package --outdated` lists nothing; every
package's licence is MIT, Apache-2.0, BSD or a free Microsoft licence; the app behaves exactly as before: every IPC
call answers the same JSON, every push event arrives, nothing new is logged.

## 2. What exists today

- **IPC:** JS → `ApiBridge.InvokeMethod` → `ApiRegistry.GetRequestType` (a `Type` keyed by `service.method`, constrained
  to MediatR's `IBaseRequest`) → `JsonUtils.Deserialize` → `await Task.Run(() => mediator.Send(request))` (the untyped
  `Send(object)`) → `{ success: true, data: result }`.
- **Messages:** 22 request types, each with one `IRequestHandler`, all in `Murmur.Core/Features`. Five return nothing
  (`IRequest`): `OpenLink`, `QuitApp`, `RestartApp`, `UiReady`, `StopMicTest`.
- **Notifications:** 6 types, each with exactly one handler: `SettingsUpdated` (Core) and five in
  `Murmur.Interop/Handlers` that forward to `ApiEventAggregator` (`engine.statusChanged`, `history.added`,
  `app.inputStatusChanged`, `setup.micLevel`, `app.showSettings`).
- **Publishing:** `await` in `DictationDelivery` and `UpdateSettingsHandler`; fire-and-forget through
  `Murmur.Common.AsyncUtils.FireAndForget(this Task)` in `MicrophoneLevelMonitor`, `UiLauncher` and `App.xaml.cs`.
- **Only `Send` and `Publish` are used.** No pipeline behaviours, pre/post processors, exception handlers or streams.
- **Container:** Autofac `ContainerBuilder` in `AutofacBootstrapper`, no `IHost` and no Microsoft DI.
  `MediatR.Extensions.Autofac.DependencyInjection` scans `Murmur.UI`, `Murmur.Interop` and `Murmur.Core` and registers
  handlers transient (its default `RegistrationScope.Transient`, i.e. `InstancePerDependency`).
- **Tests:** 11 test files substitute `IMediator` with NSubstitute (79 lines mention it), mostly
  `Received().Publish(...)`; `ApiBridgeTests` stubs `Send(object)`.

## 3. Mediator facts this design relies on (checked in the v3.0.2 source)

- Packages: `Mediator.Abstractions` (interfaces) wherever messages, handlers or `IMediator` are used;
  `Mediator.SourceGenerator` in the outermost project only, which generates `IMediator` and `AddMediator`. Latest
  stable is 3.0.2; `3.*` does not pick up the 3.1 previews.
- `AddMediator` registers into an `IServiceCollection` (Microsoft DI). The README: it "only works with DI containers
  that integrate with those abstractions".
- `options.Assemblies` names the assemblies to scan; `options.ServiceLifetime` defaults to **Singleton**.
- `ISender.Send(object message, CancellationToken)` returns `ValueTask<object?>`.
- `IRequest` (no response) means `IRequestHandler<TRequest>` returns `ValueTask<Unit>`. `Unit` is a struct with no
  instance properties, so it serialises as `{}`, as MediatR's did.
- `INotificationHandler<T>.Handle` returns `ValueTask`. The default `ForeachAwaitPublisher` runs handlers one after
  another, but it and the generated `Send`/`Publish` are not `async`: a handler that throws before its first await
  throws out of `Send`/`Publish`, where MediatR faulted the returned task. (Found in the final review; see 4.4.)
- `IBaseRequest` exists under the same name.
- Generator diagnostics that are warnings (errors here, `TreatWarningsAsErrors`): multiple handlers for a message, an
  invalid handler type, a message without a handler. None applies today: every message has exactly one handler.

## 4. Design

### 4.1 Packages

| Project | Remove | Add or change |
|---|---|---|
| `Murmur.Core` | `MediatR` 12.* | `Mediator.Abstractions` 3.*; `System.Windows.Extensions` 11.* |
| `Murmur.Interop` | `MediatR` 12.* | `Mediator.Abstractions` 3.* |
| `Murmur.UI` | `MediatR` 12.*, `MediatR.Extensions.Autofac.DependencyInjection` 12.* | `Mediator.SourceGenerator` 3.* (analyzer only, `PrivateAssets="all"`), `Autofac.Extensions.DependencyInjection` (MIT) |
| `Murmur.Core.Tests` | | `System.Speech` 11.*, `Microsoft.Extensions.TimeProvider.Testing` 11.* |
| `Murmur.UI.Tests` | | `Microsoft.Extensions.TimeProvider.Testing` 11.* |

### 4.2 Wiring

`AutofacBootstrapper.Bootstrap`:

```csharp
var services = new ServiceCollection();
services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Transient;
    options.Assemblies = [typeof(AppModule), typeof(ApiBridge), typeof(MurmurPaths)];
    options.NotificationPublisherType = typeof(AwaitingNotificationPublisher);
    options.PipelineBehaviors = [typeof(AwaitingRequestBehavior<,>)];
});

var builder = new ContainerBuilder();
builder.Populate(services);
builder.RegisterModule<AppModule>();
return builder.Build();
```

- **One container.** Autofac takes over Mediator's registrations through `Populate`; handlers keep resolving their
  dependencies from Autofac, and nothing else moves to Microsoft DI.
- **Transient**, not Mediator's singleton default: today's handlers are per-dependency, and keeping that lifetime keeps
  behaviour without auditing what each handler's dependencies are registered as. It is not a hot path (IPC calls are
  user-paced; the busiest notification is the microphone level during the setup test).
- **`Murmur.UI/Messaging`** keeps MediatR's failure shape: `AwaitingNotificationPublisher` awaits each handler in
  order and `AwaitingRequestBehavior<,>` awaits each request's handler, both in `async` methods, so a handler that
  throws faults the returned task instead of throwing into the caller (4.4).
- The generated mediator lives in `Murmur.UI` (namespace `Mediator`). `Murmur.Core` and `Murmur.Interop` see only the
  `IMediator` interface from `Mediator.Abstractions`.

### 4.3 Code changes

- `using MediatR;` → `using Mediator;` throughout `src` and `tests`.
- Request handlers: `Task<T> Handle(TRequest, CancellationToken)` → `ValueTask<T>`. The five void handlers return
  `ValueTask<Unit>` and end with `return Unit.Value;`.
- Notification handlers: `Task Handle(...)` → `ValueTask Handle(...)`.
- **`ApiBridge`:** `await Task.Run(() => mediator.Send(request).AsTask())`. Without `.AsTask()`, `Task.Run` binds to
  `Task.Run<TResult>(Func<TResult>)` with `TResult = ValueTask<object?>`, and the await yields the unawaited
  `ValueTask` instead of the response: every IPC call would answer a wrong `data` silently. A test pins this (4.5).
- **`AsyncUtils`:** add `FireAndForget(this ValueTask)` (forwards to the `Task` overload via `AsTask()`). `ValueTask`
  is a BCL type, so `Murmur.Common` stays free of Mediator. Call sites are unchanged.
- `await mediator.Publish(...).ConfigureAwait(false)` works on `ValueTask` unchanged.

### 4.4 Error handling

Unchanged, given the two `Murmur.UI/Messaging` types: a handler's exception faults the task `Send` returns, which
`ApiBridge`'s `catch` logs and answers as `{ success: false, error }`; a failing notification handler faults the
`ValueTask` `Publish` returns, which `FireAndForget` logs or the awaiting caller sees, and never throws into the
caller (at `App`'s engine `StatusChanged` that would skip the later subscribers). Without them Mediator lets a
synchronous throw escape both; `MediatorWiringTests` pins the faulted-task shape for both. Mediator's `MissingMessageHandlerException` cannot happen for IPC: `ApiRegistry` only maps
request types that have handlers, and the generator would have failed the build otherwise.

### 4.5 Testing

- The existing 1,459 tests are the safety net. Test changes are mechanical: usings, and substitutes returning
  `ValueTask`/`ValueTask<object?>` instead of `Task`.
- **New test (Murmur.UI.Tests), end to end through the real container:** `AutofacBootstrapper.Bootstrap()` resolves
  `IMediator`; sending a real request returns its handler's result; publishing a real notification reaches its handler
  (observed through `ApiEventAggregator`). This proves the generator found handlers in all three assemblies and that
  the Autofac/Microsoft DI bridge resolves them.
- **New test (Murmur.Interop.Tests):** `ApiBridge.InvokeMethod` returns the handler's response in `data` when
  `Send(object)` completes asynchronously, and `{}` for a void request: pins the `Task.Run` pitfall in 4.3.
- **By hand:** run the app (`scripts/dev.ps1`), open Settings and change a setting (IPC + `SettingsUpdated`), open
  History (a request with data), and check the engine status and microphone level push events arrive.

### 4.6 Documentation

- `CLAUDE.md`: the architecture line says "Mediator handlers (source-generated, registered through
  `Autofac.Extensions.DependencyInjection`)"; drop the "Held packages" note; say the `System.*` and
  `Microsoft.Extensions.*` packages float on `11.*`.
- Earlier specs and plans are history and stay as written.

## 5. Out of scope

- Moving the app to .NET 11 (`net11.0`); .NET 10 is the LTS the app ships on.
- Singleton handlers, pipeline behaviours, or any other Mediator feature beyond what MediatR did here.
- npm packages.
