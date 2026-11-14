import { describe, expect, it } from "vitest";
import { buildDetails, describeDictated, describeRunningOn } from "../aboutText";
import type { AboutInfo, EngineStatus } from "../../../types/domain";

const base: EngineStatus = {
  state: "ready",
  modelName: "Whisper large-v3-turbo",
  runtime: "Cuda",
  error: null,
  download: null,
  gpu: { state: "cudaInUse", gpuName: "NVIDIA GeForce RTX 3090" },
  switching: false,
};

const about: AboutInfo = {
  version: "1.1.0",
  channel: "Microsoft Store",
  dataFolder: "C:\\Users\\me\\AppData\\Local\\Murmur",
  windowsBuild: "Windows 11, build 26200",
  dictations: 1204,
  words: 18420,
  since: "2026-10-03T08:00:00+00:00",
};

describe("describeRunningOn", () => {
  it("names the graphics card when the engine runs on CUDA", () => {
    expect(describeRunningOn(base)).toBe("NVIDIA GeForce RTX 3090 · CUDA");
  });

  it("says just CUDA when the card's name is not known", () => {
    expect(describeRunningOn({ ...base, gpu: null })).toBe("CUDA");
    expect(describeRunningOn({ ...base, gpu: { state: "cudaInUse", gpuName: null } })).toBe("CUDA");
  });

  it("does not name a card on Vulkan, where nothing says which card does the work", () => {
    const vulkan: EngineStatus = { ...base, runtime: "Vulkan", gpu: { state: "installCuda", gpuName: "NVIDIA GeForce RTX 3060" } };

    expect(describeRunningOn(vulkan)).toBe("Vulkan");
  });

  it("says CPU on the processor", () => {
    expect(describeRunningOn({ ...base, runtime: "Cpu", gpu: { state: "notApplicable", gpuName: null } })).toBe("CPU");
  });

  it.each(["notStarted", "downloading", "loading"] as const)("says Starting… while the engine is %s", (state) => {
    expect(describeRunningOn({ ...base, state, runtime: null })).toBe("Starting…");
  });

  it("says Starting… before the engine has reported", () => {
    expect(describeRunningOn(null)).toBe("Starting…");
  });

  it("says Not running when the engine failed", () => {
    expect(describeRunningOn({ ...base, state: "error", runtime: null, error: "Out of memory" })).toBe("Not running");
  });
});

describe("describeDictated", () => {
  it("says Nothing yet for an empty history", () => {
    expect(describeDictated({ ...about, dictations: 0, words: 0, since: null }, "en-US")).toEqual({ main: "Nothing yet", detail: null });
  });

  it("gives the words, the dictations and the month the history starts in", () => {
    expect(describeDictated(about, "en-US")).toEqual({
      main: "18,420 words",
      detail: "in 1,204 dictations since October 2026",
    });
  });

  it("uses the singular for one word and one dictation", () => {
    expect(describeDictated({ ...about, dictations: 1, words: 1 }, "en-US")).toEqual({
      main: "1 word",
      detail: "in 1 dictation since October 2026",
    });
  });

  it("groups digits the way the user's locale does, and keeps the month in English like the rest of the UI", () => {
    const dictated = describeDictated(about, "de-DE");

    expect(dictated.main).toBe("18.420 words");
    expect(dictated.detail).toBe("in 1.204 dictations since October 2026");
  });
});

describe("buildDetails", () => {
  it("lists version and channel, model, runtime with the card, and the Windows build", () => {
    expect(buildDetails(about, base)).toBe(
      [
        "Murmur 1.1.0 (Microsoft Store)",
        "Model: Whisper large-v3-turbo",
        "Runtime: CUDA, NVIDIA GeForce RTX 3090",
        "Windows 11, build 26200",
      ].join("\n"),
    );
  });

  it("gives the bare runtime when no card can be named", () => {
    expect(buildDetails(about, { ...base, runtime: "Vulkan" })).toContain("\nRuntime: Vulkan\n");
    expect(buildDetails(about, { ...base, runtime: "Cpu" })).toContain("\nRuntime: CPU\n");
    expect(buildDetails(about, { ...base, gpu: null })).toContain("\nRuntime: CUDA\n");
  });

  it("says Starting and Not chosen yet before the engine has a model", () => {
    const details = buildDetails(about, { ...base, state: "notStarted", modelName: "", runtime: null });

    expect(details).toContain("\nModel: Not chosen yet\n");
    expect(details).toContain("\nRuntime: Starting\n");
  });

  it("says Not running when the engine failed, and copes with no status at all", () => {
    expect(buildDetails(about, { ...base, state: "error", runtime: null })).toContain("\nRuntime: Not running\n");
    expect(buildDetails(about, null)).toContain("\nModel: Not chosen yet\nRuntime: Starting\n");
  });
});
