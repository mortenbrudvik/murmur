import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { apiClient } from "../interop/WebViewApiClient";
import type { DownloadProgress, EngineStatus, MurmurSettings, SetupState, Vocabulary } from "../types/domain";

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
const download: DownloadProgress = {
  modelName: "Whisper large-v3-turbo",
  bytesReceived: 400_000_000,
  totalBytes: 1_600_000_000,
  bytesPerSecond: 18_000_000,
  secondsRemaining: 70,
  retries: 0,
  reconnecting: false,
};
const ready: EngineStatus = { state: "ready", modelName: "Whisper large-v3-turbo", runtime: "Cuda", error: null, download: null, gpu: null, switching: false };

const setupDone: SetupState = { showSetup: false, step: "speechModel", cudaTipDismissed: false, openPage: null };
const firstRun: SetupState = { ...setupDone, showSetup: true };

function mockServer(status: EngineStatus, setup: SetupState = setupDone) {
  vi.mocked(apiClient.call).mockImplementation(async (service, method) => {
    const key = `${service}.${method}`;
    if (key === "setup.getState") return setup;
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
    mockServer({ ...ready, state: "downloading", runtime: null, download });

    render(<App />);

    expect(await screen.findByText("Downloading speech model… 25%")).toBeTruthy();
    expect(screen.getByText("400 MB of 1.6 GB · 18 MB/s · about 1 minute left")).toBeTruthy();
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

  it("shows a model switch's download above every page while the old model stays ready, and drops it when it is over", async () => {
    mockServer(ready);

    render(<App />);
    await screen.findByRole("heading", { name: "History" });
    expect(screen.queryByRole("status")).toBeNull();

    pushStatus({ ...ready, download: { ...download, modelName: "Whisper small (English)" } });
    expect(await screen.findByText("Downloading Whisper small (English)… 25%")).toBeTruthy();
    expect(screen.getByRole("status").className).toBe("engine-banner");
    expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("25");

    fireEvent.click(screen.getByRole("button", { name: "Vocabulary" }));
    expect(await screen.findByRole("heading", { name: "Vocabulary" })).toBeTruthy();
    expect(screen.getByRole("status").textContent).toContain("Downloading Whisper small (English)… 25%");

    pushStatus(ready);
    expect(screen.queryByRole("status")).toBeNull();
    expect(screen.queryByRole("progressbar")).toBeNull();
  });
});

describe("App first-run setup", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.on).mockClear();
  });

  it("shows nothing until the setup state is known", async () => {
    vi.mocked(apiClient.call).mockImplementation(() => new Promise(() => undefined));

    const { container } = render(<App />);
    await waitFor(() => expect(callsTo("setup.getState")).toHaveLength(1));

    expect(container.textContent).toBe("");
  });

  it("shows setup without the sidebar on first run", async () => {
    mockServer({ ...ready, state: "downloading", runtime: null, download }, firstRun);

    render(<App />);

    expect(await screen.findByRole("heading", { name: "Getting Murmur ready" })).toBeTruthy();
    expect(screen.queryByRole("navigation", { name: "Pages" })).toBeNull();
    expect(screen.queryByRole("button", { name: "History" })).toBeNull();
    // The engine banner would repeat what the setup page shows: it carries the status role, the page does not.
    expect(screen.queryByRole("status")).toBeNull();
    expect(screen.getByText("Downloading speech model")).toBeTruthy();
  });

  it("shows the sidebar when setup is complete", async () => {
    mockServer(ready);

    render(<App />);

    expect(await screen.findByRole("heading", { name: "History" })).toBeTruthy();
    expect(screen.getByRole("navigation", { name: "Pages" })).toBeTruthy();
    expect(screen.queryByRole("heading", { name: "Getting Murmur ready" })).toBeNull();
  });

  it("opens Settings when asked at launch", async () => {
    mockServer(ready, { ...setupDone, openPage: "settings" });

    render(<App />);

    expect(await screen.findByRole("heading", { name: "Settings" })).toBeTruthy();
    expect(screen.queryByRole("heading", { name: "History" })).toBeNull();
    expect(screen.getByRole("button", { name: "Settings" }).getAttribute("aria-current")).toBe("page");
  });

  it("lets the user leave the page that was opened at launch", async () => {
    mockServer(ready, { ...setupDone, openPage: "settings" });

    render(<App />);
    await screen.findByRole("heading", { name: "Settings" });
    fireEvent.click(screen.getByRole("button", { name: "Vocabulary" }));

    expect(await screen.findByRole("heading", { name: "Vocabulary" })).toBeTruthy();
    expect(screen.queryByRole("heading", { name: "Settings" })).toBeNull();
  });

  it("falls back to the normal window when the setup state cannot be read", async () => {
    mockServer(ready);
    const answer = vi.mocked(apiClient.call).getMockImplementation()!;
    vi.mocked(apiClient.call).mockImplementation(async (service, method, params) => {
      if (service === "setup" && method === "getState") throw new Error("bridge down");
      return answer(service, method, params);
    });

    render(<App />);

    expect(await screen.findByRole("heading", { name: "History" })).toBeTruthy();
    expect(screen.getByRole("navigation", { name: "Pages" })).toBeTruthy();
  });

  it("goes to History with the sidebar when setup is done", async () => {
    mockServer(ready, { ...firstRun, step: "tryIt" });

    render(<App />);
    fireEvent.click(await screen.findByRole("button", { name: "Done" }));

    expect(await screen.findByRole("heading", { name: "History" })).toBeTruthy();
    expect(screen.getByRole("navigation", { name: "Pages" })).toBeTruthy();
    expect(callsTo("setup.update").map(([, , params]) => params)).toEqual([{ completed: true }]);
  });

  it("goes to History after setup even if Settings was asked for at launch", async () => {
    mockServer(ready, { ...firstRun, step: "tryIt", openPage: "settings" });

    render(<App />);
    fireEvent.click(await screen.findByRole("button", { name: "Done" }));

    expect(await screen.findByRole("heading", { name: "History" })).toBeTruthy();
  });

  it("saves the step when Next is pressed, and moves on to it", async () => {
    mockServer({ ...ready, state: "loading", runtime: null }, firstRun);

    render(<App />);
    fireEvent.click(await screen.findByRole("button", { name: "Next" }));

    expect(await screen.findByRole("heading", { name: "Check your microphone" })).toBeTruthy();
    expect(callsTo("setup.update").map(([, , params]) => params)).toEqual([{ step: "microphone" }]);
  });
});

function callsTo(key: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => `${service}.${method}` === key);
}
