import type { KeyboardEvent } from "react";

/** Enter, unless an input method is still composing text (Enter then confirms the composition). */
export function isPlainEnter(e: KeyboardEvent): boolean {
  return e.key === "Enter" && !e.nativeEvent.isComposing;
}
