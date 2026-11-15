import { useEffect, useRef, useState } from "react";
import { apiClient } from "../interop/WebViewApiClient";
import { useWebViewEvent } from "./useWebViewEvent";

/**
 * State the .NET side owns and pushes: read once on mount with `service.method`, then kept current by `event`. Null
 * until known. A read that fails or answers null leaves it unknown; the next push will tell.
 */
export function usePushedState<T>(service: string, method: string, event: string): T | null {
  const [state, setState] = useState<T | null>(null);
  // A pushed state is always newer than the one read on mount, even if the read finishes later.
  const pushed = useRef(false);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .call<T | null>(service, method, {})
      .then((initial) => {
        if (!cancelled && !pushed.current && initial) setState(initial);
      })
      .catch(() => {
        // Stay unknown: the next push will tell us.
      });

    return () => {
      cancelled = true;
    };
  }, [service, method]);

  useWebViewEvent<T>(event, (changed) => {
    pushed.current = true;
    setState(changed);
  });

  return state;
}
