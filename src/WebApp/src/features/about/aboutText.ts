import { runtimeLabel } from "../settings/engineStatusText";
import type { AboutInfo, EngineStatus } from "../../types/domain";

/** The UI is English, so month names are too, whatever the user's locale; only digit grouping follows the locale. */
const MONTH_LOCALE = "en-US";

type Running = { kind: "ready"; runtime: string; gpuName: string | null } | { kind: "starting" } | { kind: "stopped" };

/**
 * What the engine runs on, as far as it can be vouched for. The status's `gpu` is the CUDA tip, not the device in use:
 * it names a card whenever a CUDA-capable NVIDIA card was found, also while Vulkan does the work on some other card.
 * So the card is named only when the runtime is CUDA.
 */
function running(status: EngineStatus | null): Running {
  if (!status) return { kind: "starting" };
  if (status.state === "error") return { kind: "stopped" };
  if (status.state !== "ready" || !status.runtime) return { kind: "starting" };

  const runtime = runtimeLabel(status.runtime);
  return { kind: "ready", runtime, gpuName: status.runtime === "Cuda" ? (status.gpu?.gpuName ?? null) : null };
}

/** The "Running on" row: "NVIDIA GeForce RTX 3090 · CUDA", "Vulkan", "CPU", "Starting…" or "Not running". */
export function describeRunningOn(status: EngineStatus | null): string {
  const state = running(status);
  if (state.kind === "starting") return "Starting…";
  if (state.kind === "stopped") return "Not running";
  return state.gpuName ? `${state.gpuName} · ${state.runtime}` : state.runtime;
}

function plural(count: number, noun: string, locale?: string): string {
  return `${count.toLocaleString(locale)} ${noun}${count === 1 ? "" : "s"}`;
}

/** The "Dictated so far" row: the words, and below them the dictations and the month the history starts in. */
export function describeDictated(info: AboutInfo, locale?: string): { main: string; detail: string | null } {
  if (info.dictations === 0 || !info.since) return { main: "Nothing yet", detail: null };

  const since = new Intl.DateTimeFormat(MONTH_LOCALE, { month: "long", year: "numeric" }).format(new Date(info.since));
  return {
    main: plural(info.words, "word", locale),
    detail: `in ${plural(info.dictations, "dictation", locale)} since ${since}`,
  };
}

/** What "Copy details" puts on the clipboard: four lines for a bug report. */
export function buildDetails(info: AboutInfo, status: EngineStatus | null): string {
  const state = running(status);
  const runtime =
    state.kind === "starting"
      ? "Starting"
      : state.kind === "stopped"
        ? "Not running"
        : state.gpuName
          ? `${state.runtime}, ${state.gpuName}`
          : state.runtime;

  return [
    `Murmur ${info.version} (${info.channel})`,
    `Model: ${status?.modelName || "Not chosen yet"}`,
    `Runtime: ${runtime}`,
    info.windowsBuild,
  ].join("\n");
}
