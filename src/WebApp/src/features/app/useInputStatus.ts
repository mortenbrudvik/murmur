import { useEffect, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import { useWebViewEvent } from "../../hooks/useWebViewEvent";
import type { InputStatus } from "../../types/domain";

/**
 * Whether Windows let Murmur install its keyboard hook (the hotkey needs it): read once on mount, then kept current by
 * app.inputStatusChanged. The hook may still be retried when the window asks, and "installed" is the answer until a
 * failure is pushed. Null until known, or when it cannot be read.
 */
export function useInputStatus(): InputStatus | null {
  const [status, setStatus] = useState<InputStatus | null>(null);
  // A pushed status is always newer than the one read on mount, even if the read finishes later.
  const pushed = useRef(false);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .call<InputStatus | null>("app", "getInputStatus", {})
      .then((initial) => {
        if (!cancelled && !pushed.current && initial) setStatus(initial);
      })
      .catch(() => {
        // Stay unknown: no alert is better than a wrong one, and the next push will tell us.
      });

    return () => {
      cancelled = true;
    };
  }, []);

  useWebViewEvent<InputStatus>("app.inputStatusChanged", (changed) => {
    pushed.current = true;
    setStatus(changed);
  });

  return status;
}
