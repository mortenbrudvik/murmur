import { useState } from "react";
import { isPlainEnter } from "./isPlainEnter";
import type { Vocabulary } from "../../types/domain";

interface Props {
  hintWords: string[];
  /** Saves the change; false when it failed (the page then shows `error`). */
  apply: (change: (current: Vocabulary) => Vocabulary) => Promise<boolean>;
  /** Why the last change made here could not be saved. */
  error: string | null;
}

/** The hint words: chips with a remove button, and a box that adds one on Enter. */
export default function HintWordsSection({ hintWords, apply, error }: Props) {
  const [wordDraft, setWordDraft] = useState("");

  async function addWord() {
    const draft = wordDraft;
    const word = draft.trim();
    if (!word) return;

    // The draft is only cleared if the user has not typed on since, and never when the save failed.
    const clearDraft = () => setWordDraft((current) => (current === draft ? "" : current));

    if (hintWords.some((w) => w.toLowerCase() === word.toLowerCase())) {
      clearDraft();
      return;
    }
    if (await apply((v) => ({ ...v, hintWords: [...v.hintWords, word] }))) clearDraft();
  }

  function removeWord(word: string) {
    void apply((v) => ({ ...v, hintWords: v.hintWords.filter((w) => w !== word) }));
  }

  return (
    <section className="page-section" aria-labelledby="vocab-words-title">
      <h2 id="vocab-words-title">Hint words</h2>
      <div className="vocab-words">
        {hintWords.length > 0 ? (
          <ul className="vocab-chips" aria-label="Hint words">
            {hintWords.map((word) => (
              <li className="vocab-chip" key={word}>
                <span>{word}</span>
                <button type="button" aria-label={`Remove ${word}`} title="Remove" onClick={() => removeWord(word)}>
                  ×
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="page-muted">No hint words yet.</p>
        )}
        <input
          type="text"
          className="text-input vocab-word-input"
          placeholder="Add a word…"
          aria-label="Add a hint word"
          value={wordDraft}
          onChange={(e) => setWordDraft(e.target.value)}
          onKeyDown={(e) => {
            if (!isPlainEnter(e)) return;
            e.preventDefault();
            void addWord();
          }}
        />
      </div>
      {error && (
        <p className="page-error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
