import { useState } from "react";
import "./VocabularyPage.css";
import HintWordsSection from "./HintWordsSection";
import RulesSection from "./RulesSection";
import { useVocabulary } from "./useVocabulary";
import type { Vocabulary } from "../../types/domain";

/** Which part of the page the last change came from, so a failed save is reported next to it. */
type Section = "words" | "rules";

export default function VocabularyPage() {
  const { vocabulary, loading, error, edit } = useVocabulary();
  const [lastSection, setLastSection] = useState<Section>("words");

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

  const errorFor = (section: Section) => (lastSection === section ? error : null);

  return (
    <section className="page vocab">
      <h1 className="page-title">Vocabulary</h1>
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
          <HintWordsSection
            hintWords={vocabulary.hintWords}
            apply={(change) => apply("words", change)}
            error={errorFor("words")}
          />
          <RulesSection
            replacements={vocabulary.replacements}
            apply={(change) => apply("rules", change)}
            error={errorFor("rules")}
          />
        </>
      )}
    </section>
  );
}
