/**
 * EmailStubWidget.tsx — placeholder Email surface (FIX #10b, UAT).
 *
 * Compose is the default drafting surface; the explicit email path is a STUB
 * for now. The Compose editor's "Email" toolbar affordance
 * (`ComposeAiToolbar` → `handleEmailAction`) dispatches a `workspace`
 * `widget_load` with `widgetType: 'email'` and
 * `widgetData: { layoutName: 'Email', mode: 'open' | 'send', ... }`.
 * `WorkspacePane` resolves THIS component for that widget type and opens a tab
 * that renders this stub.
 *
 * This is intentionally minimal but REAL — the tab actually opens and renders
 * the mode (+ the attachment file name in send-mode / a body preview in
 * open-mode). A future task replaces it with the real email compose experience.
 *
 * Standards:
 *   - ADR-021: Fluent v9 semantic tokens only — dark-mode correct, no hex.
 *   - ADR-022: React 19 functional component.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Card,
  CardHeader,
  Text,
  Badge,
} from "@fluentui/react-components";
import { Mail24Regular, Attach16Regular } from "@fluentui/react-icons";
import type { WorkspaceWidgetProps } from "@spaarke/ai-widgets";

/** Payload carried on the `widget_load` event by the Compose "Email" affordance. */
export interface EmailWidgetData {
  /** Always `'Email'` — mirrors the shared chip contract. */
  layoutName?: string;
  /** `'open'` = open drafted text in a new email; `'send'` = attach the current file. */
  mode?: "open" | "send";
  /** Present in `'open'` mode — the drafted document body (plain text). */
  bodyText?: string;
  /** Present in `'send'` mode — the file name added as an attachment. */
  attachmentFileName?: string;
}

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    boxSizing: "border-box",
    padding: tokens.spacingHorizontalXXL,
    gap: tokens.spacingVerticalL,
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1,
    overflow: "auto",
  },
  card: {
    maxWidth: "560px",
    width: "100%",
    alignSelf: "center",
    marginTop: tokens.spacingVerticalXXL,
  },
  detailRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground2,
  },
  bodyPreview: {
    marginTop: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
    maxHeight: "220px",
    overflow: "auto",
    whiteSpace: "pre-wrap",
    fontFamily: tokens.fontFamilyBase,
  },
});

/** Max characters of drafted body shown in the open-mode preview. */
const BODY_PREVIEW_CAP = 2000;

export function EmailStubWidget(props: WorkspaceWidgetProps<EmailWidgetData>): React.JSX.Element {
  const styles = useStyles();
  const data = props.data ?? {};
  const mode = data.mode ?? "open";

  const rawBody = data.bodyText ?? "";
  const bodyPreview =
    rawBody.length > BODY_PREVIEW_CAP ? `${rawBody.slice(0, BODY_PREVIEW_CAP)}…` : rawBody;

  return (
    <div className={styles.root} data-testid="email-stub-widget">
      <Card className={styles.card} appearance="outline">
        <CardHeader
          image={<Mail24Regular />}
          header={
            <Text weight="semibold" size={500}>
              Email — coming soon
            </Text>
          }
          description={
            <Badge appearance="tint" color="informative" size="small">
              {mode === "send" ? "Send in Email" : "Open in Email"}
            </Badge>
          }
        />

        {mode === "send" ? (
          <div className={styles.detailRow} data-testid="email-stub-attachment">
            <Attach16Regular />
            <Text size={300}>
              Attachment: {data.attachmentFileName ?? "Untitled.docx"}
            </Text>
          </div>
        ) : (
          <>
            <div className={styles.detailRow}>
              <Text size={300}>The drafted document will open as the email body.</Text>
            </div>
            {bodyPreview.length > 0 ? (
              <div className={styles.bodyPreview} data-testid="email-stub-body">
                {bodyPreview}
              </div>
            ) : null}
          </>
        )}
      </Card>
    </div>
  );
}

EmailStubWidget.displayName = "EmailStubWidget";

export default EmailStubWidget;
