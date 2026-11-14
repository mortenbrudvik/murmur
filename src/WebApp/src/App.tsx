import { useEffect, useState } from "react";
import "./App.css";
import Sidebar from "./components/Sidebar";
import AboutDialog from "./features/about/AboutDialog";
import HotkeyUnavailableBanner from "./features/app/HotkeyUnavailableBanner";
import { useInputStatus } from "./features/app/useInputStatus";
import HistoryPage from "./features/history/HistoryPage";
import EngineStatusBanner from "./features/settings/EngineStatusBanner";
import SettingsPage from "./features/settings/SettingsPage";
import { useEngineStatus } from "./features/settings/useEngineStatus";
import SetupPage from "./features/setup/SetupPage";
import { useSetupState } from "./features/setup/useSetupState";
import { useWebViewEvent } from "./hooks/useWebViewEvent";
import { apiClient } from "./interop/WebViewApiClient";
import VocabularyPage from "./features/vocabulary/VocabularyPage";
import type { Page } from "./types/domain";

function App() {
  const { state: setup, update: updateSetup } = useSetupState();
  const engineStatus = useEngineStatus();
  const inputStatus = useInputStatus();
  // Null until the user picks a page: the page the launch asked for (if any) is shown until then.
  const [chosenPage, setChosenPage] = useState<Page | null>(null);
  const [aboutOpen, setAboutOpen] = useState(false);

  // "About Murmur" in the tray menu. Subscribed from the first mount, before the ready call below: the shell holds a
  // request back until this page reports ready, and sends it right after.
  useWebViewEvent<object>("app.showAbout", () => setAboutOpen(true));

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

  // Over the setup page as well: the tray's About Murmur works on a first run, where there is no sidebar.
  const about = <AboutDialog open={aboutOpen} engineStatus={engineStatus} onClose={() => setAboutOpen(false)} />;

  if (setup.showSetup) {
    // The alert is there too: the Try it step cannot work without the hotkey.
    return (
      <div className="setup-frame">
        <HotkeyUnavailableBanner status={inputStatus} />
        <SetupPage state={setup} engineStatus={engineStatus} update={updateSetup} onDone={() => setChosenPage("history")} />
        {about}
      </div>
    );
  }

  const page = chosenPage ?? setup.openPage ?? "history";

  return (
    <div className="shell">
      <Sidebar page={page} onNavigate={setChosenPage} onAbout={() => setAboutOpen(true)} />
      <div className="main-column">
        <HotkeyUnavailableBanner status={inputStatus} />
        <EngineStatusBanner status={engineStatus} />
        <main className="content">
          {page === "history" && <HistoryPage />}
          {page === "vocabulary" && <VocabularyPage />}
          {page === "settings" && <SettingsPage engineStatus={engineStatus} />}
        </main>
      </div>
      {about}
    </div>
  );
}

export default App;
