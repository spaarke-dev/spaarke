/**
 * EmptyResultHint — Empty-result hint for Insights Assistant RAG responses
 * (R5 task 026 / D2-16).
 *
 * Per integration brief §4.4 anti-hallucination guarantee, when the RAG path
 * returns `citations: []` + `answer: ""`, the Assistant MUST NOT render the
 * empty `answer` verbatim. Instead, it renders this muted hint:
 *
 *   "I couldn't find anything for that. Try rephrasing or attaching files."
 *
 * Visually less alarming than a decline (no `MessageBar` framing) — uses a
 * muted `Text` block with `tokens.colorNeutralForeground3` per ADR-021
 * semantic-token discipline.
 *
 * ADR-021 (Fluent v9 + dark mode): semantic tokens only.
 * ADR-022 (React 19): functional component.
 *
 * @see projects/spaarke-ai-platform-unification-r5/notes/insights-engine-assistant-integration-brief.md §4.4
 * @see types.ts — `isEmptyResult` guard
 */

import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { SearchInfoRegular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'flex-start',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingHorizontalM,
    color: tokens.colorNeutralForeground3,
  },
  icon: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase500,
    flexShrink: 0,
  },
  text: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase400,
  },
});

// ---------------------------------------------------------------------------
// Constants — copy is exported for test assertion stability
// ---------------------------------------------------------------------------

/**
 * The exact hint text per spec FR-13. Exported so tests can assert the copy
 * without rebinding to brittle DOM-text matchers.
 */
export const EMPTY_RESULT_HINT_TEXT =
  "I couldn't find anything for that. Try rephrasing or attaching files.";

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const EmptyResultHint: React.FC = () => {
  const styles = useStyles();
  return (
    <div
      className={styles.container}
      data-testid="empty-result-hint"
      role="status"
      aria-live="polite"
    >
      <SearchInfoRegular className={styles.icon} aria-hidden="true" />
      <Text className={styles.text}>{EMPTY_RESULT_HINT_TEXT}</Text>
    </div>
  );
};

export default EmptyResultHint;
