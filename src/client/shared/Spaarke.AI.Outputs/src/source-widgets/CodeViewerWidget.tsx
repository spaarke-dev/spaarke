/**
 * CodeViewerWidget
 *
 * Renders syntax-highlighted code blocks. No third-party syntax-highlighting
 * libraries are used (constraint: no highlight.js, prism). Instead:
 * - Uses Fluent v9 `tokens.fontFamilyMonospace` for the code font.
 * - Uses `tokens.colorNeutralBackground3` for the code block background.
 * - Line numbers are rendered in a separate sticky left column.
 * - A language badge is displayed at the top-right.
 * - Copy-to-clipboard button via the Clipboard API.
 *
 * Dark mode support is automatic via Fluent design tokens.
 *
 * NOT PCF-safe — React 19.
 */

import React, { useCallback, useState } from "react";
import {
  makeStyles,
  tokens,
  Button,
  Badge,
  Text,
  mergeClasses,
} from "@fluentui/react-components";
import {
  CopyRegular,
  CheckmarkRegular,
  CodeRegular,
} from "@fluentui/react-icons";
import type { SourceWidgetProps } from "../types/widget-types";

// ---------------------------------------------------------------------------
// Payload type
// ---------------------------------------------------------------------------

export interface CodeViewerData {
  /** The code string to display. */
  code: string;
  /** Programming language identifier (used as label; no syntax highlighting lib). */
  language?: string;
  /** Whether to display line numbers in the gutter. Defaults to true. */
  showLineNumbers?: boolean;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
  },
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke1,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
    minHeight: "36px",
  },
  languageBadge: {
    textTransform: "uppercase",
    letterSpacing: "0.04em",
    fontSize: tokens.fontSizeBase100,
  },
  copyButton: {
    // Compact button aligned right
  },
  scrollContainer: {
    flexGrow: 1,
    overflow: "auto",
    backgroundColor: tokens.colorNeutralBackground3,
  },
  preBlock: {
    margin: 0,
    padding: 0,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase300,
    display: "flex",
    minHeight: "100%",
    color: tokens.colorNeutralForeground1,
  },
  lineNumbers: {
    display: "flex",
    flexDirection: "column",
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalS}`,
    backgroundColor: tokens.colorNeutralBackground4,
    borderRightWidth: "1px",
    borderRightStyle: "solid",
    borderRightColor: tokens.colorNeutralStroke2,
    userSelect: "none",
    flexShrink: 0,
    textAlign: "right",
    color: tokens.colorNeutralForeground4,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase300,
    minWidth: "2.5em",
  },
  lineNumberItem: {
    display: "block",
  },
  codeContent: {
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    whiteSpace: "pre",
    flexGrow: 1,
    outline: "none",
  },
  fallback: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
  errorText: {
    color: tokens.colorPaletteCranberryForeground2,
  },
  copiedIndicator: {
    color: tokens.colorPaletteGreenForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

function CodeViewerWidget(props: SourceWidgetProps<CodeViewerData>) {
  const { data, isLoading, error, className } = props;
  const styles = useStyles();

  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(async () => {
    if (!data?.code) return;
    try {
      await navigator.clipboard.writeText(data.code);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard API may fail in sandboxed contexts — silently ignore
    }
  }, [data?.code]);

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.fallback}>
          <CodeRegular fontSize={40} />
          <Text>Loading code…</Text>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.fallback}>
          <CodeRegular fontSize={40} />
          <Text className={styles.errorText}>{error}</Text>
        </div>
      </div>
    );
  }

  const code = data?.code ?? "";
  const language = data?.language;
  const showLineNumbers = data?.showLineNumbers !== false; // default true
  const lines = code.split("\n");

  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* Header: language badge + copy button */}
      <div className={styles.header}>
        {language ? (
          <Badge
            className={styles.languageBadge}
            appearance="outline"
            color="informative"
          >
            {language}
          </Badge>
        ) : (
          <span />
        )}

        <Button
          className={styles.copyButton}
          appearance="subtle"
          size="small"
          icon={
            copied ? (
              <CheckmarkRegular className={styles.copiedIndicator} />
            ) : (
              <CopyRegular />
            )
          }
          onClick={handleCopy}
          aria-label={copied ? "Copied!" : "Copy code to clipboard"}
        >
          {copied ? "Copied!" : "Copy"}
        </Button>
      </div>

      {/* Code block */}
      <div className={styles.scrollContainer}>
        <pre className={styles.preBlock}>
          {showLineNumbers && (
            <span
              className={styles.lineNumbers}
              aria-hidden="true"
            >
              {lines.map((_, i) => (
                <span key={i} className={styles.lineNumberItem}>
                  {i + 1}
                </span>
              ))}
            </span>
          )}
          <code
            className={styles.codeContent}
            tabIndex={0}
          >
            {code}
          </code>
        </pre>
      </div>
    </div>
  );
}

export default CodeViewerWidget;
