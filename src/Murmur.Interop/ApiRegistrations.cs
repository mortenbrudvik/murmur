using Murmur.Core.Features.App;
using Murmur.Core.Features.Engine;
using Murmur.Core.Features.History;
using Murmur.Core.Features.Models;
using Murmur.Core.Features.Settings;
using Murmur.Core.Features.Setup;
using Murmur.Core.Features.Vocabulary;
using static Murmur.Interop.IpcNames.Services;

namespace Murmur.Interop;

/// <summary>Every call the page can make, by service and method name, and the request it becomes.</summary>
public static class ApiRegistrations
{
    public static void RegisterAll(ApiRegistry registry)
    {
        registry.Register<GetHistoryRequest>(History, "get");
        registry.Register<DeleteHistoryEntryRequest>(History, "delete");
        registry.Register<ClearHistoryRequest>(History, "clear");
        registry.Register<CopyHistoryEntryRequest>(History, "copy");
        registry.Register<GetVocabularyRequest>(Vocabulary, "get");
        registry.Register<UpdateVocabularyRequest>(Vocabulary, "update");
        registry.Register<GetSettingsRequest>(Settings, "get");
        registry.Register<UpdateSettingsRequest>(Settings, "update");
        registry.Register<CaptureHotkeyRequest>(Settings, "captureHotkey");
        registry.Register<GetMicrophonesRequest>(Settings, "getMicrophones");
        registry.Register<GetEngineStatusRequest>(Engine, "getStatus");
        registry.Register<GetSetupStateRequest>(Setup, "getState");
        registry.Register<UpdateSetupStateRequest>(Setup, "update");
        registry.Register<StartMicTestRequest>(Setup, "startMicTest");
        registry.Register<StopMicTestRequest>(Setup, "stopMicTest");
        registry.Register<GetModelChoiceRequest>(Models, "getChoice");
        registry.Register<SetModelChoiceRequest>(Models, "setChoice");
        registry.Register<OpenLinkRequest>(App, "openLink");
        registry.Register<RestartAppRequest>(App, "restart");
        registry.Register<QuitAppRequest>(App, "quit");
        registry.Register<GetInputStatusRequest>(App, "getInputStatus");
        registry.Register<UiReadyRequest>(App, "ready");
    }
}
