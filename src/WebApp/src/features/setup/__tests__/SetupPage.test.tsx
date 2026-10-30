import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import SetupPage from "../SetupPage";
import type { useSetupState } from "../useSetupState";
import { apiClient } from "../../../interop/WebViewApiClient";
import type {
  DownloadProgress,
  EngineStatus,
  GpuAdvice,
  HistoryEntry,
  MicrophoneInfo,
  MicTestResult,
  MurmurSettings,
  SetupState,
} from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

type Update = ReturnType<typeof useSetupState>["update"];

const stored: MurmurSettings = {
  hotkey: { virtualKey: 163, modifiers: "None", displayName: "Right Ctrl" },
  microphoneId: null,
  soundCues: true,
  startWithWindows: false,
  keepAudio: false,
};
const usb: MicrophoneInfo = { id: "usb", name: "USB Mic", isDefault: true };
const headset: MicrophoneInfo = { id: "headset", name: "Headset", isDefault: false };

const listening: MicTestResult = { state: "listening", error: null };

const firstStep: SetupState = { showSetup: true, step: "speechModel", cudaTipDismissed: false, openPage: null };
const microphoneStep: SetupState = { ...firstStep, step: "microphone" };
const tryItStep: SetupState = { ...firstStep, step: "tryIt" };

const download: DownloadProgress = {
  modelName: "Whisper large-v3-turbo",
  bytesReceived: 612_000_000,
  totalBytes: 1_600_000_000,
  bytesPerSecond: 18_000_000,
  secondsRemaining: 56,
  retries: 0,
  reconnecting: false,
};
const ready: EngineStatus = { state: "ready", modelName: "Whisper large-v3-turbo", runtime: "Vulkan", error: null, download: null, gpu: null, switching: false };
const downloading: EngineStatus = { ...ready, state: "downloading", runtime: null, download };
const loading: EngineStatus = { ...ready, state: "loading", runtime: null };
const installCuda: GpuAdvice = { state: "installCuda", gpuName: "NVIDIA GeForce RTX 3060" };

const entry: HistoryEntry = { id: "1", timestamp: "2026-10-30T10:00:00Z", text: "Hello Murmur", targetApp: "notepad", pasted: true };

interface ServerOptions {
  settings?: MurmurSettings;
  microphones?: MicrophoneInfo[];
  /** What startMicTest answers; a function is asked at every start, for a test that goes differently the second time. */
  micTest?: MicTestResult | (() => MicTestResult);
  updateSettings?: (params: MurmurSettings) => MurmurSettings;
}

/** The server for the calls the page makes; anything else answers null. */
function mockServer({ settings = stored, microphones = [usb, headset], micTest = listening, updateSettings }: ServerOptions = {}) {
  let current = settings;
  vi.mocked(apiClient.call).mockImplementation(async (service, method, params) => {
    const key = `${service}.${method}`;
    if (key === "settings.get") return current;
    if (key === "settings.update") {
      current = updateSettings ? updateSettings(params as MurmurSettings) : (params as MurmurSettings);
      return current;
    }
    if (key === "settings.getMicrophones") return microphones;
    if (key === "setup.startMicTest") return typeof micTest === "function" ? micTest() : micTest;
    return null;
  });
}

function callsTo(key: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => `${service}.${method}` === key);
}

/** The setup.* mic test calls so far, in the order they were made. */
function micTestCalls(): string[] {
  return vi.mocked(apiClient.call).mock.calls.flatMap(([service, method, params]) => {
    if (service !== "setup") return [];
    if (method === "startMicTest") return [`start:${(params as { microphoneId: string | null }).microphoneId}`];
    if (method === "stopMicTest") return ["stop"];
    return [];
  });
}

/** Delivers a push event to everything that subscribed to it. */
function push<T>(eventName: string, data: T) {
  const callbacks = vi.mocked(apiClient.on).mock.calls
    .filter(([name]) => name === eventName)
    .map(([, callback]) => callback as (data: T) => void);
  expect(callbacks.length).toBeGreaterThan(0);
  act(() => callbacks.forEach((callback) => callback(data)));
}

/** A paragraph by its whole text, also when parts of it (like the key) are elements of their own. */
function paragraph(text: string) {
  return screen.getByText((_, element) => element?.tagName === "P" && element.textContent === text);
}

function renderPage({
  state = firstStep,
  engineStatus = downloading,
  update = vi.fn<Update>().mockResolvedValue(undefined),
  onDone = vi.fn(),
}: { state?: SetupState; engineStatus?: EngineStatus | null; update?: Update; onDone?: () => void } = {}) {
  const view = render(<SetupPage state={state} engineStatus={engineStatus} update={update} onDone={onDone} />);
  return { ...view, update, onDone };
}

describe("SetupPage", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.on).mockClear();
    mockServer();
  });

  describe("steps", () => {
    it("starts at the saved step", async () => {
      renderPage({ state: microphoneStep });

      expect(await screen.findByRole("heading", { name: "Check your microphone" })).toBeTruthy();
      expect(screen.queryByRole("heading", { name: "Getting Murmur ready" })).toBeNull();
    });

    it("shows the first step's chip as current", () => {
      renderPage();

      expect(screen.getByText("1 · Speech model")).toBeTruthy();
      expect(screen.getByText("2 · Microphone")).toBeTruthy();
      expect(screen.getByText("3 · Try it")).toBeTruthy();
      expect(screen.getByText("1 · Speech model").getAttribute("aria-current")).toBe("step");
    });

    it("marks the finished steps with a check", async () => {
      const { unmount } = renderPage({ state: microphoneStep });
      expect(screen.getByText("✓ Speech model")).toBeTruthy();
      expect(screen.getByText("2 · Microphone").getAttribute("aria-current")).toBe("step");
      expect(screen.getByText("3 · Try it").getAttribute("aria-current")).toBeNull();
      await screen.findByText("✓ Microphone access allowed");
      unmount();
      await waitFor(() => expect(micTestCalls()).toContain("stop"));

      renderPage({ state: tryItStep, engineStatus: ready });
      expect(screen.getByText("✓ Speech model")).toBeTruthy();
      expect(screen.getByText("✓ Microphone")).toBeTruthy();
      expect(screen.getByText("3 · Try it").getAttribute("aria-current")).toBe("step");
    });
  });

  describe("keyboard focus", () => {
    it("leaves the focus alone on the first render of the page", async () => {
      renderPage({ state: microphoneStep });
      await screen.findByText("✓ Microphone access allowed");

      expect(document.activeElement).toBe(document.body);
    });

    it("Next moves focus to the new step's heading", async () => {
      const update = vi.fn<Update>().mockResolvedValue(undefined);
      const view = renderPage({ update });
      const next = screen.getByRole("button", { name: "Next" });
      next.focus();
      expect(document.activeElement).toBe(next);

      fireEvent.click(next);
      view.rerender(<SetupPage state={microphoneStep} engineStatus={downloading} update={update} onDone={() => undefined} />);

      const microphone = await screen.findByRole("heading", { name: "Check your microphone" });
      expect(document.activeElement).toBe(microphone);
      expect(microphone.getAttribute("tabindex")).toBe("-1");

      fireEvent.click(screen.getByRole("button", { name: "Next" }));
      view.rerender(<SetupPage state={tryItStep} engineStatus={ready} update={update} onDone={() => undefined} />);

      const tryIt = await screen.findByRole("heading", { name: "Try it" });
      expect(document.activeElement).toBe(tryIt);
      expect(tryIt.getAttribute("tabindex")).toBe("-1");
    });

    it("does not move the focus again when only the engine status changes", async () => {
      const update = vi.fn<Update>().mockResolvedValue(undefined);
      const view = renderPage({ state: tryItStep, engineStatus: loading, update });
      const done = screen.getByRole("button", { name: "Done" });
      done.focus();

      view.rerender(<SetupPage state={tryItStep} engineStatus={ready} update={update} onDone={() => undefined} />);

      expect(document.activeElement).toBe(done);
    });
  });

  describe("step 1, the speech model", () => {
    it("explains what is needed and how big it is", () => {
      renderPage();

      expect(screen.getByRole("heading", { name: "Getting Murmur ready" })).toBeTruthy();
      expect(paragraph("Murmur turns speech into text on this PC. It needs its speech model once — about 1.6 GB.")).toBeTruthy();
    });

    it("leaves the size out when it is not known", () => {
      renderPage({ engineStatus: null });

      expect(paragraph("Murmur turns speech into text on this PC. It needs its speech model once.")).toBeTruthy();
    });

    it("shows download amount, speed and time left", () => {
      renderPage();

      expect(screen.getByText("Downloading speech model")).toBeTruthy();
      expect(screen.getByText("612 MB of 1.6 GB")).toBeTruthy();
      expect(screen.getByText("18 MB/s · less than a minute left")).toBeTruthy();
      expect(screen.getByText("Resumes if interrupted")).toBeTruthy();
      const bar = screen.getByRole("progressbar");
      expect(bar.getAttribute("aria-valuenow")).toBe("38");
    });

    it("says Reconnecting… while the download reconnects", () => {
      renderPage({ engineStatus: { ...downloading, download: { ...download, reconnecting: true, retries: 2 } } });

      expect(screen.getByText("Reconnecting…")).toBeTruthy();
    });

    it("shows the empty bar when the download has not reported yet", () => {
      renderPage({ engineStatus: { ...downloading, download: null } });

      expect(screen.getByText("Downloading speech model")).toBeTruthy();
      expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("0");
    });

    it("says the model is loading while it loads, and also before the engine has reported", () => {
      const { unmount } = renderPage({ engineStatus: loading });
      expect(screen.getByText("Loading the speech model…")).toBeTruthy();
      expect(screen.queryByRole("progressbar")).toBeNull();
      unmount();

      renderPage({ engineStatus: null });
      expect(screen.getByText("Loading the speech model…")).toBeTruthy();
    });

    it("says when the model is ready", () => {
      renderPage({ engineStatus: ready });

      expect(screen.getByText("✓ Speech model ready")).toBeTruthy();
      expect(screen.queryByText("Downloading speech model")).toBeNull();
    });

    it("shows why the model failed", () => {
      renderPage({ engineStatus: { ...ready, state: "error", runtime: null, error: "Not enough disk space: 1.7 GB needed" } });

      expect(screen.getByRole("alert").textContent).toBe("Not enough disk space: 1.7 GB needed");
    });

    describe("the note that the window can be closed", () => {
      const CLOSE_NOTE = "You can close this window — Murmur keeps downloading.";

      it("is shown while the model downloads", () => {
        renderPage({ engineStatus: downloading });

        expect(screen.getByText(CLOSE_NOTE)).toBeTruthy();
      });

      it("is shown before the engine has reported, and while the download has not started", () => {
        const { unmount } = renderPage({ engineStatus: null });
        expect(screen.getByText(CLOSE_NOTE)).toBeTruthy();
        unmount();

        renderPage({ engineStatus: { ...loading, state: "notStarted" } });
        expect(screen.getByText(CLOSE_NOTE)).toBeTruthy();
      });

      it("is gone once the model is ready", () => {
        renderPage({ engineStatus: ready });

        expect(screen.getByText("✓ Speech model ready")).toBeTruthy();
        expect(screen.queryByText(CLOSE_NOTE)).toBeNull();
        expect(screen.queryByText(/keeps downloading/)).toBeNull();
      });

      it("is gone while the model loads", () => {
        renderPage({ engineStatus: loading });

        expect(screen.queryByText(CLOSE_NOTE)).toBeNull();
      });

      it("is gone when the model failed", () => {
        renderPage({ engineStatus: { ...ready, state: "error", runtime: null, error: "Not enough disk space: 1.7 GB needed" } });

        expect(screen.queryByText(CLOSE_NOTE)).toBeNull();
      });

      it("leaves Next where it is when the note is gone", () => {
        renderPage({ engineStatus: ready });

        const next = screen.getByRole("button", { name: "Next" });
        const footer = next.parentElement as HTMLElement;
        // The note's place stays as an empty first child, so the button keeps its place at the end of the footer.
        expect(footer.children).toHaveLength(2);
        expect(footer.firstElementChild?.textContent).toBe("");
        expect(footer.lastElementChild).toBe(next);
      });
    });

    it("Next on step 1 saves the microphone step", async () => {
      const { update } = renderPage();

      fireEvent.click(screen.getByRole("button", { name: "Next" }));

      expect(update).toHaveBeenCalledWith({ step: "microphone" });
    });

    it("keeps Next enabled while the model is still downloading", () => {
      renderPage();

      expect((screen.getByRole("button", { name: "Next" }) as HTMLButtonElement).disabled).toBe(false);
    });

    it("shows the CUDA tip, and Not now saves that it was dismissed", () => {
      const { update } = renderPage({ engineStatus: { ...downloading, gpu: installCuda } });

      expect(screen.getByRole("button", { name: "Get CUDA 13 ↗" })).toBeTruthy();
      fireEvent.click(screen.getByRole("button", { name: "Not now" }));

      expect(update).toHaveBeenCalledWith({ cudaTipDismissed: true });
    });

    it("hides the CUDA tip once dismissed", () => {
      renderPage({ state: { ...firstStep, cudaTipDismissed: true }, engineStatus: { ...downloading, gpu: installCuda } });

      expect(screen.queryByText(/Get CUDA 13/)).toBeNull();
      expect(screen.queryByRole("button", { name: "Not now" })).toBeNull();
    });

    it("shows no CUDA tip when the graphics card needs none", () => {
      renderPage({ engineStatus: { ...downloading, gpu: { state: "notApplicable", gpuName: null } } });

      expect(screen.queryByText(/CUDA/)).toBeNull();
    });

    it("says so when the progress could not be saved", async () => {
      const update = vi.fn<Update>().mockRejectedValue(new Error("disk full"));
      renderPage({ update });

      fireEvent.click(screen.getByRole("button", { name: "Next" }));

      expect((await screen.findByRole("alert")).textContent).toBe("Could not save your progress (disk full).");
    });
  });

  describe("step 2, the microphone", () => {
    it("asks for the microphone once the settings are known, and shows that access is allowed", async () => {
      renderPage({ state: microphoneStep });

      expect(screen.getByText("Say something — the bars should move.")).toBeTruthy();
      expect(await screen.findByText("✓ Microphone access allowed")).toBeTruthy();
      expect(micTestCalls()).toEqual(["start:null"]);
    });

    it("tests the saved microphone from the start, without trying the default first", async () => {
      mockServer({ settings: { ...stored, microphoneId: "headset" } });

      renderPage({ state: microphoneStep });

      await screen.findByText("✓ Microphone access allowed");
      expect(micTestCalls()).toEqual(["start:headset"]);
    });

    it("tests the default microphone when the settings cannot be read", async () => {
      vi.mocked(apiClient.call).mockImplementation(async (service, method) => {
        if (service === "settings" && method === "get") throw new Error("bridge down");
        if (service === "setup" && method === "startMicTest") return listening;
        return [];
      });

      renderPage({ state: microphoneStep });

      expect(await screen.findByText("✓ Microphone access allowed")).toBeTruthy();
      expect(micTestCalls()).toEqual(["start:null"]);
    });

    it("lists the microphones, with the saved one selected", async () => {
      mockServer({ settings: { ...stored, microphoneId: "headset" } });

      renderPage({ state: microphoneStep });

      const picker = (await screen.findByRole("combobox", { name: "Microphone" })) as HTMLSelectElement;
      await waitFor(() => expect(within(picker).getAllByRole("option").map((o) => o.textContent)).toEqual(["Windows default", "USB Mic", "Headset"]));
      await waitFor(() => expect(picker.value).toBe("headset"));
    });

    it("saves the chosen microphone like Settings does, and tests that one instead", async () => {
      renderPage({ state: microphoneStep });
      const picker = await screen.findByRole("combobox", { name: "Microphone" });
      await screen.findByText("✓ Microphone access allowed");
      await waitFor(() => expect(within(picker).getAllByRole("option")).toHaveLength(3));

      fireEvent.change(picker, { target: { value: "headset" } });

      await waitFor(() => expect(micTestCalls()).toEqual(["start:null", "stop", "start:headset"]));
      expect(callsTo("settings.update")[0][2]).toEqual({ ...stored, microphoneId: "headset" });
    });

    it("announces the microphone status in a polite live region that is there before the answer", async () => {
      renderPage({ state: microphoneStep });
      const region = await screen.findByRole("status");
      expect(region.getAttribute("aria-live")).toBe("polite");

      const allowed = await screen.findByText("✓ Microphone access allowed");

      expect(region.contains(allowed)).toBe(true);
    });

    it("announces that Windows is blocking the microphone, but keeps the buttons out of the live region", async () => {
      mockServer({ micTest: { state: "accessDenied", error: null } });
      renderPage({ state: microphoneStep });

      const blocked = await screen.findByText("Windows is blocking microphone access for Murmur.");

      expect(blocked.closest('[role="status"]')?.getAttribute("aria-live")).toBe("polite");
      expect(screen.getByRole("button", { name: "Check again" }).closest('[role="status"]')).toBeNull();
    });

    it("leaves a failed microphone test to its own alert, outside the live region", async () => {
      mockServer({ micTest: { state: "failed", error: "The microphone was unplugged" } });
      renderPage({ state: microphoneStep });

      const alert = await screen.findByRole("alert");

      expect(alert.closest('[role="status"]')).toBeNull();
      expect(alert.querySelector('[role="status"]')).toBeNull();
    });

    it("draws eight bars that follow the level", async () => {
      renderPage({ state: microphoneStep });
      await screen.findByText("✓ Microphone access allowed");
      const meter = screen.getByRole("meter", { name: "Microphone level" });
      expect(meter.querySelectorAll(".setup-meter-bar")).toHaveLength(8);
      expect(meter.getAttribute("aria-valuenow")).toBe("0");

      push("setup.micLevel", { level: 0.5 });

      expect(meter.getAttribute("aria-valuenow")).toBe("50");
    });

    it("offers microphone settings when access is denied", async () => {
      mockServer({ micTest: { state: "accessDenied", error: null } });
      renderPage({ state: microphoneStep });

      expect(await screen.findByText("Windows is blocking microphone access for Murmur.")).toBeTruthy();
      expect(screen.queryByText("✓ Microphone access allowed")).toBeNull();
      fireEvent.click(screen.getByRole("button", { name: "Open microphone settings" }));

      await waitFor(() => expect(apiClient.call).toHaveBeenCalledWith("app", "openLink", { target: "microphonePrivacy" }));
    });

    it("says so when the microphone settings could not be opened", async () => {
      vi.mocked(apiClient.call).mockImplementation(async (service, method) => {
        if (service === "setup" && method === "startMicTest") return { state: "accessDenied", error: null };
        if (service === "app" && method === "openLink") throw new Error("no shell");
        return service === "settings" && method === "get" ? stored : [];
      });
      renderPage({ state: microphoneStep });

      fireEvent.click(await screen.findByRole("button", { name: "Open microphone settings" }));

      expect((await screen.findByRole("alert")).textContent).toBe("Could not open the link (no shell).");
    });

    it("offers Check again when access is denied or the test failed, but not while listening", async () => {
      const listeningPage = renderPage({ state: microphoneStep });
      await screen.findByText("✓ Microphone access allowed");
      expect(screen.queryByRole("button", { name: "Check again" })).toBeNull();
      listeningPage.unmount();

      mockServer({ micTest: { state: "accessDenied", error: null } });
      const deniedPage = renderPage({ state: microphoneStep });
      expect(await screen.findByRole("button", { name: "Check again" })).toBeTruthy();
      deniedPage.unmount();

      mockServer({ micTest: { state: "failed", error: "The microphone was unplugged" } });
      renderPage({ state: microphoneStep });
      expect(await screen.findByRole("button", { name: "Check again" })).toBeTruthy();
    });

    it("tests again on Check again, and shows access allowed once Windows lets Murmur in", async () => {
      let allowed = false;
      mockServer({ micTest: () => (allowed ? listening : { state: "accessDenied", error: null }) });
      renderPage({ state: microphoneStep });
      fireEvent.click(await screen.findByRole("button", { name: "Check again" }));

      // Still blocked: the page keeps saying so.
      await waitFor(() => expect(micTestCalls()).toEqual(["start:null", "stop", "start:null"]));
      expect(await screen.findByText("Windows is blocking microphone access for Murmur.")).toBeTruthy();

      allowed = true;
      fireEvent.click(screen.getByRole("button", { name: "Check again" }));

      expect(await screen.findByText("✓ Microphone access allowed")).toBeTruthy();
      expect(screen.queryByText("Windows is blocking microphone access for Murmur.")).toBeNull();
      expect(screen.queryByRole("button", { name: "Check again" })).toBeNull();
      expect(micTestCalls()).toEqual(["start:null", "stop", "start:null", "stop", "start:null"]);
    });

    it("tests again when the window gets focus back from Windows' microphone settings", async () => {
      let allowed = false;
      mockServer({ micTest: () => (allowed ? listening : { state: "accessDenied", error: null }) });
      renderPage({ state: microphoneStep });
      fireEvent.click(await screen.findByRole("button", { name: "Open microphone settings" }));
      await waitFor(() => expect(apiClient.call).toHaveBeenCalledWith("app", "openLink", { target: "microphonePrivacy" }));

      allowed = true;
      act(() => {
        window.dispatchEvent(new Event("focus"));
      });

      expect(await screen.findByText("✓ Microphone access allowed")).toBeTruthy();
      expect(screen.queryByText("Windows is blocking microphone access for Murmur.")).toBeNull();
    });

    it("keeps a working microphone open when the window gets focus, and tests again once the test has ended", async () => {
      renderPage({ state: microphoneStep });
      await screen.findByText("✓ Microphone access allowed");
      const meter = screen.getByRole("meter", { name: "Microphone level" });
      push("setup.micLevel", { level: 0.5, ended: false });

      act(() => {
        window.dispatchEvent(new Event("focus"));
      });
      await act(() => new Promise<void>((resolve) => setTimeout(resolve, 20)));
      expect(micTestCalls()).toEqual(["start:null"]);
      expect(meter.getAttribute("aria-valuenow")).toBe("50");

      // The window was minimized, say: the .NET side ended the test.
      push("setup.micLevel", { level: 0, ended: true });
      expect(meter.getAttribute("aria-valuenow")).toBe("0");
      act(() => {
        window.dispatchEvent(new Event("focus"));
      });

      await waitFor(() => expect(micTestCalls()).toEqual(["start:null", "start:null"]));
    });

    it("shows why the microphone test failed", async () => {
      mockServer({ micTest: { state: "failed", error: "The microphone was unplugged" } });
      renderPage({ state: microphoneStep });

      expect((await screen.findByRole("alert")).textContent).toBe("The microphone was unplugged");
      expect(screen.queryByText("✓ Microphone access allowed")).toBeNull();
    });

    it("shows a failure that came without a message", async () => {
      mockServer({ micTest: { state: "failed", error: null } });
      renderPage({ state: microphoneStep });

      expect((await screen.findByRole("alert")).textContent).toBe("The microphone could not be opened.");
    });

    it("always shows the Bluetooth hint and where to look when there are problems", async () => {
      mockServer({ micTest: { state: "accessDenied", error: null } });
      renderPage({ state: microphoneStep });

      expect(
        screen.getByText(
          "Bluetooth headsets take about a second to switch on their microphone — wait for the start sound before you talk.",
        ),
      ).toBeTruthy();
      expect(screen.getByText("Problems? Windows Settings → Privacy → Microphone")).toBeTruthy();
      await screen.findByText("Windows is blocking microphone access for Murmur.");
    });

    it("says when there are no microphones", async () => {
      mockServer({ microphones: [] });
      renderPage({ state: microphoneStep });

      expect(await screen.findByText("No microphones found")).toBeTruthy();
    });

    it("Next on step 2 saves the try-it step", async () => {
      const { update } = renderPage({ state: microphoneStep });

      fireEvent.click(screen.getByRole("button", { name: "Next" }));

      expect(update).toHaveBeenCalledWith({ step: "tryIt" });
      await screen.findByText("✓ Microphone access allowed");
    });

    it("stops the microphone test when leaving step 2", async () => {
      const { unmount } = renderPage({ state: microphoneStep });
      await screen.findByText("✓ Microphone access allowed");

      unmount();

      await waitFor(() => expect(apiClient.call).toHaveBeenCalledWith("setup", "stopMicTest", {}));
      expect(micTestCalls()).toEqual(["start:null", "stop"]);
    });

    it("stops the microphone test when the page moves on to step 3", async () => {
      const update = vi.fn<Update>().mockResolvedValue(undefined);
      const { rerender } = renderPage({ state: microphoneStep, update });
      await screen.findByText("✓ Microphone access allowed");

      rerender(<SetupPage state={tryItStep} engineStatus={ready} update={update} onDone={() => undefined} />);

      await waitFor(() => expect(micTestCalls()).toEqual(["start:null", "stop"]));
    });

    it("does not test the microphone on the other steps", () => {
      renderPage();

      expect(micTestCalls()).toEqual([]);
    });
  });

  describe("step 3, try it", () => {
    it("waits for the speech model", () => {
      renderPage({ state: tryItStep, engineStatus: loading });

      expect(screen.getByRole("heading", { name: "Try it" })).toBeTruthy();
      expect(screen.getByText("Waiting for the speech model…")).toBeTruthy();
      expect(screen.queryByRole("textbox")).toBeNull();
    });

    it("waits while the model downloads, and before the engine has reported", () => {
      const { unmount } = renderPage({ state: tryItStep, engineStatus: downloading });
      expect(screen.getByText("Waiting for the speech model…")).toBeTruthy();
      unmount();

      renderPage({ state: tryItStep, engineStatus: null });
      expect(screen.getByText("Waiting for the speech model…")).toBeTruthy();
    });

    it("still waits, and says why, when the model failed", () => {
      renderPage({ state: tryItStep, engineStatus: { ...ready, state: "error", runtime: null, error: "Model file is corrupt" } });

      expect(screen.getByText("Waiting for the speech model…")).toBeTruthy();
      expect(screen.getByRole("alert").textContent).toBe("Model file is corrupt");
    });

    it("announces the waiting text in a polite live region", () => {
      renderPage({ state: tryItStep, engineStatus: loading });

      const waiting = screen.getByText("Waiting for the speech model…");

      expect(waiting.closest('[role="status"]')?.getAttribute("aria-live")).toBe("polite");
    });

    it("keeps the model's error alert out of the live region", () => {
      renderPage({ state: tryItStep, engineStatus: { ...ready, state: "error", runtime: null, error: "Model file is corrupt" } });

      expect(screen.getByRole("alert").closest('[role="status"]')).toBeNull();
    });

    it("asks for a sentence once the model is ready, with a box to click", () => {
      renderPage({ state: tryItStep, engineStatus: ready });

      expect(screen.queryByText("Waiting for the speech model…")).toBeNull();
      expect(paragraph("Click the box, hold Right Ctrl, say a sentence, and let go.")).toBeTruthy();
      expect(screen.getByRole("textbox", { name: "Practice dictation" })).toBeTruthy();
    });

    it("uses the configured hotkey name", async () => {
      mockServer({ settings: { ...stored, hotkey: { virtualKey: 124, modifiers: "None", displayName: "F13" } } });
      renderPage({ state: tryItStep, engineStatus: ready });

      await waitFor(() => expect(paragraph("Click the box, hold F13, say a sentence, and let go.")).toBeTruthy());
      expect(paragraph("Tap F13 once for hands-free; tap again to finish. Esc cancels. Change the key in Settings.")).toBeTruthy();
    });

    it("explains hands-free, finishing and cancelling", () => {
      renderPage({ state: tryItStep, engineStatus: ready });

      expect(paragraph("Tap Right Ctrl once for hands-free; tap again to finish. Esc cancels. Change the key in Settings.")).toBeTruthy();
    });

    it("does not celebrate before the first dictation", () => {
      renderPage({ state: tryItStep, engineStatus: ready });

      expect(screen.queryByText("✓ That's it — Murmur types wherever your cursor is.")).toBeNull();
    });

    it("celebrates the first dictation", async () => {
      renderPage({ state: tryItStep, engineStatus: ready });
      // Let the hotkey name arrive first, so that nothing but the event changes the page below.
      await act(async () => undefined);

      push("history.added", entry);

      expect(screen.getByText("✓ That's it — Murmur types wherever your cursor is.")).toBeTruthy();
    });

    it("announces the first dictation in a polite live region that was already there", async () => {
      renderPage({ state: tryItStep, engineStatus: ready });
      await act(async () => undefined);
      const region = screen.getByRole("status");
      expect(region.getAttribute("aria-live")).toBe("polite");
      expect(region.textContent).toBe("");

      push("history.added", entry);

      expect(screen.getByRole("status")).toBe(region);
      expect(region.textContent).toBe("✓ That's it — Murmur types wherever your cursor is.");
    });

    it("says where Murmur lives afterwards", () => {
      renderPage({ state: tryItStep, engineStatus: ready });

      expect(screen.getByText("Murmur lives in the tray from now on.")).toBeTruthy();
    });

    it("Done completes setup", async () => {
      const calls: string[] = [];
      const update = vi.fn<Update>().mockImplementation(async () => {
        calls.push("update");
      });
      const onDone = vi.fn(() => {
        calls.push("done");
      });
      renderPage({ state: tryItStep, engineStatus: ready, update, onDone });

      fireEvent.click(screen.getByRole("button", { name: "Done" }));

      await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
      expect(update).toHaveBeenCalledWith({ completed: true });
      expect(calls).toEqual(["update", "done"]);
    });

    it("keeps Done enabled before the model is ready", async () => {
      const { update, onDone } = renderPage({ state: tryItStep, engineStatus: loading });

      const done = screen.getByRole("button", { name: "Done" }) as HTMLButtonElement;
      expect(done.disabled).toBe(false);
      fireEvent.click(done);

      await waitFor(() => expect(onDone).toHaveBeenCalled());
      expect(update).toHaveBeenCalledWith({ completed: true });
    });

    it("leaves setup even when the completion could not be saved, so the user is never stuck", async () => {
      const update = vi.fn<Update>().mockRejectedValue(new Error("disk full"));
      const { onDone } = renderPage({ state: tryItStep, engineStatus: ready, update });

      fireEvent.click(screen.getByRole("button", { name: "Done" }));

      await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
    });
  });
});
