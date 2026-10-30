import { useMemo, useState } from "react";
import "./HistoryPage.css";
import HistoryEntryCard from "./HistoryEntryCard";
import { groupByDay } from "./groupByDay";
import { useHistory } from "./useHistory";
import { useHotkeyName } from "./useHotkeyName";

export default function HistoryPage() {
  const [search, setSearch] = useState("");
  const [confirmingClear, setConfirmingClear] = useState(false);
  const { entries, loading, error, remove, clearAll } = useHistory(search);
  const hotkeyName = useHotkeyName();

  const groups = useMemo(() => groupByDay(entries, new Date()), [entries]);
  const showEmpty = !loading && !error && entries.length === 0;

  function confirmClear() {
    setConfirmingClear(false);
    void clearAll();
  }

  return (
    <section className="history">
      <header className="history-header">
        <h1>History</h1>
        <div className="history-tools">
          <input
            type="search"
            className="history-search"
            placeholder="Search…"
            aria-label="Search history"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {entries.length > 0 &&
            (confirmingClear ? (
              <span className="history-confirm" role="group" aria-label="Confirm clearing the history">
                <span>Clear all history?</span>
                <button type="button" className="history-confirm-yes" onClick={confirmClear}>
                  Yes
                </button>
                <button type="button" className="history-confirm-no" onClick={() => setConfirmingClear(false)}>
                  No
                </button>
              </span>
            ) : (
              <button type="button" className="history-clear" onClick={() => setConfirmingClear(true)}>
                Clear all
              </button>
            ))}
        </div>
      </header>

      {error && (
        <p className="history-error" role="alert">
          {error}
        </p>
      )}

      <div className="history-scroll">
        {/* Keyed by the day, not its newest entry: a pushed entry must not remount the group and reset its cards. */}
        {groups.map((group) => (
          <section className="history-group" key={group.label}>
            <h2 className="history-day">{group.label}</h2>
            <ul className="history-list">
              {group.entries.map((entry) => (
                <HistoryEntryCard key={entry.id} entry={entry} onDelete={(id) => void remove(id)} />
              ))}
            </ul>
          </section>
        ))}

        {showEmpty && (
          <p className="history-empty">
            {search.trim() === ""
              ? `Nothing yet. Hold ${hotkeyName} and start talking.`
              : `No dictations match “${search.trim()}”.`}
          </p>
        )}
      </div>
    </section>
  );
}
