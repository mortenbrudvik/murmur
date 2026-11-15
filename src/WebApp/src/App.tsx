import { useEffect, useState } from "react";
import "./App.css";
import Sidebar from "./components/Sidebar";
import HotkeyUnavailableBanner from "./features/app/HotkeyUnavailableBanner";
import HistoryPage from "./features/history/HistoryPage";
import EngineStatusBanner from "./features/settings/EngineStatusBanner";
import SettingsPage from "./features/settings/SettingsPage";
import SetupPage from "./features/setup/SetupPage";
import { useSetupState } from "./features/setup/useSetupState";
import { usePushedState } from "./hooks/usePushedState";
import { useWebViewEvent } from "./hooks/useWebViewEvent";
import { apiClient } from "./interop/WebViewApiClient";
import VocabularyPage from "./features/vocabulary/VocabularyPage";
import type { EngineStatus, InputStatus, Page } from "./types/domain";

function App() {
  const { state: setup, update: updateSetup } = useSetupState();
  const engineStatus = usePushedState<EngineStatus>("engine", "getStatus", "engine.statusChanged");
  // Whether Windows let Murmur install the keyboard hook the hotkey needs. The hook may still be starting when the window
  // asks, and "installed" is the answer until a failure is pushed.
  const inputStatus = usePushedState<InputStatus>("app", "getInputStatus", "app.inputStatusChanged");
  // Null until the user picks a page: the page the launch asked for (if any) is shown until then.
  const [chosenPage, setChosenPage] = useState<Page | null>(null);

  // "Settings" in the tray menu. Subscribed from the first mount, before the ready call below: the shell holds a
  // request back until this page reports ready, and sends it right after.
  useWebViewEvent<object>("app.showSettings", () => setChosenPage("settings"));

  // Tells the shell that the first real page is on screen (the setup page or the normal window), so that the splash
  // can hand over to a window that is already drawn. Once: `drawn` only ever goes from false to true.
  const drawn = setup !== null;
  useEffect(() => {
    if (!drawn) return;

    apiClient.call("app", "ready", {}).catch(() => {
      // The shell does not wait for ever: the splash closes on its own timeout.
    });
  }, [drawn]);

  // The setup state arrives within milliseconds; showing the normal window first would flash it before the setup.
  if (!setup) return null;

  if (setup.showSetup) {
    // The alert is there too: the Try it step cannot work without the hotkey.
    return (
      <div className="setup-frame">
        <HotkeyUnavailableBanner status={inputStatus} />
        <SetupPage state={setup} engineStatus={engineStatus} update={updateSetup} onDone={() => setChosenPage("history")} />
      </div>
    );
  }

  const page = chosenPage ?? setup.openPage ?? "history";

  return (
    <div className="shell">
      <Sidebar page={page} onNavigate={setChosenPage} />
      <div className="main-column">
        <HotkeyUnavailableBanner status={inputStatus} />
        <EngineStatusBanner status={engineStatus} hideDownload={page === "settings"} />
        <main className="content">
          {page === "history" && <HistoryPage />}
          {page === "vocabulary" && <VocabularyPage />}
          {page === "settings" && <SettingsPage engineStatus={engineStatus} />}
        </main>
      </div>
    </div>
  );
}

export default App;
