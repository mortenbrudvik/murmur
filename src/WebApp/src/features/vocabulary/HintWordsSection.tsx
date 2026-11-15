import { useEffect, useRef, useState } from "react";
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
  const chips = useRef<HTMLUListElement>(null);
  const input = useRef<HTMLInputElement>(null);
  // The word being removed and where its chip was. Its button had the focus and goes with the chip, so the focus
  // moves to the chip that takes its place, or to the box when it was the last one.
  const removing = useRef<{ word: string; index: number } | null>(null);

  useEffect(() => {
    const removed = removing.current;
    if (!removed || hintWords.includes(removed.word)) return;
    removing.current = null;
    // Only the focus the removed button took with it: not where the user has gone since.
    if (document.activeElement && document.activeElement !== document.body) return;
    const next = chips.current?.querySelectorAll("button")[removed.index];
    (next ?? input.current)?.focus();
  }, [hintWords]);

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

  async function removeWord(word: string, index: number) {
    removing.current = { word, index };
    const removed = await apply((v) => ({ ...v, hintWords: v.hintWords.filter((w) => w !== word) }));
    // A failed save leaves the chip, and the focus on it.
    if (!removed && removing.current?.word === word) removing.current = null;
  }

  return (
    <section className="page-section" aria-labelledby="vocab-words-title">
      <h2 className="section-title" id="vocab-words-title">Hint words</h2>
      <div className="vocab-words">
        {hintWords.length > 0 ? (
          <ul className="vocab-chips" aria-label="Hint words" ref={chips}>
            {hintWords.map((word, index) => (
              <li className="vocab-chip" key={word}>
                <span>{word}</span>
                <button type="button" aria-label={`Remove ${word}`} title="Remove" onClick={() => void removeWord(word, index)}>
                  ×
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="page-muted">No hint words yet.</p>
        )}
        <input
          ref={input}
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
