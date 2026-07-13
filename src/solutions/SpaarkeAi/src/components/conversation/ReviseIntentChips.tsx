/**
 * ComposeDocActionChips — the document-level action chips shown after a natural-language
 * "revise this document" auto-mounts the chat-uploaded file into Compose (spaarkeai-compose-r2
 * FIX #1 — editor-centric reframe).
 *
 * REFRAME RATIONALE: the prior surface showed whole-document revision-TYPE chips (align-clauses /
 * flag-risks / improve-clarity / custom) that dispatched `compose-revise-document`. That whole-
 * document flow is superseded by an EDITOR-CENTRIC model: once the file is mounted, the user
 * revises by highlighting text in the Compose editor and using the inline editing toolbar. The
 * chips here are now DOCUMENT-LEVEL actions (summarize / add-to-DMS / draft-email), each reusing an
 * EXISTING host mechanism (no new services — CLAUDE.md §11). The host (ConversationPane) owns the
 * dispatch; this component only renders the chips and collects the choice (single responsibility).
 *
 * ADR-021: Fluent UI v9 design tokens ONLY — no hardcoded colors; correct in dark mode.
 */

import * as React from "react";
import { Button, makeStyles, shorthands, tokens } from "@fluentui/react-components";
import {
  COMPOSE_DOC_ACTION_CHIPS,
  type ComposeDocAction,
} from "./composeReviseRouting";

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    rowGap: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  strip: {
    display: "flex",
    flexWrap: "wrap",
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalS,
  },
  chip: {
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground2,
    ...shorthands.borderColor(tokens.colorBrandStroke2),
    fontWeight: tokens.fontWeightRegular,
    ":hover": {
      backgroundColor: tokens.colorBrandBackground2Hover,
      color: tokens.colorBrandForeground2Hover,
    },
    ":hover:active": {
      backgroundColor: tokens.colorBrandBackground2Pressed,
      color: tokens.colorBrandForeground2Pressed,
    },
    ":disabled": {
      backgroundColor: tokens.colorNeutralBackgroundDisabled,
      color: tokens.colorNeutralForegroundDisabled,
      ...shorthands.borderColor(tokens.colorNeutralStrokeDisabled),
    },
  },
});

export interface ComposeDocActionChipsProps {
  /**
   * Dispatch the chosen document-level action. The host resolves the appropriate EXISTING mechanism
   * (compose-summarize dispatch / create-on-save + File Preview / Email widget_load) — see
   * ConversationPane.
   */
  readonly onAction: (action: ComposeDocAction) => void;
  /** Disables the whole surface (e.g. while the document session is still being established). */
  readonly disabled?: boolean;
}

/**
 * The three document-level action chips. Stateless — the host owns the dispatch.
 */
export function ComposeDocActionChips(props: ComposeDocActionChipsProps): React.JSX.Element {
  const { onAction, disabled } = props;
  const styles = useStyles();

  return (
    <div
      className={styles.root}
      role="group"
      aria-label="Document actions"
      data-testid="compose-doc-action-chips"
    >
      <div className={styles.strip}>
        {COMPOSE_DOC_ACTION_CHIPS.map((chip) => (
          <Button
            key={chip.id}
            className={styles.chip}
            appearance="secondary"
            size="small"
            shape="circular"
            disabled={disabled === true}
            onClick={() => onAction(chip.action)}
            aria-label={chip.label}
            data-testid={`compose-doc-action-chip-${chip.action}`}
            data-doc-action={chip.action}
          >
            {chip.label}
          </Button>
        ))}
      </div>
    </div>
  );
}
