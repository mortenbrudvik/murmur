import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import { useWebViewEvent } from "../../hooks/useWebViewEvent";
import type { HistoryEntry } from "../../types/domain";

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
  error: string | null;
  remove(id: string): Promise<void>;
  clearAll(): Promise<void>;
} {
  const [entries, setEntries] = useState<HistoryEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Each fetch gets a number; only the latest one may update the state.
  const latestFetch = useRef(0);
  const hasFetched = useRef(false);

  const fetchEntries = useCallback(async (query: string) => {
    const fetchId = ++latestFetch.current;
    hasFetched.current = true;
    setLoading(true);
    try {
      const result = await apiClient.call<HistoryEntry[]>("history", "get", { search: query, limit: LIMIT });
      if (fetchId !== latestFetch.current) return;
      setEntries(result);
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
    setEntries((current) =>
      current.some((e) => e.id === entry.id) ? current : [entry, ...current].slice(0, LIMIT),
    );
  });

  const remove = useCallback(async (id: string) => {
    setActionError(null);
    try {
      await apiClient.call<boolean>("history", "delete", { id });
      setEntries((current) => current.filter((e) => e.id !== id));
    } catch {
      // The server may have partly applied it, so show what is really stored.
      await fetchEntries(search);
      setActionError("Could not delete that entry. The list has been refreshed.");
    }
  }, [search, fetchEntries]);

  const clearAll = useCallback(async () => {
    setActionError(null);
    try {
      await apiClient.call<boolean>("history", "clear", {});
      setEntries([]);
    } catch {
      await fetchEntries(search);
      setActionError("Could not clear the history. The list has been refreshed.");
    }
  }, [search, fetchEntries]);

  return { entries, loading, error: actionError ?? loadError, remove, clearAll };
}
