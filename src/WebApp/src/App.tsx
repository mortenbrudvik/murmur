import { useState } from "react";
import "./App.css";
import Sidebar from "./components/Sidebar";
import HistoryPage from "./features/history/HistoryPage";
import type { Page } from "./types/domain";

// Placeholder until the Vocabulary and Settings pages arrive.
function ComingSoon({ title }: { title: string }) {
  return (
    <section className="coming-soon">
      <h1>{title}</h1>
      <p>Coming soon.</p>
    </section>
  );
}

function App() {
  const [page, setPage] = useState<Page>("history");

  return (
    <div className="shell">
      <Sidebar page={page} onNavigate={setPage} />
      <main className="content">
        {page === "history" && <HistoryPage />}
        {page === "vocabulary" && <ComingSoon title="Vocabulary" />}
        {page === "settings" && <ComingSoon title="Settings" />}
      </main>
    </div>
  );
}

export default App;
