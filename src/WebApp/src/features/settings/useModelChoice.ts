import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import { useWebViewEvent } from "../../hooks/useWebViewEvent";
import type { EngineStatus, ModelChoice, SpeechModelId } from "../../types/domain";

/**
 * The speech model in use, the one that suits this PC and the models on offer. Null until known.
 * Choosing a model returns at once: the download and the switch go on in the background and are reported by
 * engine.statusChanged.
 */
export function useModelChoice(): { choice: ModelChoice | null; select(id: SpeechModelId): Promise<void> } {
  const [choice, setChoice] = useState<ModelChoice | null>(null);

  // Each read or choice gets a number; only the latest answer may update the state, and none after unmount.
  const latest = useRef(0);
  const mounted = useRef(false);

  const accept = useCallback((requestId: number, answer: ModelChoice | null) => {
    // A failed or empty answer keeps what we had: the server's choice is read again when the engine next reports ready.
    if (mounted.current && requestId === latest.current && answer) setChoice(answer);
  }, []);

  const read = useCallback(async () => {
    const requestId = ++latest.current;
    try {
      accept(requestId, await apiClient.call<ModelChoice | null>("models", "getChoice", {}));
    } catch {
      // Stay as we are; see accept.
    }
  }, [accept]);

  useEffect(() => {
    mounted.current = true;
    void read();

    return () => {
      mounted.current = false;
    };
  }, [read]);

  // A switch that fails leaves the engine ready on the old model and puts the old choice back, so the choice shown here
  // is read again whenever the engine reports ready with nothing left to download.
  useWebViewEvent<EngineStatus>("engine.statusChanged", (status) => {
    if (status.state === "ready" && !status.download) void read();
  });

  /** Never rejects: when the request fails the selection simply stays where it was. */
  const select = useCallback(
    async (id: SpeechModelId) => {
      const requestId = ++latest.current;
      try {
        accept(requestId, await apiClient.call<ModelChoice | null>("models", "setChoice", { model: id }));
      } catch {
        // The selection stays on the previous model, which is the model still in use.
      }
    },
    [accept],
  );

  return { choice, select };
}
