import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import HistoryPage from "../HistoryPage";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { HistoryEntry, MurmurSettings } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const settings: MurmurSettings = {
  hotkey: { virtualKey: 163, modifiers: "None", displayName: "Right Alt" },
  microphoneId: null,
  soundCues: true,
  startWithWindows: false,
  keepAudio: false,
};

/** An entry from today, at the given local time. */
function todayEntry(id: string, hour: number, minute: number, overrides: Partial<HistoryEntry> = {}): HistoryEntry {
  const now = new Date();
  const when = new Date(now.getFullYear(), now.getMonth(), now.getDate(), hour, minute);
  return { id, timestamp: when.toISOString(), text: `text ${id}`, targetApp: "Claude", pasted: true, ...overrides };
}

function mockApi(entries: HistoryEntry[], overrides: Record<string, () => unknown> = {}) {
  vi.mocked(apiClient.call).mockImplementation(async (service, method) => {
    const key = `${service}.${method}`;
    if (overrides[key]) return overrides[key]();
    if (key === "history.get") return entries;
    if (key === "settings.get") return settings;
    return true;
  });
}

function setClipboard(writeText: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
}

/** Pushes an entry the way the host does after a dictation. */
function pushAdded(entry: HistoryEntry) {
  const calls = vi.mocked(apiClient.on).mock.calls.filter(([name]) => name === "history.added");
  expect(calls.length).toBeGreaterThan(0);
  const callback = calls[calls.length - 1][1] as (entry: HistoryEntry) => void;
  act(() => callback(entry));
}

describe("HistoryPage", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.on).mockClear();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("shows the day label and a card with text and the app and time", async () => {
    mockApi([todayEntry("a", 19, 12, { text: "Yes WPF, but hybrid.", targetApp: "Claude" })]);

    render(<HistoryPage />);

    expect(await screen.findByText("Yes WPF, but hybrid.")).toBeTruthy();
    expect(screen.getByText("Today")).toBeTruthy();
    expect(screen.getByText("Claude · 19:12")).toBeTruthy();
    expect(screen.queryByText("Not pasted")).toBeNull();
  });

  it("shows a dash for an unknown app and a tag for text that was not pasted", async () => {
    mockApi([todayEntry("a", 9, 5, { targetApp: "", pasted: false })]);

    render(<HistoryPage />);

    expect(await screen.findByText("— · 09:05")).toBeTruthy();
    expect(screen.getByText("Not pasted")).toBeTruthy();
  });

  it("names the configured hotkey in the empty state", async () => {
    mockApi([]);

    render(<HistoryPage />);

    expect(await screen.findByText("Nothing yet. Hold Right Alt and start talking.")).toBeTruthy();
  });

  it("falls back to Right Ctrl when the settings cannot be read", async () => {
    mockApi([], { "settings.get": () => { throw new Error("nope"); } });

    render(<HistoryPage />);

    expect(await screen.findByText("Nothing yet. Hold Right Ctrl and start talking.")).toBeTruthy();
  });

  it("copies the text and shows Copied for 1.5 seconds", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const writeText = vi.fn().mockResolvedValue(undefined);
    setClipboard(writeText);
    mockApi([todayEntry("a", 10, 0, { text: "copy me" })]);

    render(<HistoryPage />);
    fireEvent.click(await screen.findByRole("button", { name: "Copy" }));

    expect(writeText).toHaveBeenCalledWith("copy me");
    expect(await screen.findByRole("button", { name: "Copied" })).toBeTruthy();

    await act(async () => { await vi.advanceTimersByTimeAsync(1500); });

    expect(screen.getByRole("button", { name: "Copy" })).toBeTruthy();
  });

  it("keeps a card's Copied state when a new entry is pushed into the same day", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    setClipboard(vi.fn().mockResolvedValue(undefined));
    mockApi([todayEntry("a", 10, 0, { text: "copy me" })]);

    render(<HistoryPage />);
    fireEvent.click(await screen.findByRole("button", { name: "Copy" }));
    expect(await screen.findByRole("button", { name: "Copied" })).toBeTruthy();

    pushAdded(todayEntry("b", 11, 0, { text: "just dictated" }));

    expect(await screen.findByText("just dictated")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Copied" })).toBeTruthy();
    expect(screen.getAllByRole("button", { name: "Copy" })).toHaveLength(1);
  });

  it("shows Copy failed for 1.5 seconds when the clipboard rejects", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    setClipboard(vi.fn().mockRejectedValue(new Error("denied")));
    mockApi([todayEntry("a", 10, 0)]);

    render(<HistoryPage />);
    fireEvent.click(await screen.findByRole("button", { name: "Copy" }));

    expect(await screen.findByRole("button", { name: "Copy failed" })).toBeTruthy();

    await act(async () => { await vi.advanceTimersByTimeAsync(1500); });

    expect(screen.getByRole("button", { name: "Copy" })).toBeTruthy();
  });

  it("deletes an entry with the x button", async () => {
    mockApi([todayEntry("a", 10, 0), todayEntry("b", 9, 0)]);

    render(<HistoryPage />);
    await screen.findByText("text a");
    fireEvent.click(screen.getAllByRole("button", { name: "Delete" })[0]);

    await waitFor(() => expect(screen.queryByText("text a")).toBeNull());
    expect(apiClient.call).toHaveBeenCalledWith("history", "delete", { id: "a" });
    expect(screen.getByText("text b")).toBeTruthy();
  });

  it("asks inline before clearing everything, and does nothing on No", async () => {
    mockApi([todayEntry("a", 10, 0)]);

    render(<HistoryPage />);
    await screen.findByText("text a");
    fireEvent.click(screen.getByRole("button", { name: "Clear all" }));

    expect(screen.getByText("Clear all history?")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "No" }));

    expect(screen.queryByText("Clear all history?")).toBeNull();
    expect(apiClient.call).not.toHaveBeenCalledWith("history", "clear", {});
    expect(screen.getByText("text a")).toBeTruthy();
  });

  it("clears everything after Yes", async () => {
    mockApi([todayEntry("a", 10, 0)]);

    render(<HistoryPage />);
    await screen.findByText("text a");
    fireEvent.click(screen.getByRole("button", { name: "Clear all" }));
    fireEvent.click(screen.getByRole("button", { name: "Yes" }));

    await waitFor(() => expect(screen.queryByText("text a")).toBeNull());
    expect(apiClient.call).toHaveBeenCalledWith("history", "clear", {});
    expect(screen.queryByText("Clear all history?")).toBeNull();
    expect(await screen.findByText(/Nothing yet/)).toBeTruthy();
  });

  it("shows an inline error when a delete fails", async () => {
    mockApi([todayEntry("a", 10, 0)], { "history.delete": () => { throw new Error("locked"); } });

    render(<HistoryPage />);
    await screen.findByText("text a");
    fireEvent.click(screen.getByRole("button", { name: "Delete" }));

    expect((await screen.findByRole("alert")).textContent).toMatch(/could not delete/i);
    expect(screen.getByText("text a")).toBeTruthy();
  });

  it("says when a search finds nothing", async () => {
    mockApi([]);

    render(<HistoryPage />);
    await screen.findByText(/Nothing yet/);
    fireEvent.change(screen.getByPlaceholderText("Search…"), { target: { value: "zzz" } });

    expect(await screen.findByText("No dictations match “zzz”.")).toBeTruthy();
  });
});
