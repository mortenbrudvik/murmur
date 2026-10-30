import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useHistory } from "../useHistory";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { HistoryEntry, HistoryPageResult } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

function makeEntry(id: string, text = `text ${id}`): HistoryEntry {
  return { id, timestamp: "2026-10-29T10:00:00+01:00", text, targetApp: "Claude", pasted: true };
}

function page(entries: HistoryEntry[], hasMore = false): HistoryPageResult {
  return { entries, hasMore };
}

/** The callback useHistory registered for history.added. */
function addedCallback(): (entry: HistoryEntry) => void {
  const calls = vi.mocked(apiClient.on).mock.calls.filter(([name]) => name === "history.added");
  expect(calls.length).toBeGreaterThan(0);
  return calls[calls.length - 1][1] as (entry: HistoryEntry) => void;
}

function historyGetCalls() {
  return vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => service === "history" && method === "get");
}

describe("useHistory", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.on).mockClear();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("fetches on mount and sets the entries", async () => {
    const entries = [makeEntry("a"), makeEntry("b")];
    vi.mocked(apiClient.call).mockResolvedValue(page(entries));

    const { result } = renderHook(() => useHistory(""));

    expect(result.current.loading).toBe(true);
    await waitFor(() => expect(result.current.entries).toEqual(entries));
    expect(apiClient.call).toHaveBeenCalledWith("history", "get", { search: "", limit: 200 });
    expect(result.current.loading).toBe(false);
    expect(result.current.error).toBeNull();
  });

  it("reports a failed fetch", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));

    const { result } = renderHook(() => useHistory(""));

    await waitFor(() => expect(result.current.error).toBe("bridge down"));
    expect(result.current.loading).toBe(false);
    expect(result.current.entries).toEqual([]);
  });

  it("refetches 200 ms after the search changes, and only once for fast typing", async () => {
    vi.useFakeTimers();
    vi.mocked(apiClient.call).mockResolvedValue(page([]));

    const { rerender } = renderHook(({ search }) => useHistory(search), { initialProps: { search: "" } });
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(historyGetCalls()).toHaveLength(1);

    rerender({ search: "h" });
    await act(async () => { await vi.advanceTimersByTimeAsync(150); });
    rerender({ search: "he" });
    await act(async () => { await vi.advanceTimersByTimeAsync(199); });
    expect(historyGetCalls()).toHaveLength(1);

    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    expect(historyGetCalls()).toHaveLength(2);
    expect(apiClient.call).toHaveBeenLastCalledWith("history", "get", { search: "he", limit: 200 });
  });

  it("ignores a slow response for an older search", async () => {
    vi.useFakeTimers();
    let resolveFirst: (value: HistoryPageResult) => void = () => {};
    vi.mocked(apiClient.call)
      .mockReturnValueOnce(new Promise<HistoryPageResult>((resolve) => { resolveFirst = resolve; }))
      .mockResolvedValueOnce(page([makeEntry("new")]));

    const { result, rerender } = renderHook(({ search }) => useHistory(search), { initialProps: { search: "" } });
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    rerender({ search: "x" });
    await act(async () => { await vi.advanceTimersByTimeAsync(200); });
    expect(result.current.entries.map((e) => e.id)).toEqual(["new"]);

    await act(async () => { resolveFirst(page([makeEntry("stale")])); });
    expect(result.current.entries.map((e) => e.id)).toEqual(["new"]);
  });

  it("prepends an entry pushed by history.added", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(page([makeEntry("a")]));
    const { result } = renderHook(() => useHistory(""));
    await waitFor(() => expect(result.current.entries).toHaveLength(1));

    act(() => addedCallback()(makeEntry("fresh")));

    expect(result.current.entries.map((e) => e.id)).toEqual(["fresh", "a"]);
  });

  it("does not add the same pushed entry twice", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(page([makeEntry("a")]));
    const { result } = renderHook(() => useHistory(""));
    await waitFor(() => expect(result.current.entries).toHaveLength(1));

    act(() => addedCallback()(makeEntry("a")));

    expect(result.current.entries.map((e) => e.id)).toEqual(["a"]);
  });

  it("prepends a pushed entry that contains the search, ignoring case", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(page([]));
    const { result } = renderHook(() => useHistory("hello"));
    await waitFor(() => expect(result.current.loading).toBe(false));

    act(() => addedCallback()(makeEntry("hit", "Well HELLO there")));

    expect(result.current.entries.map((e) => e.id)).toEqual(["hit"]);
  });

  it("ignores a pushed entry that does not match the search", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(page([makeEntry("a", "hello world")]));
    const { result } = renderHook(() => useHistory("hello"));
    await waitFor(() => expect(result.current.entries).toHaveLength(1));

    act(() => addedCallback()(makeEntry("miss", "goodbye")));

    expect(result.current.entries.map((e) => e.id)).toEqual(["a"]);
  });

  it("removes an entry locally after deleting it", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) =>
      method === "get" ? page([makeEntry("a"), makeEntry("b")]) : true,
    );
    const { result } = renderHook(() => useHistory(""));
    await waitFor(() => expect(result.current.entries).toHaveLength(2));

    await act(async () => { await result.current.remove("a"); });

    expect(apiClient.call).toHaveBeenCalledWith("history", "delete", { id: "a" });
    expect(result.current.entries.map((e) => e.id)).toEqual(["b"]);
    expect(result.current.error).toBeNull();
  });

  it("refetches and reports an error when a delete fails", async () => {
    let getCount = 0;
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
      if (method === "delete") throw new Error("disk full");
      getCount++;
      return getCount === 1 ? page([makeEntry("a"), makeEntry("b")]) : page([makeEntry("b")]);
    });
    const { result } = renderHook(() => useHistory(""));
    await waitFor(() => expect(result.current.entries).toHaveLength(2));

    await act(async () => { await result.current.remove("a"); });

    expect(getCount).toBe(2);
    expect(result.current.entries.map((e) => e.id)).toEqual(["b"]);
    expect(result.current.error).toMatch(/delete/i);
  });

  it("empties the list after clearing", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) =>
      method === "get" ? page([makeEntry("a"), makeEntry("b")]) : true,
    );
    const { result } = renderHook(() => useHistory(""));
    await waitFor(() => expect(result.current.entries).toHaveLength(2));

    await act(async () => { await result.current.clearAll(); });

    expect(apiClient.call).toHaveBeenCalledWith("history", "clear", {});
    expect(result.current.entries).toEqual([]);
    expect(result.current.error).toBeNull();
  });

  it("refetches and reports an error when clearing fails", async () => {
    let getCount = 0;
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
      if (method === "clear") throw new Error("locked");
      getCount++;
      return page([makeEntry("a")]);
    });
    const { result } = renderHook(() => useHistory(""));
    await waitFor(() => expect(result.current.entries).toHaveLength(1));

    await act(async () => { await result.current.clearAll(); });

    expect(getCount).toBe(2);
    expect(result.current.entries.map((e) => e.id)).toEqual(["a"]);
    expect(result.current.error).toMatch(/clear/i);
  });

  describe("older pages", () => {
    /** Entries named "<prefix>0".."<prefix>n-1", newest first. */
    function entriesNamed(prefix: string, count: number): HistoryEntry[] {
      return Array.from({ length: count }, (_, i) => makeEntry(`${prefix}${i}`));
    }

    it("reports whether older entries remain", async () => {
      vi.mocked(apiClient.call).mockResolvedValue(page([makeEntry("a")], true));

      const { result } = renderHook(() => useHistory(""));

      await waitFor(() => expect(result.current.hasMore).toBe(true));
    });

    it("loads the next page below the oldest entry and appends it", async () => {
      vi.mocked(apiClient.call)
        .mockResolvedValueOnce(page([makeEntry("a"), makeEntry("b")], true))
        .mockResolvedValueOnce(page([makeEntry("c"), makeEntry("d")], false));
      const { result } = renderHook(() => useHistory(""));
      await waitFor(() => expect(result.current.entries).toHaveLength(2));

      await act(async () => { await result.current.loadOlder(); });

      expect(apiClient.call).toHaveBeenLastCalledWith("history", "get", { search: "", limit: 200, before: "b" });
      expect(result.current.entries.map((e) => e.id)).toEqual(["a", "b", "c", "d"]);
      expect(result.current.hasMore).toBe(false);
      expect(result.current.loadingOlder).toBe(false);
    });

    it("keeps the search that produced the entries when it loads older ones", async () => {
      vi.mocked(apiClient.call)
        .mockResolvedValueOnce(page([makeEntry("a", "hello a")], true))
        .mockResolvedValueOnce(page([makeEntry("b", "hello b")]));
      const { result } = renderHook(() => useHistory("hello"));
      await waitFor(() => expect(result.current.entries).toHaveLength(1));

      await act(async () => { await result.current.loadOlder(); });

      expect(apiClient.call).toHaveBeenLastCalledWith("history", "get", { search: "hello", limit: 200, before: "a" });
    });

    it("does nothing when there is no older page", async () => {
      vi.mocked(apiClient.call).mockResolvedValue(page([makeEntry("a")], false));
      const { result } = renderHook(() => useHistory(""));
      await waitFor(() => expect(result.current.entries).toHaveLength(1));

      await act(async () => { await result.current.loadOlder(); });

      expect(historyGetCalls()).toHaveLength(1);
    });

    it("asks for one older page at a time", async () => {
      let resolveOlder: (value: HistoryPageResult) => void = () => {};
      vi.mocked(apiClient.call)
        .mockResolvedValueOnce(page([makeEntry("a")], true))
        .mockReturnValueOnce(new Promise<HistoryPageResult>((resolve) => { resolveOlder = resolve; }));
      const { result } = renderHook(() => useHistory(""));
      await waitFor(() => expect(result.current.entries).toHaveLength(1));

      act(() => { void result.current.loadOlder(); });
      await waitFor(() => expect(result.current.loadingOlder).toBe(true));
      await act(async () => { await result.current.loadOlder(); });
      expect(historyGetCalls()).toHaveLength(2);

      await act(async () => { resolveOlder(page([makeEntry("b")])); });
      expect(result.current.entries.map((e) => e.id)).toEqual(["a", "b"]);
      expect(result.current.loadingOlder).toBe(false);
    });

    it("does not repeat an entry the older page already holds", async () => {
      vi.mocked(apiClient.call)
        .mockResolvedValueOnce(page([makeEntry("a"), makeEntry("b")], true))
        .mockResolvedValueOnce(page([makeEntry("b"), makeEntry("c")]));
      const { result } = renderHook(() => useHistory(""));
      await waitFor(() => expect(result.current.entries).toHaveLength(2));

      await act(async () => { await result.current.loadOlder(); });

      expect(result.current.entries.map((e) => e.id)).toEqual(["a", "b", "c"]);
    });

    it("a live entry does not drop loaded older pages", async () => {
      vi.mocked(apiClient.call)
        .mockResolvedValueOnce(page(entriesNamed("n", 200), true))
        .mockResolvedValueOnce(page(entriesNamed("o", 200), true));
      const { result } = renderHook(() => useHistory(""));
      await waitFor(() => expect(result.current.entries).toHaveLength(200));
      await act(async () => { await result.current.loadOlder(); });
      expect(result.current.entries).toHaveLength(400);

      act(() => addedCallback()(makeEntry("fresh")));

      expect(result.current.entries).toHaveLength(401);
      expect(result.current.entries[0].id).toBe("fresh");
      expect(result.current.entries[400].id).toBe("o199");
      expect(result.current.hasMore).toBe(true);
    });

    it("the next page after a live entry still starts below the oldest loaded entry", async () => {
      vi.mocked(apiClient.call)
        .mockResolvedValueOnce(page([makeEntry("a"), makeEntry("b")], true))
        .mockResolvedValueOnce(page([makeEntry("c")]));
      const { result } = renderHook(() => useHistory(""));
      await waitFor(() => expect(result.current.entries).toHaveLength(2));
      act(() => addedCallback()(makeEntry("fresh")));

      await act(async () => { await result.current.loadOlder(); });

      expect(apiClient.call).toHaveBeenLastCalledWith("history", "get", { search: "", limit: 200, before: "b" });
      expect(result.current.entries.map((e) => e.id)).toEqual(["fresh", "a", "b", "c"]);
    });

    it("keeps the list and reports an error when the older page cannot be loaded", async () => {
      vi.mocked(apiClient.call)
        .mockResolvedValueOnce(page([makeEntry("a")], true))
        .mockRejectedValueOnce(new Error("bridge down"));
      const { result } = renderHook(() => useHistory(""));
      await waitFor(() => expect(result.current.entries).toHaveLength(1));

      await act(async () => { await result.current.loadOlder(); });

      expect(result.current.error).toMatch(/older/i);
      expect(result.current.entries.map((e) => e.id)).toEqual(["a"]);
      expect(result.current.hasMore).toBe(true);
      expect(result.current.loadingOlder).toBe(false);
    });

    it("drops an older page that arrives after the search changed", async () => {
      vi.useFakeTimers();
      let resolveOlder: (value: HistoryPageResult) => void = () => {};
      vi.mocked(apiClient.call)
        .mockResolvedValueOnce(page([makeEntry("a")], true))
        .mockReturnValueOnce(new Promise<HistoryPageResult>((resolve) => { resolveOlder = resolve; }))
        .mockResolvedValueOnce(page([makeEntry("fresh", "x")]));
      const { result, rerender } = renderHook(({ search }) => useHistory(search), { initialProps: { search: "" } });
      await act(async () => { await vi.advanceTimersByTimeAsync(0); });
      act(() => { void result.current.loadOlder(); });
      rerender({ search: "x" });
      await act(async () => { await vi.advanceTimersByTimeAsync(200); });
      expect(result.current.entries.map((e) => e.id)).toEqual(["fresh"]);

      await act(async () => { resolveOlder(page([makeEntry("stale")])); });

      expect(result.current.entries.map((e) => e.id)).toEqual(["fresh"]);
      expect(result.current.loadingOlder).toBe(false);
      expect(result.current.hasMore).toBe(false);
    });

    it("loads the first page again when every loaded entry was deleted but older ones remain", async () => {
      let getCount = 0;
      vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
        if (method !== "get") return true;
        getCount++;
        return getCount === 1 ? page([makeEntry("a")], true) : page([makeEntry("b")]);
      });
      const { result } = renderHook(() => useHistory(""));
      await waitFor(() => expect(result.current.entries).toHaveLength(1));

      await act(async () => { await result.current.remove("a"); });

      expect(getCount).toBe(2);
      expect(result.current.entries.map((e) => e.id)).toEqual(["b"]);
    });

    it("has nothing older after clearing", async () => {
      vi.mocked(apiClient.call).mockImplementation(async (_service, method) =>
        method === "get" ? page([makeEntry("a")], true) : true,
      );
      const { result } = renderHook(() => useHistory(""));
      await waitFor(() => expect(result.current.hasMore).toBe(true));

      await act(async () => { await result.current.clearAll(); });

      expect(result.current.entries).toEqual([]);
      expect(result.current.hasMore).toBe(false);
    });
  });
});
