import { useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import "./VocabularyPage.css";
import { useVocabulary } from "./useVocabulary";
import type { ReplacementRule, Vocabulary } from "../../types/domain";

/** Which part of the page the last change came from, so a failed save is reported next to it. */
type Section = "words" | "rules";

const sameRule = (a: ReplacementRule, b: ReplacementRule) => a.spoken === b.spoken && a.written === b.written;

/** Enter, unless an input method is still composing text (Enter then confirms the composition). */
function isPlainEnter(e: KeyboardEvent): boolean {
  return e.key === "Enter" && !e.nativeEvent.isComposing;
}

export default function VocabularyPage() {
  const { vocabulary, loading, error, edit } = useVocabulary();
  const [wordDraft, setWordDraft] = useState("");
  const [spokenDraft, setSpokenDraft] = useState("");
  const [writtenDraft, setWrittenDraft] = useState("");
  const [lastSection, setLastSection] = useState<Section>("words");
  const spokenInput = useRef<HTMLInputElement>(null);

  /** Saves the change; a failure is shown by the hook as `error`, so callers only need to know it did not work. */
  async function apply(section: Section, change: (current: Vocabulary) => Vocabulary): Promise<boolean> {
    setLastSection(section);
    try {
      await edit(change);
      return true;
    } catch {
      return false;
    }
  }

  async function addWord() {
    const draft = wordDraft;
    const word = draft.trim();
    if (!word || !vocabulary) return;

    // The draft is only cleared if the user has not typed on since, and never when the save failed.
    const clearDraft = () => setWordDraft((current) => (current === draft ? "" : current));

    if (vocabulary.hintWords.some((w) => w.toLowerCase() === word.toLowerCase())) {
      clearDraft();
      return;
    }
    if (await apply("words", (v) => ({ ...v, hintWords: [...v.hintWords, word] }))) clearDraft();
  }

  async function addRule() {
    const spokenText = spokenDraft;
    const writtenText = writtenDraft;
    const rule: ReplacementRule = { spoken: spokenText.trim(), written: writtenText.trim() };
    if (!rule.spoken) return;

    const saved = await apply("rules", (v) => ({ ...v, replacements: [...v.replacements, rule] }));
    if (!saved) return;
    setSpokenDraft((current) => (current === spokenText ? "" : current));
    setWrittenDraft((current) => (current === writtenText ? "" : current));
    spokenInput.current?.focus();
  }

  function removeWord(word: string) {
    void apply("words", (v) => ({ ...v, hintWords: v.hintWords.filter((w) => w !== word) }));
  }

  function removeRule(rule: ReplacementRule) {
    void apply("rules", (v) => {
      const index = v.replacements.findIndex((r) => sameRule(r, rule));
      return index < 0 ? v : { ...v, replacements: v.replacements.filter((_, i) => i !== index) };
    });
  }

  return (
    <section className="page vocab">
      <h1>Vocabulary</h1>
      <p className="page-help">
        Hint words help Murmur spell names and jargon. Rules replace what it hears with what you want written.
      </p>

      {error && !vocabulary && (
        <p className="page-error" role="alert">
          {error}
        </p>
      )}
      {loading && !vocabulary && <p className="page-muted">Loading…</p>}

      {vocabulary && (
        <>
          <section className="page-section" aria-labelledby="vocab-words-title">
            <h2 id="vocab-words-title">Hint words</h2>
            <div className="vocab-words">
              {vocabulary.hintWords.length > 0 ? (
                <ul className="vocab-chips" aria-label="Hint words">
                  {vocabulary.hintWords.map((word) => (
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
            {error && lastSection === "words" && (
              <p className="page-error" role="alert">
                {error}
              </p>
            )}
          </section>

          <section className="page-section" aria-labelledby="vocab-rules-title">
            <h2 id="vocab-rules-title">Replacement rules</h2>
            <div className="vocab-rules">
              <table aria-label="Replacement rules">
                <thead>
                  <tr>
                    <th scope="col">When I hear</th>
                    <th scope="col">Write</th>
                    <th scope="col" className="vocab-rule-end">
                      <span className="visually-hidden">Remove</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {vocabulary.replacements.map((rule, index) => (
                    <tr key={index}>
                      <td>{rule.spoken}</td>
                      <td>{rule.written}</td>
                      <td className="vocab-rule-end">
                        <button
                          type="button"
                          className="vocab-rule-remove"
                          aria-label={`Remove rule for ${rule.spoken}`}
                          title="Remove"
                          onClick={() => removeRule(rule)}
                        >
                          ×
                        </button>
                      </td>
                    </tr>
                  ))}
                  {vocabulary.replacements.length === 0 && (
                    <tr>
                      <td colSpan={3} className="page-muted">
                        No rules yet.
                      </td>
                    </tr>
                  )}
                </tbody>
                <tfoot>
                  <tr>
                    <td>
                      <input
                        ref={spokenInput}
                        type="text"
                        className="text-input"
                        aria-label="New rule: when I hear"
                        value={spokenDraft}
                        onChange={(e) => setSpokenDraft(e.target.value)}
                        onKeyDown={(e) => {
                          if (!isPlainEnter(e)) return;
                          e.preventDefault();
                          void addRule();
                        }}
                      />
                    </td>
                    <td>
                      <input
                        type="text"
                        className="text-input"
                        aria-label="New rule: write"
                        value={writtenDraft}
                        onChange={(e) => setWrittenDraft(e.target.value)}
                        onKeyDown={(e) => {
                          if (!isPlainEnter(e)) return;
                          e.preventDefault();
                          void addRule();
                        }}
                      />
                    </td>
                    <td className="vocab-rule-end">
                      <button type="button" className="vocab-add" disabled={spokenDraft.trim() === ""} onClick={() => void addRule()}>
                        Add
                      </button>
                    </td>
                  </tr>
                </tfoot>
              </table>
            </div>
            {error && lastSection === "rules" && (
              <p className="page-error" role="alert">
                {error}
              </p>
            )}
          </section>
        </>
      )}
    </section>
  );
}
