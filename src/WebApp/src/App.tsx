import { useState } from "react";
import "./App.css";
import Sidebar from "./components/Sidebar";
import HotkeyUnavailableBanner from "./features/app/HotkeyUnavailableBanner";
import { useInputStatus } from "./features/app/useInputStatus";
import HistoryPage from "./features/history/HistoryPage";
import EngineStatusBanner from "./features/settings/EngineStatusBanner";
import SettingsPage from "./features/settings/SettingsPage";
import { useEngineStatus } from "./features/settings/useEngineStatus";
import SetupPage from "./features/setup/SetupPage";
import { useSetupState } from "./features/setup/useSetupState";
import VocabularyPage from "./features/vocabulary/VocabularyPage";
import type { Page } from "./types/domain";

function App() {
  const { state: setup, update: updateSetup } = useSetupState();
  const engineStatus = useEngineStatus();
  const inputStatus = useInputStatus();
  // Null until the user picks a page: the page the launch asked for (if any) is shown until then.
  const [chosenPage, setChosenPage] = useState<Page | null>(null);

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
        <EngineStatusBanner status={engineStatus} />
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
