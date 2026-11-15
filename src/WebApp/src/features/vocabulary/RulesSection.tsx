import { useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import { isPlainEnter } from "./isPlainEnter";
import type { ReplacementRule, Vocabulary } from "../../types/domain";

interface Props {
  replacements: ReplacementRule[];
  /** Saves the change; false when it failed (the page then shows `error`). */
  apply: (change: (current: Vocabulary) => Vocabulary) => Promise<boolean>;
  /** Why the last change made here could not be saved. */
  error: string | null;
}

const sameRule = (a: ReplacementRule, b: ReplacementRule) => a.spoken === b.spoken && a.written === b.written;

/** The replacement rules: a table of "when I hear" and "write", with a row at the foot that adds one. */
export default function RulesSection({ replacements, apply, error }: Props) {
  const [spokenDraft, setSpokenDraft] = useState("");
  const [writtenDraft, setWrittenDraft] = useState("");
  const spokenInput = useRef<HTMLInputElement>(null);

  async function addRule() {
    const spokenText = spokenDraft;
    const writtenText = writtenDraft;
    const rule: ReplacementRule = { spoken: spokenText.trim(), written: writtenText.trim() };
    if (!rule.spoken) return;

    const saved = await apply((v) => ({ ...v, replacements: [...v.replacements, rule] }));
    if (!saved) return;
    setSpokenDraft((current) => (current === spokenText ? "" : current));
    setWrittenDraft((current) => (current === writtenText ? "" : current));
    spokenInput.current?.focus();
  }

  function removeRule(rule: ReplacementRule) {
    void apply((v) => {
      const index = v.replacements.findIndex((r) => sameRule(r, rule));
      return index < 0 ? v : { ...v, replacements: v.replacements.filter((_, i) => i !== index) };
    });
  }

  function onDraftKeyDown(e: KeyboardEvent) {
    if (!isPlainEnter(e)) return;
    e.preventDefault();
    void addRule();
  }

  return (
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
            {replacements.map((rule, index) => (
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
            {replacements.length === 0 && (
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
                  onKeyDown={onDraftKeyDown}
                />
              </td>
              <td>
                <input
                  type="text"
                  className="text-input"
                  aria-label="New rule: write"
                  value={writtenDraft}
                  onChange={(e) => setWrittenDraft(e.target.value)}
                  onKeyDown={onDraftKeyDown}
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
      {error && (
        <p className="page-error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
