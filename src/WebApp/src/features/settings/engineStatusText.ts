import type { EngineStatus } from "../../types/domain";
import { describeDownload } from "./formatDownload";

export type EngineTone = "progress" | "info" | "ready" | "error";

export interface EngineStatusView {
  text: string;
  tone: EngineTone;
  /** Percent 0..100 while downloading, otherwise null. */
  progress: number | null;
}

/** The runtime names the engine reports ("Cuda", "Vulkan", "Cpu") as they are written. */
function runtimeLabel(runtime: string): string {
  switch (runtime) {
    case "Cuda":
      return "CUDA";
    case "Cpu":
      return "CPU";
    default:
      return runtime;
  }
}

/** The one status line shared by the Settings engine card and the banner above every page. */
export function describeEngineStatus(status: EngineStatus): EngineStatusView {
  switch (status.state) {
    case "downloading": {
      // The bar keeps its place while the download reconnects.
      const download = status.download;
      const percent = download ? describeDownload(download).percent : 0;
      const text = download?.reconnecting ? "Reconnecting…" : `Downloading speech model… ${percent}%`;
      return { text, tone: "progress", progress: percent };
    }
    case "ready":
      return { text: status.runtime ? `Ready · ${runtimeLabel(status.runtime)}` : "Ready", tone: "ready", progress: null };
    case "error":
      return { text: status.error || "The speech model could not be loaded.", tone: "error", progress: null };
    default:
      // loading and notStarted: from the user's side the model is on its way.
      return { text: "Loading model…", tone: "info", progress: null };
  }
}
