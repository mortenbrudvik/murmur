import { useCallback, useEffect, useState } from "react";
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
 * whether the microphone opened, and is null while the test starts. `restart` runs the test again ("Check again").
 *
 * The .NET side handles requests on several threads, so a stop sent before the start it follows has answered could be
 * handled first and leave the microphone open. Start and stop therefore go through one queue, in order.
 *
 * The microphone is only opened while the window is visible, so the Windows microphone indicator never lights up for a
 * page nobody sees. The .NET side stops the test by itself when the window is hidden; it is started again when the
 * window is shown or gets focus. The focus case covers coming back from Windows' microphone settings, which never hides
 * Murmur's window: access allowed there is only noticed by testing again. Starting a test that is running restarts it.
 */
export function useMicLevel(microphoneId: string | null): { level: number; result: MicTestResult | null; restart(): void } {
  const [level, setLevel] = useState(0);
  const [answer, setAnswer] = useState<Answer | null>(null);
  // Counts "Check again" presses: a new value runs the effect again, which stops the test and starts it anew.
  const [attempt, setAttempt] = useState(0);
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
      // Hidden: the handlers below start the test when the window is shown.
      if (document.visibilityState !== "visible") return;

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

    start();
    document.addEventListener("visibilitychange", start);
    window.addEventListener("focus", start);

    return () => {
      document.removeEventListener("visibilitychange", start);
      window.removeEventListener("focus", start);
      live = false;
      // Nothing to report if this fails: the .NET side ends the test by itself when the window hides or after two minutes.
      enqueue(() => apiClient.call("setup", "stopMicTest", {})).catch(() => undefined);
    };
  }, [microphoneId, enqueue, attempt]);

  const restart = useCallback(() => setAttempt((count) => count + 1), []);

  return { level, result: answer?.microphoneId === microphoneId ? answer.result : null, restart };
}
