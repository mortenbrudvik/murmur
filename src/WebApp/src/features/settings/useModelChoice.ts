import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import { failure } from "../../interop/errorMessage";
import { useWebViewEvent } from "../../hooks/useWebViewEvent";
import type { EngineStatus, ModelChoice, SpeechModelId } from "../../types/domain";

/** How long a choice waits for a newer one before it is sent, so that arrowing through the options starts no downloads. */
const CHOICE_DELAY_MS = 1000;

/**
 * The speech model in use, the one that suits this PC and the models on offer. Null until known.
 * A choice shows at once but is sent one second after the last change, or at once when the page is left before then;
 * sending it returns at once: the download and the switch go on in the background and are reported by
 * engine.statusChanged. When reading or sending fails, `error` says so and the choice shown is the one the server last
 * confirmed.
 */
export function useModelChoice(): {
  choice: ModelChoice | null;
  error: string | null;
  select(id: SpeechModelId): void;
} {
  // What the server last confirmed, and the model chosen since then that has not been confirmed yet.
  const [saved, setSaved] = useState<ModelChoice | null>(null);
  const [pending, setPending] = useState<SpeechModelId | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Each read or send gets a number; only the latest answer may update the state, and none after unmount.
  const latest = useRef(0);
  const mounted = useRef(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const savedSelection = useRef<SpeechModelId | null>(null);
  // The choice the timer will send, for an unmount during the wait.
  const waitingSelection = useRef<SpeechModelId | null>(null);

  const accept = useCallback((requestId: number, answer: ModelChoice | null) => {
    if (!mounted.current || requestId !== latest.current) return;
    // An empty answer keeps what we had: the server's choice is read again when the engine next reports ready.
    if (answer) {
      savedSelection.current = answer.selected;
      setSaved(answer);
    }
    setError(null);
    // The answer settles the choice made so far, unless a newer choice is still waiting to be sent.
    if (timer.current === null) setPending(null);
  }, []);

  const reject = useCallback((requestId: number, message: string) => {
    if (!mounted.current || requestId !== latest.current) return;
    setError(message);
    if (timer.current === null) setPending(null);
  }, []);

  const read = useCallback(async () => {
    const requestId = ++latest.current;
    try {
      accept(requestId, await apiClient.call<ModelChoice | null>("models", "getChoice", {}));
    } catch (err) {
      reject(requestId, failure("Could not read the speech model", err));
    }
  }, [accept, reject]);

  const send = useCallback(
    async (id: SpeechModelId) => {
      const requestId = ++latest.current;
      try {
        accept(requestId, await apiClient.call<ModelChoice | null>("models", "setChoice", { model: id }));
      } catch (err) {
        reject(requestId, failure("Could not change the speech model", err));
      }
    },
    [accept, reject],
  );

  useEffect(() => {
    mounted.current = true;
    void read();

    return () => {
      mounted.current = false;
      if (timer.current === null) return;

      // Leaving the page within the wait is not taking the choice back: a choice still waiting is sent at once. Its
      // answer finds the hook unmounted and is ignored; the switch is reported through the engine status as usual.
      clearTimeout(timer.current);
      timer.current = null;
      const waiting = waitingSelection.current;
      if (waiting !== null && waiting !== savedSelection.current) void send(waiting);
    };
  }, [read, send]);

  // A switch that fails leaves the engine ready on the old model and puts the old choice back, so the choice shown here
  // is read again whenever the engine reports ready with nothing left to download.
  useWebViewEvent<EngineStatus>("engine.statusChanged", (status) => {
    if (status.state === "ready" && !status.download) void read();
  });

  const select = useCallback(
    (id: SpeechModelId) => {
      setError(null);
      setPending(id);
      waitingSelection.current = id;
      if (timer.current !== null) clearTimeout(timer.current);
      timer.current = setTimeout(() => {
        timer.current = null;
        // Back on the confirmed model by the end of the wait: nothing to switch.
        if (id === savedSelection.current) setPending(null);
        else void send(id);
      }, CHOICE_DELAY_MS);
    },
    [send],
  );

  const choice = useMemo(() => (saved && pending ? { ...saved, selected: pending } : saved), [saved, pending]);

  return { choice, error, select };
}
