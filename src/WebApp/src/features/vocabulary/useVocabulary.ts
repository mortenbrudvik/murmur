import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import { errorMessage, failure } from "../../interop/errorMessage";
import { useSerialQueue } from "../../hooks/useSerialQueue";
import type { Vocabulary } from "../../types/domain";

export function useVocabulary(): {
  vocabulary: Vocabulary | null;
  loading: boolean;
  error: string | null;
  /** Saves the whole document. Resolves once stored; rejects (and sets `error`) when the save fails. */
  save(v: Vocabulary): Promise<void>;
  /**
   * Saves the document that `change` makes from the latest stored one. Edits run one after another, so
   * several quick edits never overwrite each other. Rejects (and sets `error`) when the save fails.
   */
  edit(change: (current: Vocabulary) => Vocabulary): Promise<void>;
} {
  const [vocabulary, setVocabulary] = useState<Vocabulary | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  // The document the server last confirmed, which edits are built from.
  const latest = useRef<Vocabulary | null>(null);
  const enqueue = useSerialQueue();

  useEffect(() => {
    let cancelled = false;
    apiClient
      .call<Vocabulary>("vocabulary", "get", {})
      .then((loaded) => {
        if (cancelled) return;
        latest.current = loaded;
        setVocabulary(loaded);
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

  // The server replaces the whole document, so every save sends both lists, and what it returns
  // (trimmed, de-duplicated) is what the page shows.
  const send = useCallback(async (doc: Vocabulary) => {
    setSaveError(null);
    try {
      const saved = await apiClient.call<Vocabulary>("vocabulary", "update", {
        hintWords: doc.hintWords,
        replacements: doc.replacements,
      });
      latest.current = saved;
      setVocabulary(saved);
    } catch (err) {
      setSaveError(failure("Could not save the vocabulary", err));
      throw err;
    }
  }, []);

  const save = useCallback((doc: Vocabulary) => enqueue(() => send(doc)), [enqueue, send]);

  const edit = useCallback(
    (change: (current: Vocabulary) => Vocabulary) =>
      enqueue(async () => {
        const current = latest.current;
        if (!current) throw new Error("The vocabulary has not been loaded yet.");
        await send(change(current));
      }),
    [enqueue, send],
  );

  return { vocabulary, loading, error: saveError ?? loadError, save, edit };
}
