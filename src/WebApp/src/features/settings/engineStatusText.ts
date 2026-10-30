import type { EngineStatus } from "../../types/domain";

export type EngineTone = "progress" | "info" | "ready" | "error";

export interface EngineStatusView {
  text: string;
  tone: EngineTone;
  /** Percent 0..100 while downloading, otherwise null. */
  progress: number | null;
}

export const NO_GPU_NOTE = "Running without GPU acceleration — install CUDA 13 for faster transcription.";

/** The one status line shared by the Settings engine card and the banner above every page. */
export function describeEngineStatus(status: EngineStatus): EngineStatusView {
  switch (status.state) {
    case "downloading": {
      const percent = Math.round(Math.min(1, Math.max(0, status.downloadProgress ?? 0)) * 100);
      return { text: `Downloading model… ${percent}%`, tone: "progress", progress: percent };
    }
    case "ready":
      return { text: status.runtime ? `Ready · ${status.runtime}` : "Ready", tone: "ready", progress: null };
    case "error":
      return { text: status.error || "The speech model could not be loaded.", tone: "error", progress: null };
    default:
      // loading and notStarted: from the user's side the model is on its way.
      return { text: "Loading model…", tone: "info", progress: null };
  }
}

/** CUDA falls back silently to Vulkan or the CPU, so a ready engine on anything else gets a note. */
export function showsNoGpuNote(status: EngineStatus): boolean {
  return status.state === "ready" && status.runtime !== "Cuda";
}
