import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import { useWebViewEvent } from "../../hooks/useWebViewEvent";
import type { HistoryEntry, HistoryPageResult } from "../../types/domain";

const LIMIT = 200;
const SEARCH_DEBOUNCE_MS = 200;

function errorMessage(err: unknown): string {
  return err instanceof Error && err.message ? err.message : "Something went wrong.";
}

/** Same rule as the server: a blank search matches everything, otherwise a case-insensitive contains. */
function matchesSearch(entry: HistoryEntry, search: string): boolean {
  return search.trim() === "" || entry.text.toLowerCase().includes(search.toLowerCase());
}

export function useHistory(search: string): {
  entries: HistoryEntry[];
  loading: boolean;
  /** True while an older page is on its way. */
  loadingOlder: boolean;
  /** Whether the store holds entries older than the last one loaded. */
  hasMore: boolean;
  error: string | null;
  /**
   * Appends the page of entries just below the oldest loaded one. Answers the id of the first entry it appended, or
   * null when it appended none (nothing to load, a failure, or a newer fetch or Clear all overtook it).
   */
  loadOlder(): Promise<string | null>;
  remove(id: string): Promise<void>;
  clearAll(): Promise<void>;
} {
  const [entries, setEntries] = useState<HistoryEntry[]>([]);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Each fetch gets a number; only the latest one may update the state. Loading an older page counts as a fetch too,
  // so a new search discards an older page still on its way.
  const latestFetch = useRef(0);
  const hasFetched = useRef(false);
  const olderInFlight = useRef(false);
  // The search the loaded entries answer. It lags `search` while the user is typing, and older pages must follow
  // the entries on screen, not the box.
  const loadedSearch = useRef("");

  const fetchEntries = useCallback(async (query: string) => {
    const fetchId = ++latestFetch.current;
    hasFetched.current = true;
    olderInFlight.current = false;
    setLoadingOlder(false);
    setLoading(true);
    try {
      const result = await apiClient.call<HistoryPageResult>("history", "get", { search: query, limit: LIMIT });
      if (fetchId !== latestFetch.current) return;
      loadedSearch.current = query;
      setEntries(result.entries);
      setHasMore(result.hasMore);
      setLoadError(null);
    } catch (err) {
      if (fetchId !== latestFetch.current) return;
      setLoadError(errorMessage(err));
    } finally {
      if (fetchId === latestFetch.current) setLoading(false);
    }
  }, []);

  // The first fetch is immediate; later ones wait for the user to stop typing.
  useEffect(() => {
    const delay = hasFetched.current ? SEARCH_DEBOUNCE_MS : 0;
    const timer = setTimeout(() => {
      setActionError(null);
      void fetchEntries(search);
    }, delay);

    return () => clearTimeout(timer);
  }, [search, fetchEntries]);

  useWebViewEvent<HistoryEntry>("history.added", (entry) => {
    if (!matchesSearch(entry, search)) return;
    // Prepended without trimming: older pages the user has loaded stay.
    setEntries((current) => (current.some((e) => e.id === entry.id) ? current : [entry, ...current]));
  });

  const loadOlder = useCallback(async (): Promise<string | null> => {
    if (loading || olderInFlight.current || !hasMore || entries.length === 0) return null;

    const fetchId = ++latestFetch.current;
    olderInFlight.current = true;
    setLoadingOlder(true);
    setActionError(null);
    try {
      const result = await apiClient.call<HistoryPageResult>("history", "get", {
        search: loadedSearch.current,
        limit: LIMIT,
        before: entries[entries.length - 1].id,
      });
      if (fetchId !== latestFetch.current) return null;
      setEntries((current) => {
        const known = new Set(current.map((e) => e.id));
        return [...current, ...result.entries.filter((e) => !known.has(e.id))];
      });
      setHasMore(result.hasMore);
      // Entries pushed meanwhile are new dictations, never part of an older page, so the entries this page was asked
      // below are enough to tell which of its entries are new.
      const shown = new Set(entries.map((e) => e.id));
      return result.entries.find((e) => !shown.has(e.id))?.id ?? null;
    } catch {
      if (fetchId !== latestFetch.current) return null;
      setActionError("Could not load older entries.");
      return null;
    } finally {
      if (fetchId === latestFetch.current) {
        olderInFlight.current = false;
        setLoadingOlder(false);
      }
    }
  }, [loading, hasMore, entries]);

  const remove = useCallback(async (id: string) => {
    setActionError(null);
    try {
      await apiClient.call<boolean>("history", "delete", { id });
      setEntries((current) => current.filter((e) => e.id !== id));
      // The last loaded entry went, yet older ones exist: show them rather than an empty page.
      if (hasMore && entries.length === 1 && entries[0].id === id) await fetchEntries(loadedSearch.current);
    } catch {
      // The server may have partly applied it, so show what is really stored.
      await fetchEntries(search);
      setActionError("Could not delete that entry. The list has been refreshed.");
    }
  }, [search, entries, hasMore, fetchEntries]);

  const clearAll = useCallback(async () => {
    setActionError(null);
    try {
      await apiClient.call<boolean>("history", "clear", {});
      // Counts as a fetch: whatever page is still on its way (an older page, a search) answers for entries that are
      // gone now, so it is dropped, and nothing is loading any more.
      ++latestFetch.current;
      olderInFlight.current = false;
      setLoadingOlder(false);
      setLoading(false);
      setEntries([]);
      setHasMore(false);
    } catch {
      await fetchEntries(search);
      setActionError("Could not clear the history. The list has been refreshed.");
    }
  }, [search, fetchEntries]);

  return { entries, loading, loadingOlder, hasMore, error: actionError ?? loadError, loadOlder, remove, clearAll };
}
