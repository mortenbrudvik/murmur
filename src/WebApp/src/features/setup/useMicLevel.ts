import { useEffect, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import { failure } from "../../interop/errorMessage";
import { useSerialQueue } from "../../hooks/useSerialQueue";
import { useWebViewEvent } from "../../hooks/useWebViewEvent";
import type { MicTestResult } from "../../types/domain";

/** How the test went, and for which microphone: an answer about the previous microphone is not shown for the next one. */
interface Answer {
  microphoneId: string | null;
  result: MicTestResult;
}

/**
 * The microphone test of the setup page: starts it on `microphoneId` (null is the Windows default) when mounted and
 * whenever the microphone changes, and stops it when unmounted. `level` is the current volume from 0 to 1; `result` says
 * whether the microphone opened, and is null while the test starts.
 *
 * The .NET side handles requests on several threads, so a stop sent before the start it follows has answered could be
 * handled first and leave the microphone open. Start and stop therefore go through one queue, in order.
 * The .NET side stops the test by itself when the window is hidden, so it is started again when the window is shown.
 */
export function useMicLevel(microphoneId: string | null): { level: number; result: MicTestResult | null } {
  const [level, setLevel] = useState(0);
  const [answer, setAnswer] = useState<Answer | null>(null);
  const enqueue = useSerialQueue();

  useWebViewEvent<{ level: number }>("setup.micLevel", (data) => {
    const value = Number(data?.level);
    setLevel(Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 0);
  });

  useEffect(() => {
    // Each start gets a number, and only the latest one of the live effect may show its answer.
    let live = true;
    let latestStart = 0;

    function start() {
      const mine = ++latestStart;
      const accept = (result: MicTestResult | null) => {
        if (!live || mine !== latestStart) return;
        setAnswer(result ? { microphoneId, result } : null);
        // A microphone that did not open has no level, whatever arrived before the answer.
        if (result?.state !== "listening") setLevel(0);
      };

      enqueue(() => apiClient.call<MicTestResult>("setup", "startMicTest", { microphoneId })).then(accept, (err: unknown) =>
        accept({ state: "failed", error: failure("Could not test the microphone", err) }),
      );
    }

    function onVisibilityChange() {
      if (document.visibilityState === "visible") start();
    }

    start();
    document.addEventListener("visibilitychange", onVisibilityChange);

    return () => {
      document.removeEventListener("visibilitychange", onVisibilityChange);
      live = false;
      // Nothing to report if this fails: the .NET side ends the test by itself when the window hides or after two minutes.
      enqueue(() => apiClient.call("setup", "stopMicTest", {})).catch(() => undefined);
    };
  }, [microphoneId, enqueue]);

  return { level, result: answer?.microphoneId === microphoneId ? answer.result : null };
}
