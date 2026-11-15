import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import { failure } from "../../interop/errorMessage";
import { useServerDocument } from "../../hooks/useServerDocument";
import type { HotkeyDefinition, MurmurSettings } from "../../types/domain";

/** The capture waits on the user: the .NET side gives up after 10 seconds, so the page waits longer than that. */
const CAPTURE_TIMEOUT_MS = 20_000;

// The server replaces the whole document (a missing flag would reset to false), so every save sends all six fields,
// and what it returns is what the page shows: an unusable hotkey comes back unchanged.
const toParams = (doc: MurmurSettings) => ({
  hotkey: doc.hotkey,
  microphoneId: doc.microphoneId,
  soundCues: doc.soundCues,
  startWithWindows: doc.startWithWindows,
  keepAudio: doc.keepAudio,
  keepMicrophoneReady: doc.keepMicrophoneReady,
});

export function useSettings(): {
  settings: MurmurSettings | null;
  loading: boolean;
  error: string | null;
  /** True while waiting for the user to press a new hotkey. */
  capturing: boolean;
  /**
   * Saves the document that `change` makes from the latest stored one. Edits run one after another, so
   * several quick changes never overwrite each other. Rejects (and sets `error`) when the save fails.
   */
  edit(change: (current: MurmurSettings) => MurmurSettings): Promise<void>;
  /**
   * Waits for the user to press the new hotkey. Resolves with null when they cancel (Esc), when it times
   * out, when it fails (then `error` is set) or when the page was left in the meantime.
   */
  captureHotkey(): Promise<HotkeyDefinition | null>;
} {
  const { doc, loading, error, edit, setSaveError } = useServerDocument<MurmurSettings>({
    service: "settings",
    toParams,
    saveFailure: "Could not save the settings",
    notLoaded: "The settings have not been loaded yet.",
  });
  const [capturing, setCapturing] = useState(false);
  const mounted = useRef(false);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const captureHotkey = useCallback(async (): Promise<HotkeyDefinition | null> => {
    setSaveError(null);
    setCapturing(true);
    try {
      const key = await apiClient.call<HotkeyDefinition | null>("settings", "captureHotkey", {}, { timeoutMs: CAPTURE_TIMEOUT_MS });
      // A key pressed after the page was left is ignored, not saved.
      return mounted.current ? (key ?? null) : null;
    } catch (err) {
      if (mounted.current) setSaveError(failure("Could not read the key press", err));
      return null;
    } finally {
      if (mounted.current) setCapturing(false);
    }
  }, [setSaveError]);

  return { settings: doc, loading, error, capturing, edit, captureHotkey };
}
