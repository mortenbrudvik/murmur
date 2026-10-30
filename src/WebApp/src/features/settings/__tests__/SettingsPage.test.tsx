import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import SettingsPage from "../SettingsPage";
import { apiClient } from "../../../interop/WebViewApiClient";
import type {
  DownloadProgress,
  EngineStatus,
  HotkeyDefinition,
  MicrophoneInfo,
  ModelChoice,
  MurmurSettings,
} from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const rightAlt: HotkeyDefinition = { virtualKey: 163, modifiers: "None", displayName: "Right Alt" };
const f9: HotkeyDefinition = { virtualKey: 120, modifiers: "Ctrl", displayName: "Ctrl + F9" };

const stored: MurmurSettings = {
  hotkey: rightAlt,
  microphoneId: null,
  soundCues: true,
  startWithWindows: false,
  keepAudio: true,
};

const usb: MicrophoneInfo = { id: "usb", name: "USB Mic", isDefault: true };
const headset: MicrophoneInfo = { id: "headset", name: "Headset", isDefault: false };

const ready: EngineStatus = { state: "ready", modelName: "Whisper large-v3-turbo", runtime: "Cuda", error: null, download: null, gpu: null, switching: false };

function engine(overrides: Partial<EngineStatus>): EngineStatus {
  return { ...ready, ...overrides };
}

const smallDownload: DownloadProgress = {
  modelName: "Whisper small (English)",
  bytesReceived: 122_000_000,
  totalBytes: 488_000_000,
  bytesPerSecond: 18_000_000,
  secondsRemaining: 20,
  retries: 0,
  reconnecting: false,
};

const BEST = "Best accuracy (large, 1.6 GB)";
const FAST = "Faster (small, 488 MB)";
const BEST_RECOMMENDED = `${BEST} — recommended for this PC`;
const choice: ModelChoice = {
  selected: "largeV3Turbo",
  recommended: "largeV3Turbo",
  options: [
    { id: "largeV3Turbo", label: BEST, sizeBytes: 1_624_555_275 },
    { id: "smallEn", label: FAST, sizeBytes: 487_614_201 },
  ],
};

interface ServerOptions {
  settings?: MurmurSettings;
  microphones?: MicrophoneInfo[];
  captureHotkey?: () => Promise<HotkeyDefinition | null>;
  update?: (params: MurmurSettings) => MurmurSettings | Promise<MurmurSettings>;
  /** What models.getChoice answers, and setChoice echoes with the new selection. Null (the default): no answer. */
  choice?: ModelChoice | null;
}

/** The server: update stores and echoes by default. */
function mockServer({ settings = stored, microphones = [usb, headset], captureHotkey, update, choice: modelChoice = null }: ServerOptions = {}) {
  let current = settings;
  vi.mocked(apiClient.call).mockImplementation(async (service, method, params) => {
    const key = `${service}.${method}`;
    if (key === "settings.get") return current;
    if (key === "settings.update") {
      current = update ? await update(params as MurmurSettings) : (params as MurmurSettings);
      return current;
    }
    if (key === "settings.getMicrophones") return microphones;
    if (key === "settings.captureHotkey") return captureHotkey ? captureHotkey() : null;
    if (key === "models.getChoice") return modelChoice;
    if (key === "models.setChoice" && modelChoice) {
      return { ...modelChoice, selected: (params as { model: ModelChoice["selected"] }).model };
    }
    return null;
  });
}

function callsTo(key: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => `${service}.${method}` === key);
}

function lastUpdate(): MurmurSettings {
  const calls = callsTo("settings.update");
  return calls[calls.length - 1][2] as MurmurSettings;
}

async function renderLoaded(options: ServerOptions = {}, engineStatus: EngineStatus | null = ready) {
  mockServer(options);
  const view = render(<SettingsPage engineStatus={engineStatus} />);
  await screen.findByRole("button", { name: "Change" });
  // The microphone list arrives separately from the settings.
  const microphones = options.microphones ?? [usb, headset];
  if (microphones.length > 0) await screen.findByRole("option", { name: microphones[0].name });
  else await screen.findByText("No microphones found");
  return view;
}

/** Lets the speech model choice's 1 s wait pass; the clock only runs fake from here on, so loading stays real. */
async function waitOutModelChoice() {
  await act(async () => { await vi.advanceTimersByTimeAsync(1000); });
}

function radio(name: string) {
  return screen.getByRole("radio", { name }) as HTMLInputElement;
}

function toggle(name: string) {
  return screen.getByRole("switch", { name }) as HTMLInputElement;
}

describe("SettingsPage", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.on).mockClear();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe("hotkey", () => {
    it("shows the configured hotkey as a key cap with a Change button", async () => {
      await renderLoaded();

      expect(screen.getByRole("heading", { name: "Settings" })).toBeTruthy();
      expect(screen.getByText("Right Alt")).toBeTruthy();
      expect((screen.getByRole("button", { name: "Change" }) as HTMLButtonElement).disabled).toBe(false);
    });

    it("waits for the key press with the button disabled, then saves the whole document", async () => {
      let press: (key: HotkeyDefinition | null) => void = () => {};
      await renderLoaded({ captureHotkey: () => new Promise((resolve) => { press = resolve; }) });

      fireEvent.click(screen.getByRole("button", { name: "Change" }));

      const waiting = (await screen.findByRole("button", { name: "Press the key you want… (Esc to cancel)" })) as HTMLButtonElement;
      expect(waiting.disabled).toBe(true);
      expect(callsTo("settings.captureHotkey")).toHaveLength(1);

      await act(async () => { press(f9); });

      expect(await screen.findByText("Ctrl + F9")).toBeTruthy();
      expect(lastUpdate()).toEqual({ ...stored, hotkey: f9 });
      const change = screen.getByRole("button", { name: "Change" }) as HTMLButtonElement;
      expect(change.disabled).toBe(false);
    });

    it("leaves the hotkey alone and saves nothing when the capture is cancelled", async () => {
      let press: (key: HotkeyDefinition | null) => void = () => {};
      await renderLoaded({ captureHotkey: () => new Promise((resolve) => { press = resolve; }) });

      fireEvent.click(screen.getByRole("button", { name: "Change" }));
      await screen.findByRole("button", { name: /Press the key you want/ });
      await act(async () => { press(null); });

      expect(await screen.findByRole("button", { name: "Change" })).toBeTruthy();
      expect(screen.getByText("Right Alt")).toBeTruthy();
      expect(callsTo("settings.update")).toHaveLength(0);
      expect(screen.queryByRole("alert")).toBeNull();
    });

    it("ignores a key press that arrives after leaving the page", async () => {
      let press: (key: HotkeyDefinition | null) => void = () => {};
      const { unmount } = await renderLoaded({ captureHotkey: () => new Promise((resolve) => { press = resolve; }) });

      fireEvent.click(screen.getByRole("button", { name: "Change" }));
      await screen.findByRole("button", { name: /Press the key you want/ });
      unmount();
      await act(async () => { press(f9); });

      expect(callsTo("settings.update")).toHaveLength(0);
    });

    it("keeps changes made during the capture", async () => {
      let press: (key: HotkeyDefinition | null) => void = () => {};
      await renderLoaded({ captureHotkey: () => new Promise((resolve) => { press = resolve; }) });

      fireEvent.click(screen.getByRole("button", { name: "Change" }));
      await screen.findByRole("button", { name: /Press the key you want/ });
      fireEvent.click(toggle("Start with Windows"));
      await waitFor(() => expect(toggle("Start with Windows").checked).toBe(true));
      await act(async () => { press(f9); });

      await screen.findByText("Ctrl + F9");
      expect(lastUpdate()).toEqual({ ...stored, startWithWindows: true, hotkey: f9 });
    });

    it("shows an inline error when the hotkey cannot be saved", async () => {
      await renderLoaded({
        captureHotkey: async () => f9,
        update: () => { throw new Error("read-only"); },
      });

      fireEvent.click(screen.getByRole("button", { name: "Change" }));

      expect((await screen.findByRole("alert")).textContent).toMatch(/could not save the settings/i);
      expect(screen.getByText("Right Alt")).toBeTruthy();
    });
  });

  describe("microphone", () => {
    it("offers Windows default first, then the microphones", async () => {
      await renderLoaded();

      const select = screen.getByLabelText("Microphone") as HTMLSelectElement;
      const options = within(select).getAllByRole("option").map((o) => [o.textContent, (o as HTMLOptionElement).value]);
      expect(options).toEqual([["Windows default", ""], ["USB Mic", "usb"], ["Headset", "headset"]]);
      expect(select.value).toBe("");
      expect(screen.queryByText("No microphones found")).toBeNull();
    });

    it("saves the chosen microphone with every other field", async () => {
      await renderLoaded();

      fireEvent.change(screen.getByLabelText("Microphone"), { target: { value: "headset" } });

      await waitFor(() => expect((screen.getByLabelText("Microphone") as HTMLSelectElement).value).toBe("headset"));
      expect(lastUpdate()).toEqual({ ...stored, microphoneId: "headset" });
    });

    it("saves null when Windows default is chosen again", async () => {
      await renderLoaded({ settings: { ...stored, microphoneId: "usb" } });
      expect((screen.getByLabelText("Microphone") as HTMLSelectElement).value).toBe("usb");

      fireEvent.change(screen.getByLabelText("Microphone"), { target: { value: "" } });

      await waitFor(() => expect(callsTo("settings.update")).toHaveLength(1));
      expect(lastUpdate()).toEqual({ ...stored, microphoneId: null });
    });

    it("refreshes the list when the select gets focus", async () => {
      await renderLoaded();

      fireEvent.focus(screen.getByLabelText("Microphone"));

      await waitFor(() => expect(callsTo("settings.getMicrophones")).toHaveLength(2));
    });

    it("still offers Windows default and says so when there are no microphones", async () => {
      await renderLoaded({ microphones: [] });

      const select = screen.getByLabelText("Microphone");
      expect(within(select).getAllByRole("option").map((o) => o.textContent)).toEqual(["Windows default"]);
      expect(await screen.findByText("No microphones found")).toBeTruthy();
    });

    it("keeps a saved microphone that is no longer listed, as an unavailable option", async () => {
      await renderLoaded({ settings: { ...stored, microphoneId: "gone" }, microphones: [usb] });

      const select = screen.getByLabelText("Microphone") as HTMLSelectElement;
      await waitFor(() =>
        expect(within(select).getAllByRole("option").map((o) => o.textContent)).toEqual([
          "Windows default",
          "USB Mic",
          "Unavailable microphone",
        ]),
      );
      expect(select.value).toBe("gone");
      expect(callsTo("settings.update")).toHaveLength(0);
    });

    it("saves the other settings without touching an unavailable microphone", async () => {
      await renderLoaded({ settings: { ...stored, microphoneId: "gone" }, microphones: [] });

      fireEvent.click(toggle("Sound cues"));

      await waitFor(() => expect(callsTo("settings.update")).toHaveLength(1));
      expect(lastUpdate()).toEqual({ ...stored, microphoneId: "gone", soundCues: false });
    });
  });

  describe("toggles", () => {
    it("shows the three switches in their saved state", async () => {
      await renderLoaded();

      expect(toggle("Sound cues").checked).toBe(true);
      expect(toggle("Start with Windows").checked).toBe(false);
      expect(toggle("Keep audio recordings").checked).toBe(true);
    });

    it.each([
      ["Sound cues", { soundCues: false }],
      ["Start with Windows", { startWithWindows: true }],
      ["Keep audio recordings", { keepAudio: false }],
    ])("%s saves the whole document with only that field changed", async (name, change) => {
      await renderLoaded();

      fireEvent.click(toggle(name));

      await waitFor(() => expect(callsTo("settings.update")).toHaveLength(1));
      expect(lastUpdate()).toEqual({ ...stored, ...change });
      await waitFor(() => expect(toggle(name).checked).toBe(Object.values(change)[0]));
    });

    it("two quick changes both survive", async () => {
      let release: () => void = () => {};
      const firstSave = new Promise<void>((resolve) => { release = resolve; });
      let saves = 0;
      await renderLoaded({
        update: async (params) => {
          if (++saves === 1) await firstSave;
          return params;
        },
      });

      fireEvent.click(toggle("Sound cues"));
      fireEvent.click(toggle("Start with Windows"));
      release();

      await waitFor(() => expect(callsTo("settings.update")).toHaveLength(2));
      expect(lastUpdate()).toEqual({ ...stored, soundCues: false, startWithWindows: true });
      await waitFor(() => expect(toggle("Start with Windows").checked).toBe(true));
      expect(toggle("Sound cues").checked).toBe(false);
    });

    it("shows an inline error and keeps the switch where it was when the save fails", async () => {
      await renderLoaded({ update: () => { throw new Error("read-only"); } });

      fireEvent.click(toggle("Sound cues"));

      expect((await screen.findByRole("alert")).textContent).toMatch(/could not save the settings/i);
      expect(toggle("Sound cues").checked).toBe(true);
    });
  });

  describe("engine card", () => {
    it("names the model from the engine status", async () => {
      await renderLoaded();

      expect(screen.getByRole("heading", { name: "Whisper large-v3-turbo" })).toBeTruthy();
    });

    it("shows no model name while it is empty, as before the first model is chosen", async () => {
      await renderLoaded({}, engine({ state: "notStarted", modelName: "", runtime: null }));

      expect(screen.queryByRole("heading", { name: "Whisper large-v3-turbo" })).toBeNull();
      expect(screen.queryByRole("heading", { level: 2, name: "" })).toBeNull();
      expect(screen.getByText("Loading model…")).toBeTruthy();
    });

    it("shows a progress bar, the percentage and the size, speed and time left while downloading", async () => {
      const download = { ...smallDownload, modelName: "Whisper large-v3-turbo", bytesReceived: 672_000_000, totalBytes: 1_600_000_000 };
      await renderLoaded({}, engine({ state: "downloading", runtime: null, download }));

      expect(screen.getByText("Downloading speech model… 42%")).toBeTruthy();
      expect(screen.getByText("672 MB of 1.6 GB · 18 MB/s · less than a minute left")).toBeTruthy();
      const bar = screen.getByRole("progressbar");
      expect(bar.getAttribute("aria-valuenow")).toBe("42");
    });

    it("says Reconnecting… once while the first download reconnects", async () => {
      const download = { ...smallDownload, retries: 2, reconnecting: true };
      await renderLoaded({}, engine({ state: "downloading", runtime: null, download }));

      expect(screen.getAllByText(/Reconnecting…/)).toHaveLength(1);
      expect(screen.getByText("Reconnecting…")).toBeTruthy();
      expect(screen.getByText("122 MB of 488 MB")).toBeTruthy();
    });

    it.each(["loading", "notStarted"] as const)("says Loading model… when %s", async (state) => {
      await renderLoaded({}, engine({ state, runtime: null }));

      expect(screen.getByText("Loading model…")).toBeTruthy();
      expect(screen.queryByRole("progressbar")).toBeNull();
    });

    it.each([
      ["Cuda", "CUDA"],
      ["Vulkan", "Vulkan"],
      ["Cpu", "CPU"],
    ])("shows Ready and the %s runtime, with no CUDA tip when there is no advice", async (runtime, label) => {
      await renderLoaded({}, engine({ state: "ready", runtime }));

      expect(screen.getByText(`Ready · ${label}`)).toBeTruthy();
      expect(screen.queryByText(/without GPU acceleration/)).toBeNull();
      expect(screen.queryByRole("button", { name: "Get CUDA 13 ↗" })).toBeNull();
    });

    it("shows the CUDA tip of the settings variant when the engine advises installing CUDA", async () => {
      const gpu = { state: "installCuda", gpuName: "NVIDIA GeForce RTX 3060" } as const;
      await renderLoaded({}, engine({ state: "ready", runtime: "Vulkan", gpu }));

      expect(screen.getByText("Ready · Vulkan")).toBeTruthy();
      expect(
        screen.getByText("CUDA isn't installed. Your NVIDIA GeForce RTX 3060 can run Murmur about twice as fast with CUDA 13."),
      ).toBeTruthy();
      expect(screen.getByRole("button", { name: "Get CUDA 13 ↗" })).toBeTruthy();

      fireEvent.click(screen.getByRole("button", { name: "Restart Murmur" }));
      await waitFor(() => expect(callsTo("app.restart")).toHaveLength(1));
    });

    it("shows no CUDA tip when CUDA is in use", async () => {
      const gpu = { state: "cudaInUse", gpuName: "NVIDIA GeForce RTX 3060" } as const;
      await renderLoaded({}, engine({ state: "ready", runtime: "Cuda", gpu }));

      expect(screen.getByText("Ready · CUDA")).toBeTruthy();
      expect(screen.queryByRole("button", { name: "Restart Murmur" })).toBeNull();
    });

    it("shows the error text in the error color", async () => {
      await renderLoaded({}, engine({ state: "error", runtime: null, error: "Out of memory" }));

      const text = screen.getByText("Out of memory");
      expect(text.className).toMatch(/error/);
    });

    it("shows no status line until the engine status is known", async () => {
      await renderLoaded({}, null);

      expect(screen.queryByRole("heading", { name: "Whisper large-v3-turbo" })).toBeNull();
      expect(screen.queryByText(/model…/)).toBeNull();
      expect(screen.queryByText(/^Ready/)).toBeNull();
    });
  });

  describe("speech model", () => {
    it("lists the models as radio buttons, with the selected one checked and the recommended one marked", async () => {
      await renderLoaded({ choice });

      const best = (await screen.findByRole("radio", { name: BEST_RECOMMENDED })) as HTMLInputElement;
      expect(best.checked).toBe(true);
      expect((screen.getByRole("radio", { name: FAST }) as HTMLInputElement).checked).toBe(false);
      expect(screen.getAllByRole("radio")).toHaveLength(2);
    });

    it("marks the recommended model even when another one is selected", async () => {
      await renderLoaded({ choice: { ...choice, selected: "smallEn" } });

      expect(((await screen.findByRole("radio", { name: FAST })) as HTMLInputElement).checked).toBe(true);
      expect((screen.getByRole("radio", { name: BEST_RECOMMENDED }) as HTMLInputElement).checked).toBe(false);
    });

    it("shows the other model selected at once, and asks for it 1 s later", async () => {
      await renderLoaded({ choice });
      await screen.findByRole("radio", { name: FAST });
      vi.useFakeTimers({ shouldAdvanceTime: true });

      fireEvent.click(radio(FAST));

      expect(radio(FAST).checked).toBe(true);
      expect(radio(BEST_RECOMMENDED).checked).toBe(false);
      expect(callsTo("models.setChoice")).toHaveLength(0);

      await waitOutModelChoice();

      expect(apiClient.call).toHaveBeenCalledWith("models", "setChoice", { model: "smallEn" });
      expect(radio(FAST).checked).toBe(true);
      expect(radio(BEST_RECOMMENDED).checked).toBe(false);
      expect(screen.queryByRole("alert")).toBeNull();
    });

    it("a failed setChoice shows the alert and restores the saved choice", async () => {
      await renderLoaded({ choice });
      await screen.findByRole("radio", { name: FAST });
      const serve = vi.mocked(apiClient.call).getMockImplementation()!;
      vi.mocked(apiClient.call).mockImplementation(async (service, method, params) => {
        if (`${service}.${method}` === "models.setChoice") throw new Error("busy");
        return serve(service, method, params);
      });
      vi.useFakeTimers({ shouldAdvanceTime: true });

      fireEvent.click(radio(FAST));
      expect(radio(FAST).checked).toBe(true);
      expect(screen.queryByRole("alert")).toBeNull();
      await waitOutModelChoice();

      expect(screen.getByRole("alert").textContent).toBe("Could not change the speech model (busy).");
      expect(radio(BEST_RECOMMENDED).checked).toBe(true);
      expect(radio(FAST).checked).toBe(false);
    });

    it("sends only the last of several quick changes", async () => {
      await renderLoaded({ choice });
      await screen.findByRole("radio", { name: FAST });
      vi.useFakeTimers({ shouldAdvanceTime: true });

      fireEvent.click(radio(FAST));
      await act(async () => { await vi.advanceTimersByTimeAsync(600); });
      fireEvent.click(radio(BEST_RECOMMENDED));
      await act(async () => { await vi.advanceTimersByTimeAsync(600); });
      fireEvent.click(radio(FAST));
      await act(async () => { await vi.advanceTimersByTimeAsync(600); });
      expect(callsTo("models.setChoice")).toHaveLength(0);

      await act(async () => { await vi.advanceTimersByTimeAsync(400); });

      expect(callsTo("models.setChoice")).toHaveLength(1);
      expect(callsTo("models.setChoice")[0][2]).toEqual({ model: "smallEn" });
    });

    it("does not call setChoice for the model that is already selected", async () => {
      await renderLoaded({ choice });

      fireEvent.click(await screen.findByRole("radio", { name: BEST_RECOMMENDED }));

      expect(callsTo("models.setChoice")).toHaveLength(0);
    });

    it("shows nothing for the model choice until it is known", async () => {
      await renderLoaded();

      expect(screen.queryByRole("radio")).toBeNull();
    });

    it("shows the progress of a model switch under the choices while the old model stays ready", async () => {
      await renderLoaded({ choice }, engine({ state: "ready", runtime: "Cuda", download: smallDownload }));
      await screen.findByRole("radio", { name: FAST });

      expect(screen.getByText("Ready · CUDA")).toBeTruthy();
      expect(screen.getByText("Downloading Whisper small (English)")).toBeTruthy();
      expect(screen.getByText("122 MB of 488 MB · 18 MB/s · less than a minute left")).toBeTruthy();
      expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("25");
    });

    it("shows Reconnecting… in the switch progress line", async () => {
      const download = { ...smallDownload, retries: 1, reconnecting: true };
      await renderLoaded({ choice }, engine({ state: "ready", runtime: "Cuda", download }));
      await screen.findByRole("radio", { name: FAST });

      expect(screen.getByText("122 MB of 488 MB · Reconnecting…")).toBeTruthy();
    });

    it("shows no switch progress line when nothing is downloading", async () => {
      await renderLoaded({ choice });
      await screen.findByRole("radio", { name: FAST });

      expect(screen.queryByText(/^Downloading/)).toBeNull();
      expect(screen.queryByRole("progressbar")).toBeNull();
    });

    it("says why a model switch failed while the old model stays ready", async () => {
      const error = "Could not switch to Whisper small (English): disk full";
      await renderLoaded({ choice }, engine({ state: "ready", runtime: "Cuda", error }));
      await screen.findByRole("radio", { name: FAST });

      expect((await screen.findByRole("alert")).textContent).toBe(error);
    });

    it("reads the choice again when the engine is ready after a switch, in case a failed switch put the old model back", async () => {
      await renderLoaded({ choice });
      await screen.findByRole("radio", { name: FAST });
      vi.useFakeTimers({ shouldAdvanceTime: true });
      fireEvent.click(radio(FAST));
      await waitOutModelChoice();
      expect(radio(FAST).checked).toBe(true);
      const readsBefore = callsTo("models.getChoice").length;

      // The engine reports the failed switch: ready on the old model again. The server's choice is back to large.
      vi.mocked(apiClient.call).mockImplementation(async (service, method) =>
        `${service}.${method}` === "models.getChoice" ? choice : null,
      );
      const pushed = vi.mocked(apiClient.on).mock.calls
        .filter(([name]) => name === "engine.statusChanged")
        .map(([, callback]) => callback as (status: EngineStatus) => void);
      act(() => pushed.forEach((callback) => callback(engine({ state: "ready", runtime: "Cuda" }))));

      await waitFor(() => expect(callsTo("models.getChoice").length).toBeGreaterThan(readsBefore));
      await waitFor(() => expect(radio(BEST_RECOMMENDED).checked).toBe(true));
    });
  });

  it("shows an error when the settings cannot be loaded", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));

    render(<SettingsPage engineStatus={ready} />);

    expect((await screen.findByRole("alert")).textContent).toMatch(/bridge down/);
    expect(screen.queryByRole("button", { name: "Change" })).toBeNull();
  });
});
