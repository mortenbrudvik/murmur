import { describe, expect, it } from "vitest";
import { describeEngineStatus, describeModelSwitch } from "../engineStatusText";
import type { DownloadProgress, EngineStatus } from "../../../types/domain";

function status(overrides: Partial<EngineStatus>): EngineStatus {
  return { state: "loading", modelName: "Whisper large-v3-turbo", runtime: null, error: null, download: null, gpu: null, switching: false, ...overrides };
}

function download(overrides: Partial<DownloadProgress>): DownloadProgress {
  return {
    modelName: "Whisper large-v3-turbo",
    bytesReceived: 0,
    totalBytes: 1_000_000_000,
    bytesPerSecond: 0,
    secondsRemaining: null,
    retries: 0,
    reconnecting: false,
    ...overrides,
  };
}

describe("describeEngineStatus", () => {
  it("shows the rounded download percentage taken from the bytes, with a progress value", () => {
    expect(
      describeEngineStatus(status({ state: "downloading", download: download({ bytesReceived: 380_000_000 }) })),
    ).toEqual({
      text: "Downloading speech model… 38%",
      tone: "progress",
      progress: 38,
    });
  });

  it("says Reconnecting… while the download reconnects, and keeps the bar where it was", () => {
    const view = describeEngineStatus(
      status({ state: "downloading", download: download({ bytesReceived: 380_000_000, retries: 1, reconnecting: true }) }),
    );

    expect(view).toEqual({ text: "Reconnecting…", tone: "progress", progress: 38 });
  });

  it("shows 0% while the download details are not known yet", () => {
    expect(describeEngineStatus(status({ state: "downloading", download: null }))).toEqual({
      text: "Downloading speech model… 0%",
      tone: "progress",
      progress: 0,
    });
    expect(describeEngineStatus(status({ state: "downloading", download: download({ totalBytes: 0 }) })).progress).toBe(0);
  });

  it("says Loading model… for loading and notStarted", () => {
    for (const state of ["loading", "notStarted"] as const) {
      expect(describeEngineStatus(status({ state }))).toEqual({ text: "Loading model…", tone: "info", progress: null });
    }
  });

  it("says which model it is switching to while the next model loads, with no progress bar", () => {
    expect(
      describeEngineStatus(status({ state: "loading", modelName: "Whisper small (English)", switching: true })),
    ).toEqual({ text: "Switching to Whisper small (English)…", tone: "info", progress: null });
  });

  it("names the runtime when ready, in capitals where it is an acronym", () => {
    expect(describeEngineStatus(status({ state: "ready", runtime: "Cuda" })).text).toBe("Ready · CUDA");
    expect(describeEngineStatus(status({ state: "ready", runtime: "Vulkan" })).text).toBe("Ready · Vulkan");
    expect(describeEngineStatus(status({ state: "ready", runtime: "Cpu" })).text).toBe("Ready · CPU");
    expect(describeEngineStatus(status({ state: "ready", runtime: "Metal" })).text).toBe("Ready · Metal");
    expect(describeEngineStatus(status({ state: "ready", runtime: "Cuda" })).tone).toBe("ready");
  });

  it("drops the separator when the runtime is unknown", () => {
    expect(describeEngineStatus(status({ state: "ready", runtime: null })).text).toBe("Ready");
  });

  it("stays Ready, with no progress bar, while a model switch downloads", () => {
    const view = describeEngineStatus(
      status({ state: "ready", runtime: "Cuda", download: download({ bytesReceived: 380_000_000 }) }),
    );

    expect(view).toEqual({ text: "Ready · CUDA", tone: "ready", progress: null });
  });

  it("shows the error text, with a fallback when there is none", () => {
    expect(describeEngineStatus(status({ state: "error", error: "Out of memory" }))).toEqual({
      text: "Out of memory",
      tone: "error",
      progress: null,
    });
    expect(describeEngineStatus(status({ state: "error", error: null })).text).toBe("The speech model could not be loaded.");
  });
});

describe("describeModelSwitch", () => {
  it("names the model being downloaded, with the rounded percent and the progress value", () => {
    expect(
      describeModelSwitch(download({ modelName: "Whisper small (English)", bytesReceived: 380_000_000 })),
    ).toEqual({ text: "Downloading Whisper small (English)… 38%", tone: "progress", progress: 38 });
  });

  it("says Reconnecting… while the download reconnects, and keeps the bar where it was", () => {
    expect(describeModelSwitch(download({ bytesReceived: 380_000_000, retries: 1, reconnecting: true }))).toEqual({
      text: "Reconnecting…",
      tone: "progress",
      progress: 38,
    });
  });
});
