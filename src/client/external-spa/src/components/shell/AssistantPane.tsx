/**
 * AssistantPane — the "Ask Legal" assistant BODY placeholder (task 011 §4).
 *
 * The dock chrome (the single "Ask Legal" header + move-side + collapse controls)
 * is owned by `PortalWorkspaceShell`; this component renders only the pane body:
 * a placeholder transcript area + a composer pinned to the BOTTOM (per the locked
 * UX refinement — one header, input at the bottom, no duplicated Quick Start cards).
 *
 * PLACEHOLDER — NOT wired in task 011. The production assistant is the shared
 * `SprkChat` component (`@spaarke/ui-components`), which requires a live BFF
 * session (apiBaseUrl + authenticatedFetch + getAccessToken) and the bounded
 * 2-tool catalog (policy_search + launch_wizard). That wiring is FR-26 (task 051),
 * gated by the P5 external-assistant security spike (task 050). Until then this
 * pane is a visual placeholder so the dockable-assistant chassis is demonstrable.
 * The composer is intentionally disabled to signal it is not yet functional.
 *
 * Fluent v9 semantic tokens only — zero hardcoded hex (ADR-021).
 */
import * as React from 'react';
import { makeStyles, shorthands, tokens, Text, Textarea, Button } from '@fluentui/react-components';
import { SparkleRegular, SendRegular } from '@fluentui/react-icons';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    minHeight: 0,
    gap: tokens.spacingVerticalM,
  },
  transcript: {
    flexGrow: 1,
    minHeight: 0,
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  intro: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingVerticalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
  },
  introTitle: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS },
  muted: { color: tokens.colorNeutralForeground3 },
  // Composer pinned to the bottom of the pane.
  composer: {
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  composerRow: {
    display: 'flex',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalS,
  },
  textarea: { flexGrow: 1 },
});

/**
 * @param unavailableReason Optional text shown in place of the default "coming soon"
 *   note (e.g. an entitlement/plane message once FR-26 gating lands).
 */
export interface AssistantPaneProps {
  unavailableReason?: string;
}

export const AssistantPane: React.FC<AssistantPaneProps> = ({ unavailableReason }) => {
  const s = useStyles();
  return (
    <div className={s.root}>
      <div className={s.transcript} role="log" aria-label="Assistant conversation">
        <div className={s.intro}>
          <span className={s.introTitle}>
            <SparkleRegular aria-hidden="true" />
            <Text weight="semibold">Ask Legal</Text>
          </span>
          <Text size={300} className={s.muted}>
            {unavailableReason ??
              'The Ask Legal assistant will help you search Policy & Procedures and start the ' +
                'right request. It arrives in a later release (FR-26) — this is a preview of where ' +
                'it will live.'}
          </Text>
        </div>
      </div>

      <div className={s.composer}>
        <div className={s.composerRow}>
          <Textarea
            className={s.textarea}
            placeholder="Ask a policy question… (coming soon)"
            resize="vertical"
            disabled
            aria-label="Message the Ask Legal assistant (not yet available)"
          />
          <Button
            appearance="primary"
            icon={<SendRegular />}
            disabled
            aria-label="Send message (not yet available)"
          />
        </div>
        <Text size={200} className={s.muted}>
          Assistant responses are not yet enabled.
        </Text>
      </div>
    </div>
  );
};

export default AssistantPane;
