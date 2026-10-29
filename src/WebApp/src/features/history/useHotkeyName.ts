import { useEffect, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import type { MurmurSettings } from "../../types/domain";

export const DEFAULT_HOTKEY_NAME = "Right Ctrl";

/** The configured hotkey's display name; the default one until it is known or if it cannot be read. */
export function useHotkeyName(): string {
  const [name, setName] = useState(DEFAULT_HOTKEY_NAME);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .call<MurmurSettings>("settings", "get", {})
      .then((settings) => {
        const displayName = settings?.hotkey?.displayName;
        if (!cancelled && displayName) setName(displayName);
      })
      .catch(() => {
        // Keep the default name: it is only used for the empty-state text.
      });

    return () => {
      cancelled = true;
    };
  }, []);

  return name;
}
