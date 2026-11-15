import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../interop/WebViewApiClient";
import { errorMessage, failure } from "../interop/errorMessage";
import { useSerialQueue } from "./useSerialQueue";

interface Options<T> {
  /** The bridge service whose `get` reads the document and whose `update` replaces it. */
  service: string;
  /** What `update` is sent for a document: the server replaces the whole document, so every field it keeps. */
  toParams(doc: T): unknown;
  /** "Could not save the settings": what a failed save reports, before the reason. */
  saveFailure: string;
  /** Why an edit made before the document has loaded is refused. */
  notLoaded: string;
}

/**
 * A document the server stores whole (the settings, the vocabulary): read once on mount, then changed one edit at a
 * time. Each edit is built from the document the server last confirmed, not from what the page shows, and edits run
 * one after another, so several quick changes never overwrite each other. What the server answers to a save is what
 * the page shows: it may have normalized or refused part of the change.
 */
export function useServerDocument<T>({ service, toParams, saveFailure, notLoaded }: Options<T>): {
  doc: T | null;
  loading: boolean;
  /** Why the last save failed, else why the load failed; null when neither did. */
  error: string | null;
  /** Saves the document that `change` makes from the latest stored one. Rejects (and sets `error`) when the save fails. */
  edit(change: (current: T) => T): Promise<void>;
  /** Sets or clears the error a save would set, for a step before the save (the hotkey capture) that failed. */
  setSaveError(message: string | null): void;
} {
  const [doc, setDoc] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  // The document the server last confirmed, which edits are built from.
  const latest = useRef<T | null>(null);
  const enqueue = useSerialQueue();

  // The latest options, so that callers can pass them inline without making `edit` change on every render.
  const options = useRef({ toParams, saveFailure, notLoaded });
  useEffect(() => {
    options.current = { toParams, saveFailure, notLoaded };
  });

  useEffect(() => {
    let cancelled = false;
    apiClient
      .call<T>(service, "get", {})
      .then((loaded) => {
        if (cancelled) return;
        latest.current = loaded;
        setDoc(loaded);
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
  }, [service]);

  const edit = useCallback(
    (change: (current: T) => T) =>
      enqueue(async () => {
        const current = latest.current;
        if (!current) throw new Error(options.current.notLoaded);

        const next = change(current);
        setSaveError(null);
        try {
          const saved = await apiClient.call<T>(service, "update", options.current.toParams(next));
          latest.current = saved;
          setDoc(saved);
        } catch (err) {
          setSaveError(failure(options.current.saveFailure, err));
          throw err;
        }
      }),
    [enqueue, service],
  );

  return { doc, loading, error: saveError ?? loadError, edit, setSaveError };
}
