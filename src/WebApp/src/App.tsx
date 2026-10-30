import { useState } from "react";
import "./App.css";
import Sidebar from "./components/Sidebar";
import HistoryPage from "./features/history/HistoryPage";
import EngineStatusBanner from "./features/settings/EngineStatusBanner";
import SettingsPage from "./features/settings/SettingsPage";
import { useEngineStatus } from "./features/settings/useEngineStatus";
import VocabularyPage from "./features/vocabulary/VocabularyPage";
import type { Page } from "./types/domain";

function App() {
  const [page, setPage] = useState<Page>("history");
  const engineStatus = useEngineStatus();

  return (
    <div className="shell">
      <Sidebar page={page} onNavigate={setPage} />
      <div className="main-column">
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
