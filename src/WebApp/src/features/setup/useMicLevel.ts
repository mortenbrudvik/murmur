import { useCallback, useEffect, useRef, useState } from "react";
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

/** The "setup.micLevel" event: the volume from 0 to 1, and whether the .NET side ended the test without being asked. */
interface MicLevelEvent {
  level: number;
  ended?: boolean;
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
 * page nobody sees. The .NET side stops the test by itself when the window is hidden or minimized, after two minutes and
 * when the microphone fails, and says so with an `ended` event. Showing the window or giving it focus starts the test
 * again, but only when none is running or on its way: opening a microphone that already works switches a Bluetooth
 * headset's profile, so alt-tabbing back must leave a working test alone, and the two events of one show start it once.
 * The focus case covers coming back from Windows' microphone settings, which never hides Murmur's window: access allowed
 * there is only noticed by testing again. "Check again" and a new microphone always restart the test.
 */
export function useMicLevel(microphoneId: string | null): { level: number; result: MicTestResult | null; restart(): void } {
  const [level, setLevel] = useState(0);
  const [answer, setAnswer] = useState<Answer | null>(null);
  // Counts "Check again" presses: a new value runs the effect again, which stops the test and starts it anew.
  const [attempt, setAttempt] = useState(0);
  const enqueue = useSerialQueue();
  // A start has answered `listening`, and the test has not ended or been stopped since.
  const running = useRef(false);
  // A start has been sent and not answered yet.
  const pending = useRef(false);

  useWebViewEvent<MicLevelEvent>("setup.micLevel", (data) => {
    if (data?.ended) {
      // An end that arrives while a start is on its way may belong to the test that start replaces: its answer decides.
      if (running.current && !pending.current) {
        running.current = false;
        setLevel(0);
      }
      return;
    }

    // A level can trail the end of its test, or the stop the page asked for: the meter stays at 0 then.
    if (!running.current && !pending.current) return;
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
      running.current = false;
      pending.current = true;
      // The meter of a new test starts from silence, not from where the previous one left off.
      setLevel(0);
      const accept = (result: MicTestResult | null) => {
        if (!live || mine !== latestStart) return;
        pending.current = false;
        running.current = result?.state === "listening";
        setAnswer(result ? { microphoneId, result } : null);
        // A microphone that did not open has no level, whatever arrived before the answer.
        if (!running.current) setLevel(0);
      };

      enqueue(() => apiClient.call<MicTestResult>("setup", "startMicTest", { microphoneId })).then(accept, (err: unknown) =>
        accept({ state: "failed", error: failure("Could not test the microphone", err) }),
      );
    }

    // The window was shown or got focus: start the test unless it is running or on its way.
    function resume() {
      if (!running.current && !pending.current) start();
    }

    function onVisibilityChange() {
      if (document.visibilityState === "visible") resume();
    }

    start();
    document.addEventListener("visibilitychange", onVisibilityChange);
    window.addEventListener("focus", resume);

    return () => {
      document.removeEventListener("visibilitychange", onVisibilityChange);
      window.removeEventListener("focus", resume);
      live = false;
      running.current = false;
      pending.current = false;
      // Nothing to report if this fails: the .NET side ends the test by itself when the window hides or after two minutes.
      enqueue(() => apiClient.call("setup", "stopMicTest", {})).catch(() => undefined);
    };
  }, [microphoneId, enqueue, attempt]);

  const restart = useCallback(() => setAttempt((count) => count + 1), []);

  return { level, result: answer?.microphoneId === microphoneId ? answer.result : null, restart };
}
