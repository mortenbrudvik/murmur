import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import SettingsPage from "../SettingsPage";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { EngineStatus, HotkeyDefinition, MicrophoneInfo, MurmurSettings } from "../../../types/domain";

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

const ready: EngineStatus = { state: "ready", modelName: "large-v3-turbo", runtime: "Cuda", downloadProgress: null, error: null };

function engine(overrides: Partial<EngineStatus>): EngineStatus {
  return { ...ready, ...overrides };
}

interface ServerOptions {
  settings?: MurmurSettings;
  microphones?: MicrophoneInfo[];
  captureHotkey?: () => Promise<HotkeyDefinition | null>;
  update?: (params: MurmurSettings) => MurmurSettings | Promise<MurmurSettings>;
}

/** The server: update stores and echoes by default. */
function mockServer({ settings = stored, microphones = [usb, headset], captureHotkey, update }: ServerOptions = {}) {
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

function toggle(name: string) {
  return screen.getByRole("switch", { name }) as HTMLInputElement;
}

describe("SettingsPage", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
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
    it("names the model", async () => {
      await renderLoaded();

      expect(screen.getByRole("heading", { name: "Whisper large-v3-turbo" })).toBeTruthy();
    });

    it("shows a progress bar and the percentage while downloading", async () => {
      await renderLoaded({}, engine({ state: "downloading", runtime: null, downloadProgress: 0.42 }));

      expect(screen.getByText("Downloading model… 42%")).toBeTruthy();
      const bar = screen.getByRole("progressbar");
      expect(bar.getAttribute("aria-valuenow")).toBe("42");
    });

    it.each(["loading", "notStarted"] as const)("says Loading model… when %s", async (state) => {
      await renderLoaded({}, engine({ state, runtime: null }));

      expect(screen.getByText("Loading model…")).toBeTruthy();
      expect(screen.queryByRole("progressbar")).toBeNull();
    });

    it("shows Ready and the runtime, with no GPU note on CUDA", async () => {
      await renderLoaded({}, engine({ state: "ready", runtime: "Cuda" }));

      expect(screen.getByText("Ready · Cuda")).toBeTruthy();
      expect(screen.queryByText(/without GPU acceleration/)).toBeNull();
    });

    it.each(["Vulkan", "Cpu"])("explains the missing GPU acceleration when ready on %s", async (runtime) => {
      await renderLoaded({}, engine({ state: "ready", runtime }));

      expect(screen.getByText(`Ready · ${runtime}`)).toBeTruthy();
      expect(screen.getByText("Running without GPU acceleration — install CUDA 13 for faster transcription.")).toBeTruthy();
    });

    it("shows the error text in the error color", async () => {
      await renderLoaded({}, engine({ state: "error", runtime: null, error: "Out of memory" }));

      const text = screen.getByText("Out of memory");
      expect(text.className).toMatch(/error/);
      expect(screen.queryByText(/without GPU acceleration/)).toBeNull();
    });

    it("shows no status line until the engine status is known", async () => {
      await renderLoaded({}, null);

      expect(screen.getByRole("heading", { name: "Whisper large-v3-turbo" })).toBeTruthy();
      expect(screen.queryByText(/model…/)).toBeNull();
      expect(screen.queryByText(/^Ready/)).toBeNull();
    });
  });

  it("shows an error when the settings cannot be loaded", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));

    render(<SettingsPage engineStatus={ready} />);

    expect((await screen.findByRole("alert")).textContent).toMatch(/bridge down/);
    expect(screen.queryByRole("button", { name: "Change" })).toBeNull();
  });
});
