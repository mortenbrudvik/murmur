import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import type { MicrophoneInfo } from "../../types/domain";

/** The microphones now, or null when they could not be read. */
async function readMicrophones(): Promise<MicrophoneInfo[] | null> {
  try {
    return await apiClient.call<MicrophoneInfo[]>("settings", "getMicrophones", {});
  } catch {
    return null;
  }
}

export function useMicrophones(): {
  microphones: MicrophoneInfo[];
  /** False until the first answer (or failure) has come back, so "no microphones" is not shown too early. */
  loaded: boolean;
  /** Reads the list again, e.g. when the user opens the picker. Never rejects: a failure keeps the old list. */
  refresh(): Promise<void>;
} {
  const [microphones, setMicrophones] = useState<MicrophoneInfo[]>([]);
  const [loaded, setLoaded] = useState(false);

  // Each read gets a number; only the latest one may update the state, and none after unmount.
  const latestRead = useRef(0);
  const mounted = useRef(false);

  const accept = useCallback((readId: number, list: MicrophoneInfo[] | null) => {
    if (!mounted.current || readId !== latestRead.current) return;
    // A failed read keeps what we had: the list is only a convenience and is read again when next opened.
    if (list) setMicrophones(list);
    setLoaded(true);
  }, []);

  const refresh = useCallback(async () => {
    const readId = ++latestRead.current;
    accept(readId, await readMicrophones());
  }, [accept]);

  useEffect(() => {
    mounted.current = true;
    const readId = ++latestRead.current;
    void readMicrophones().then((list) => accept(readId, list));

    return () => {
      mounted.current = false;
    };
  }, [accept]);

  return { microphones, loaded, refresh };
}
