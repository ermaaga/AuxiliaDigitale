/** The words of a text as axe compares them: lower case, punctuation and symbols dropped. */
function words(text: string): string[] {
  return text
    .toLocaleLowerCase()
    .replace(/[^\p{L}\p{N}\s]/gu, " ")
    .split(/\s+/)
    .filter(Boolean);
}

/**
 * The accessible name of a control whose visible text is generic ("Edit", "Run now") and whose `aria-label` adds the
 * object ("Rename Tag A"). WCAG 2.5.3 (Label in Name, checked by axe `label-content-name-mismatch`) wants the visible
 * words, in order, inside the name, so speech users can say what they see. The translations do not always do that
 * ("Open again" / "Open Task A again"), and differ by language: the label is kept when it already contains the visible
 * text, otherwise the visible text goes first (`Open again: Open Task A again`).
 */
export function labelInName(visible: string, label: string): string {
  const shown = words(visible);
  const named = words(label);
  const contained =
    shown.length === 0 ||
    named.some((_, start) => shown.every((word, offset) => named[start + offset] === word));
  return contained ? label : `${visible}: ${label}`;
}
