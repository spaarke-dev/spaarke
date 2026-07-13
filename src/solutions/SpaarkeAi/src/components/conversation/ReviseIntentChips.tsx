/**
 * ReviseIntentChips — the four clickable "what type of revision?" options shown after a
 * natural-language "revise this document" auto-mounts the chat-uploaded file into Compose
 * (spaarkeai-compose-r2 Wave 4 — end-to-end revise).
 *
 * WHY A DEDICATED CHIP SURFACE (not `ConsumerChips`): the general Click-path `ConsumerChips`
 * dispatches each chip's `bindingId` through `dispatchConsumer` into the CHAT session. A revise chip
 * must instead dispatch `compose-revise-document` into the DOCUMENT session (with `revisionScope:
 * 'whole-document'`) so the edits materialize as an inline redline rather than narrated prose — the
 * host wires that via `dispatchComposeAction`. All four chips share the SAME resolved
 * `compose-revise-document` bindingId and differ only by their `revisionIntent` slot; `ConsumerChips`
 * models one bindingId per chip, so a small purpose-built surface is the honest fit.
 *
 * "Custom…" reveals an inline free-text field (owner-confirmed UX): the user's instruction becomes
 * the `instruction` slot with `revisionIntent: 'custom'`.
 *
 * ADR-021: Fluent UI v9 design tokens ONLY — no hardcoded colors; correct in dark mode.
 */

import * as React from "react";
import {
  Button,
  Input,
  makeStyles,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import { SendRegular } from "@fluentui/react-icons";
import {
  REVISE_CHIP_OPTIONS,
  type RevisionIntent,
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
  customRow: {
    display: "flex",
    columnGap: tokens.spacingHorizontalS,
    alignItems: "center",
  },
  customInput: {
    flexGrow: 1,
  },
});

export interface ReviseIntentChipsProps {
  /**
   * Dispatch the chosen revision. `instruction` is present only for the "custom" intent (the free
   * text the user typed). The host resolves the `compose-revise-document` bindingId + document
   * session and routes the dispatch (see ConversationPane).
   */
  readonly onPick: (revisionIntent: RevisionIntent, instruction?: string) => void;
  /** Disables the whole surface (e.g. while the document session is still being established). */
  readonly disabled?: boolean;
}

/**
 * The four revise-intent chips. Stateless except for the local "custom" free-text reveal — the host
 * owns the dispatch (single responsibility: this component renders + collects the choice).
 */
export function ReviseIntentChips(props: ReviseIntentChipsProps): React.JSX.Element {
  const { onPick, disabled } = props;
  const styles = useStyles();
  const [customOpen, setCustomOpen] = React.useState(false);
  const [customText, setCustomText] = React.useState("");

  const handleChipClick = React.useCallback(
    (revisionIntent: RevisionIntent): void => {
      if (revisionIntent === "custom") {
        setCustomOpen(true);
        return;
      }
      onPick(revisionIntent);
    },
    [onPick]
  );

  const submitCustom = React.useCallback((): void => {
    const instruction = customText.trim();
    if (instruction.length === 0) return;
    onPick("custom", instruction);
    setCustomText("");
    setCustomOpen(false);
  }, [customText, onPick]);

  return (
    <div
      className={styles.root}
      role="group"
      aria-label="Choose a revision type"
      data-testid="revise-intent-chips"
    >
      <div className={styles.strip}>
        {REVISE_CHIP_OPTIONS.map((option) => (
          <Button
            key={option.revisionIntent}
            className={styles.chip}
            appearance="secondary"
            size="small"
            shape="circular"
            disabled={disabled === true}
            onClick={() => handleChipClick(option.revisionIntent)}
            aria-label={option.label}
            data-testid={`revise-intent-chip-${option.revisionIntent}`}
            data-revision-intent={option.revisionIntent}
          >
            {option.label}
          </Button>
        ))}
      </div>

      {customOpen && (
        <div className={styles.customRow}>
          <Input
            className={styles.customInput}
            size="small"
            value={customText}
            placeholder="Describe the revision you want…"
            disabled={disabled === true}
            aria-label="Custom revision instruction"
            data-testid="revise-custom-input"
            onChange={(_e, data) => setCustomText(data.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                submitCustom();
              }
            }}
          />
          <Button
            appearance="primary"
            size="small"
            icon={<SendRegular />}
            disabled={disabled === true || customText.trim().length === 0}
            onClick={submitCustom}
            aria-label="Apply custom revision"
            data-testid="revise-custom-submit"
          >
            Apply
          </Button>
        </div>
      )}
    </div>
  );
}
