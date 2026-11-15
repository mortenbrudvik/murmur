import { useEffect, useRef, useState } from "react";
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
  const rows = useRef<HTMLTableSectionElement>(null);
  // The row being removed and how many rules there were. Its button had the focus and goes with the row, so the
  // focus moves to the row that takes its place, or to the new rule's first box when it was the last one.
  const removing = useRef<{ index: number; count: number } | null>(null);

  useEffect(() => {
    const removed = removing.current;
    if (!removed || replacements.length >= removed.count) return;
    removing.current = null;
    // Only the focus the removed button took with it: not where the user has gone since.
    if (document.activeElement && document.activeElement !== document.body) return;
    const next = rows.current?.querySelectorAll<HTMLButtonElement>(".vocab-rule-remove")[removed.index];
    (next ?? spokenInput.current)?.focus();
  }, [replacements]);

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

  async function removeRule(rule: ReplacementRule, index: number) {
    const mine = { index, count: replacements.length };
    removing.current = mine;
    const removed = await apply((v) => {
      const at = v.replacements.findIndex((r) => sameRule(r, rule));
      return at < 0 ? v : { ...v, replacements: v.replacements.filter((_, i) => i !== at) };
    });
    // A failed save leaves the row, and the focus on it.
    if (!removed && removing.current === mine) removing.current = null;
  }

  function onDraftKeyDown(e: KeyboardEvent) {
    if (!isPlainEnter(e)) return;
    e.preventDefault();
    void addRule();
  }

  return (
    <section className="page-section" aria-labelledby="vocab-rules-title">
      <h2 className="section-title" id="vocab-rules-title">Replacement rules</h2>
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
          <tbody ref={rows}>
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
                    onClick={() => void removeRule(rule, index)}
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
                <button type="button" className="btn-ghost" disabled={spokenDraft.trim() === ""} onClick={() => void addRule()}>
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
