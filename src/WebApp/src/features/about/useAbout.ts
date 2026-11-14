import { useEffect, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import type { AboutInfo } from "../../types/domain";

export interface AboutState {
  /** Null until the shell has answered, and when it could not. */
  info: AboutInfo | null;
  /** The shell could not be asked: the dialog says "Unavailable" rather than waiting for ever. */
  failed: boolean;
}

const UNKNOWN: AboutState = { info: null, failed: false };

/**
 * What the About dialog needs from the shell. Read each time the dialog opens, so the count of dictated words is the
 * one of that moment; nothing is asked while it is closed.
 */
export function useAbout(open: boolean): AboutState {
  const [state, setState] = useState<AboutState>(UNKNOWN);

  useEffect(() => {
    if (!open) return;

    let cancelled = false;
    apiClient
      .call<AboutInfo | null>("app", "getAbout", {})
      .then((info) => {
        if (!cancelled) setState(info ? { info, failed: false } : { info: null, failed: true });
      })
      .catch(() => {
        if (!cancelled) setState({ info: null, failed: true });
      });

    return () => {
      cancelled = true;
      setState(UNKNOWN);
    };
  }, [open]);

  return state;
}
