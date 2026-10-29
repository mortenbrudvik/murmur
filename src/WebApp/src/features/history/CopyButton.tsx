import { useEffect, useRef, useState } from "react";

type CopyState = "idle" | "copied" | "failed";

const FEEDBACK_MS = 1500;

const LABELS: Record<CopyState, string> = {
  idle: "Copy",
  copied: "Copied",
  failed: "Copy failed",
};

export default function CopyButton({ text }: { text: string }) {
  const [state, setState] = useState<CopyState>("idle");
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  useEffect(() => () => clearTimeout(timer.current), []);

  async function copy() {
    let next: CopyState = "copied";
    try {
      await navigator.clipboard.writeText(text);
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
