import { useEffect, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import { useWebViewEvent } from "../../hooks/useWebViewEvent";
import type { EngineStatus } from "../../types/domain";

/** The speech engine's status: read once on mount, then kept current by engine.statusChanged. Null until known. */
export function useEngineStatus(): EngineStatus | null {
  const [status, setStatus] = useState<EngineStatus | null>(null);
  // A pushed status is always newer than the one read on mount, even if the read finishes later.
  const pushed = useRef(false);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .call<EngineStatus>("engine", "getStatus", {})
      .then((initial) => {
        if (!cancelled && !pushed.current) setStatus(initial);
      })
      .catch(() => {
        // Stay unknown: the next engine.statusChanged push will tell us.
      });

    return () => {
      cancelled = true;
    };
  }, []);

  useWebViewEvent<EngineStatus>("engine.statusChanged", (changed) => {
    pushed.current = true;
    setStatus(changed);
  });

  return status;
}
