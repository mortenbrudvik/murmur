import { describe, expect, it } from "vitest";
import { NO_GPU_NOTE, describeEngineStatus, showsNoGpuNote } from "../engineStatusText";
import type { EngineStatus } from "../../../types/domain";

function status(overrides: Partial<EngineStatus>): EngineStatus {
  return { state: "loading", modelName: "large-v3-turbo", runtime: null, downloadProgress: null, error: null, ...overrides };
}

describe("describeEngineStatus", () => {
  it("shows the rounded download percentage with a progress value", () => {
    expect(describeEngineStatus(status({ state: "downloading", downloadProgress: 0.426 }))).toEqual({
      text: "Downloading model… 43%",
      tone: "progress",
      progress: 43,
    });
  });

  it("treats a missing or out-of-range download progress as 0..100", () => {
    expect(describeEngineStatus(status({ state: "downloading", downloadProgress: null })).text).toBe("Downloading model… 0%");
    expect(describeEngineStatus(status({ state: "downloading", downloadProgress: 1.7 })).progress).toBe(100);
    expect(describeEngineStatus(status({ state: "downloading", downloadProgress: -1 })).progress).toBe(0);
  });

  it("says Loading model… for loading and notStarted", () => {
    for (const state of ["loading", "notStarted"] as const) {
      expect(describeEngineStatus(status({ state }))).toEqual({ text: "Loading model…", tone: "info", progress: null });
    }
  });

  it("names the runtime when ready, and drops the separator when it is unknown", () => {
    expect(describeEngineStatus(status({ state: "ready", runtime: "Cuda" })).text).toBe("Ready · Cuda");
    expect(describeEngineStatus(status({ state: "ready", runtime: null })).text).toBe("Ready");
    expect(describeEngineStatus(status({ state: "ready", runtime: "Cuda" })).tone).toBe("ready");
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

describe("showsNoGpuNote", () => {
  it("is true only when ready on something other than CUDA", () => {
    expect(showsNoGpuNote(status({ state: "ready", runtime: "Vulkan" }))).toBe(true);
    expect(showsNoGpuNote(status({ state: "ready", runtime: "Cpu" }))).toBe(true);
    expect(showsNoGpuNote(status({ state: "ready", runtime: null }))).toBe(true);
    expect(showsNoGpuNote(status({ state: "ready", runtime: "Cuda" }))).toBe(false);
    expect(showsNoGpuNote(status({ state: "loading", runtime: "Vulkan" }))).toBe(false);
    expect(showsNoGpuNote(status({ state: "error", runtime: null }))).toBe(false);
  });

  it("has the agreed wording", () => {
    expect(NO_GPU_NOTE).toBe("Running without GPU acceleration — install CUDA 13 for faster transcription.");
  });
});
