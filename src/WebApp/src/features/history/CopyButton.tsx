import { useEffect, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";

type CopyState = "idle" | "copied" | "failed";

const FEEDBACK_MS = 1500;

const LABELS: Record<CopyState, string> = {
  idle: "Copy",
  copied: "Copied",
  failed: "Copy failed",
};

/**
 * Copies a history entry's text. Through Murmur rather than navigator.clipboard, so the copy stays out of Win+V
 * clipboard history and the cloud clipboard, like every other clipboard write Murmur makes.
 */
export default function CopyButton({ entryId }: { entryId: string }) {
  const [state, setState] = useState<CopyState>("idle");
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  useEffect(() => () => clearTimeout(timer.current), []);

  async function copy() {
    let next: CopyState;
    try {
      // False when the entry is gone or the clipboard could not be written.
      next = (await apiClient.call<boolean>("history", "copy", { id: entryId })) ? "copied" : "failed";
    } catch {
      next = "failed";
    }

    setState(next);
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setState("idle"), FEEDBACK_MS);
  }

  return (
    <button
      type="button"
      className={`history-action history-copy history-copy-${state}`}
      onClick={() => void copy()}
    >
      {LABELS[state]}
    </button>
  );
}
