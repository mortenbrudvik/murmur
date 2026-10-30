import { useEffect, useMemo, useRef, useState } from "react";
import "./HistoryPage.css";
import HistoryEntryCard from "./HistoryEntryCard";
import { groupByDay } from "./groupByDay";
import { useHistory } from "./useHistory";
import { useHotkeyName } from "./useHotkeyName";

export default function HistoryPage() {
  const [search, setSearch] = useState("");
  const [confirmingClear, setConfirmingClear] = useState(false);
  // The first entry the last Show older loaded, which takes the focus from the button (it goes with the last page).
  const [focusEntryId, setFocusEntryId] = useState<string | null>(null);
  const list = useRef<HTMLDivElement>(null);
  const { entries, loading, loadingOlder, hasMore, error, loadOlder, remove, clearAll } = useHistory(search);
  const hotkeyName = useHotkeyName();

  const groups = useMemo(() => groupByDay(entries, new Date()), [entries]);
  const showEmpty = !loading && !error && entries.length === 0;

  // Only Show older sets it, so the first page and pushed entries never take the focus.
  useEffect(() => {
    if (focusEntryId === null) return;
    list.current?.querySelector<HTMLElement>(`[data-entry-id="${focusEntryId}"]`)?.focus();
  }, [focusEntryId]);

  function confirmClear() {
    setConfirmingClear(false);
    void clearAll();
  }

  async function showOlder() {
    const first = await loadOlder();
    if (first !== null) setFocusEntryId(first);
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

      <div className="history-scroll" ref={list}>
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

        {hasMore && entries.length > 0 && (
          <button
            type="button"
            className="history-older"
            disabled={loading || loadingOlder}
            onClick={() => void showOlder()}
          >
            Show older
          </button>
        )}

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
