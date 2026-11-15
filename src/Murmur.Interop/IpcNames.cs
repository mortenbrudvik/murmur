namespace Murmur.Interop;

/// <summary>
/// The names the page uses: the services it calls (<c>apiClient.call(service, method)</c>) and the events it listens
/// for (<c>useWebViewEvent</c>). Renaming one here means renaming it in src/WebApp too.
/// </summary>
public static class IpcNames
{
    public static class Services
    {
        public const string App = "app";
        public const string Engine = "engine";
        public const string History = "history";
        public const string Models = "models";
        public const string Settings = "settings";
        public const string Setup = "setup";
        public const string Vocabulary = "vocabulary";
    }

    public static class Events
    {
        public const string EngineStatusChanged = "engine.statusChanged";
        public const string HistoryAdded = "history.added";
        public const string InputStatusChanged = "app.inputStatusChanged";
        public const string MicLevel = "setup.micLevel";
        public const string ShowSettings = "app.showSettings";
    }
}
