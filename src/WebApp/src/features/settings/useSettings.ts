import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import { errorMessage, failure } from "../../interop/errorMessage";
import { useSerialQueue } from "../../hooks/useSerialQueue";
import type { HotkeyDefinition, MurmurSettings } from "../../types/domain";

export function useSettings(): {
  settings: MurmurSettings | null;
  loading: boolean;
  error: string | null;
  /** True while waiting for the user to press a new hotkey. */
  capturing: boolean;
  /** Saves the whole document. Resolves once stored; rejects (and sets `error`) when the save fails. */
  save(s: MurmurSettings): Promise<void>;
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
  const [settings, setSettings] = useState<MurmurSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [capturing, setCapturing] = useState(false);

  // The document the server last confirmed, which edits are built from.
  const latest = useRef<MurmurSettings | null>(null);
  const mounted = useRef(false);
  const enqueue = useSerialQueue();

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .call<MurmurSettings>("settings", "get", {})
      .then((loaded) => {
        if (cancelled) return;
        latest.current = loaded;
        setSettings(loaded);
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoadError(errorMessage(err));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  // The server replaces the whole document (a missing flag would reset to false), so every save sends
  // all five fields, and what it returns is what the page shows: an unusable hotkey comes back unchanged.
  const send = useCallback(async (doc: MurmurSettings) => {
    setSaveError(null);
    try {
      const saved = await apiClient.call<MurmurSettings>("settings", "update", {
        hotkey: doc.hotkey,
        microphoneId: doc.microphoneId,
        soundCues: doc.soundCues,
        startWithWindows: doc.startWithWindows,
        keepAudio: doc.keepAudio,
      });
      latest.current = saved;
      setSettings(saved);
    } catch (err) {
      setSaveError(failure("Could not save the settings", err));
      throw err;
    }
  }, []);

  const save = useCallback((doc: MurmurSettings) => enqueue(() => send(doc)), [enqueue, send]);

  const edit = useCallback(
    (change: (current: MurmurSettings) => MurmurSettings) =>
      enqueue(async () => {
        const current = latest.current;
        if (!current) throw new Error("The settings have not been loaded yet.");
        await send(change(current));
      }),
    [enqueue, send],
  );

  const captureHotkey = useCallback(async (): Promise<HotkeyDefinition | null> => {
    setSaveError(null);
    setCapturing(true);
    try {
      const key = await apiClient.call<HotkeyDefinition | null>("settings", "captureHotkey", {});
      // A key pressed after the page was left is ignored, not saved.
      return mounted.current ? (key ?? null) : null;
    } catch (err) {
      if (mounted.current) setSaveError(failure("Could not read the key press", err));
      return null;
    } finally {
      if (mounted.current) setCapturing(false);
    }
  }, []);

  return { settings, loading, error: saveError ?? loadError, capturing, save, edit, captureHotkey };
}
