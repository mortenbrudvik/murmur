import { useServerDocument } from "../../hooks/useServerDocument";
import type { Vocabulary } from "../../types/domain";

// The server replaces the whole document, so every save sends both lists, and what it returns (trimmed,
// de-duplicated) is what the page shows.
const toParams = (doc: Vocabulary) => ({ hintWords: doc.hintWords, replacements: doc.replacements });

export function useVocabulary(): {
  vocabulary: Vocabulary | null;
  loading: boolean;
  error: string | null;
  /**
   * Saves the document that `change` makes from the latest stored one. Edits run one after another, so
   * several quick edits never overwrite each other. Rejects (and sets `error`) when the save fails.
   */
  edit(change: (current: Vocabulary) => Vocabulary): Promise<void>;
} {
  const { doc, loading, error, edit } = useServerDocument<Vocabulary>({
    service: "vocabulary",
    toParams,
    saveFailure: "Could not save the vocabulary",
    notLoaded: "The vocabulary has not been loaded yet.",
  });

  return { vocabulary: doc, loading, error, edit };
}
