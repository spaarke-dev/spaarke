/**
 * DeclineResponseRenderer — Playbook-decline path renderer for Insights
 * Assistant responses (R5 task 026 / D2-16).
 *
 * Per integration brief §4.5: the playbook path returns 200 OK (NOT an error)
 * when the playbook's evidence-sufficiency gate fails. The response carries:
 *
 *   - `answer` = decline's `Explanation`
 *   - `structuredResult.envelope.suggestedActions` = plain-string list
 *   - `structuredResult.envelope.minimumEvidenceNeeded` (optional)
 *
 * Renders as a Fluent v9 `MessageBar intent="warning"` (visually distinct from
 * `intent="error"` reserved for ProblemDetails errors handled in task 029).
 * Below the MessageBar we render a bulleted list of `suggestedActions` as
 * plain text (NOT Fluent Buttons — per integration brief §6 D3 decision,
 * actionable buttons are R6 backlog).
 *
 * ADR-021 (Fluent v9 + dark mode): semantic tokens only.
 * ADR-022 (React 19): functional component + hooks.
 * ADR-013 §3.5 (Zone B boundary): consumes the HTTP response envelope only.
 *
 * @see types.ts — PlaybookDeclineResponse + DeclineEnvelope types
 * @see projects/spaarke-ai-platform-unification-r5/notes/insights-engine-assistant-integration-brief.md §4.5
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  MessageBar,
  MessageBarBody,
  Text,
} from '@fluentui/react-components';
import type { PlaybookDeclineResponse } from './types';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface DeclineResponseRendererProps {
  /** Discriminated-union response narrowed to the playbook-decline variant. */
  readonly response: PlaybookDeclineResponse;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    width: '100%',
  },
  suggestedHeader: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    textTransform: 'uppercase',
    letterSpacing: tokens.strokeWidthThin,
  },
  suggestedList: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalXL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground1,
  },
  suggestedItem: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase400,
    color: tokens.colorNeutralForeground1,
  },
  minEvidenceText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
    lineHeight: tokens.lineHeightBase300,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DeclineResponseRenderer: React.FC<DeclineResponseRendererProps> = ({
  response,
}) => {
  const styles = useStyles();
  const envelope = response.structuredResult.envelope;

  // The outer `answer` field is the decline `Explanation` per brief §4.5.
  // Prefer the outer field (always present) and fall back to the envelope's
  // own `explanation` if the outer is empty.
  const explanation = response.answer.length > 0
    ? response.answer
    : envelope.explanation;

  // Suggested actions — plain-text strings only per integration brief §6 D3.
  // No Fluent `Button` row (that is R6 backlog per the v1 decline-UX decision).
  const suggestedActions = envelope.suggestedActions ?? [];

  return (
    <div className={styles.container} data-testid="decline-response-renderer">
      <MessageBar intent="warning" data-testid="decline-warning">
        <MessageBarBody>{explanation}</MessageBarBody>
      </MessageBar>

      {envelope.minimumEvidenceNeeded && envelope.minimumEvidenceNeeded.length > 0 && (
        <Text
          className={styles.minEvidenceText}
          data-testid="decline-min-evidence"
        >
          Minimum evidence needed: {envelope.minimumEvidenceNeeded}
        </Text>
      )}

      {suggestedActions.length > 0 && (
        <>
          <Text className={styles.suggestedHeader}>Suggested actions</Text>
          <ul
            className={styles.suggestedList}
            data-testid="decline-suggested-actions"
          >
            {suggestedActions.map((action, idx) => (
              <li
                key={`action-${idx}-${action.slice(0, 24)}`}
                className={styles.suggestedItem}
                data-testid={`decline-action-${idx}`}
              >
                {action}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
};

export default DeclineResponseRenderer;
