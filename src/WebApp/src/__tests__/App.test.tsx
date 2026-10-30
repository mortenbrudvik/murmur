import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { apiClient } from "../interop/WebViewApiClient";
import type { EngineStatus, MurmurSettings, Vocabulary } from "../types/domain";

vi.mock("../interop/WebViewApiClient", () => ({
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
const vocabulary: Vocabulary = { hintWords: [], replacements: [] };
const ready: EngineStatus = { state: "ready", modelName: "large-v3-turbo", runtime: "Cuda", downloadProgress: null, error: null };

function mockServer(status: EngineStatus) {
  vi.mocked(apiClient.call).mockImplementation(async (service, method) => {
    const key = `${service}.${method}`;
    if (key === "engine.getStatus") return status;
    if (key === "settings.get") return settings;
    if (key === "vocabulary.get") return vocabulary;
    if (key === "settings.getMicrophones" || key === "history.get") return [];
    return null;
  });
}

/** Delivers an engine.statusChanged push to everything that subscribed to it. */
function pushStatus(status: EngineStatus) {
  const callbacks = vi.mocked(apiClient.on).mock.calls
    .filter(([name]) => name === "engine.statusChanged")
    .map(([, callback]) => callback as (status: EngineStatus) => void);
  expect(callbacks.length).toBeGreaterThan(0);
  act(() => callbacks.forEach((callback) => callback(status)));
}

describe("App engine status banner", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.on).mockClear();
  });

  it("shows the download progress above the History page, with a progress bar", async () => {
    mockServer({ ...ready, state: "downloading", runtime: null, downloadProgress: 0.25 });

    render(<App />);

    expect(await screen.findByText("Downloading model… 25%")).toBeTruthy();
    expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("25");
    expect(screen.getByRole("heading", { name: "History" })).toBeTruthy();
  });

  it("stays above every page", async () => {
    mockServer({ ...ready, state: "loading", runtime: null });

    render(<App />);
    expect(await screen.findByRole("status")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Vocabulary" }));
    expect(await screen.findByRole("heading", { name: "Vocabulary" })).toBeTruthy();
    expect(screen.getByRole("status").textContent).toBe("Loading model…");

    fireEvent.click(screen.getByRole("button", { name: "Settings" }));
    expect(await screen.findByRole("heading", { name: "Settings" })).toBeTruthy();
    // The banner and the engine card both carry the same text.
    expect(screen.getAllByText("Loading model…")).toHaveLength(2);
  });

  it("is hidden when the engine is ready", async () => {
    mockServer(ready);

    render(<App />);
    await waitFor(() => expect(callsTo("engine.getStatus")).toHaveLength(1));
    await screen.findByRole("heading", { name: "History" });

    expect(screen.queryByRole("status")).toBeNull();
    expect(screen.queryByText(/Ready/)).toBeNull();
  });

  it("appears and disappears as the engine status changes", async () => {
    mockServer({ ...ready, state: "loading", runtime: null });

    render(<App />);
    expect(await screen.findByText("Loading model…")).toBeTruthy();

    pushStatus(ready);
    expect(screen.queryByText("Loading model…")).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();

    pushStatus({ ...ready, state: "error", runtime: null, error: "Model file is corrupt" });
    expect((await screen.findByRole("alert")).textContent).toBe("Model file is corrupt");
  });
});

function callsTo(key: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => `${service}.${method}` === key);
}
