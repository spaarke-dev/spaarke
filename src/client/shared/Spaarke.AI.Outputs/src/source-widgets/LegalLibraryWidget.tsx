/**
 * LegalLibraryWidget
 *
 * Renders a structured card for a legal case or statute citation. Displays
 * the citation reference, court, date, excerpt, and an optional external link.
 *
 * Designed for legal workspace use cases where AI responses reference cases
 * from a legal research database or Bing Legal Grounding.
 *
 * NOT PCF-safe — React 19.
 */

import React from "react";
import {
  makeStyles,
  tokens,
  Card,
  CardHeader,
  Text,
  Link,
  Divider,
  mergeClasses,
} from "@fluentui/react-components";
import { BookOpenRegular, CalendarRegular, BuildingBankRegular, OpenRegular } from "@fluentui/react-icons";
import type { SourceWidgetProps } from "../types/widget-types";

// ---------------------------------------------------------------------------
// Payload type
// ---------------------------------------------------------------------------

export interface LegalLibraryData {
  /** Citation reference string, e.g. "Brown v. Board of Education, 347 U.S. 483 (1954)". */
  citation: string;
  /** Name of the court that issued the ruling. */
  court?: string;
  /** Decision date as a display string, e.g. "May 17, 1954". */
  date?: string;
  /** Excerpt from the case or statute (may include markdown). */
  excerpt: string;
  /** Optional external URL to the full text. */
  url?: string;
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
    overflow: "auto",
    padding: tokens.spacingHorizontalM,
  },
  card: {
    width: "100%",
    backgroundColor: tokens.colorNeutralBackground2,
  },
  citationTitle: {
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase400,
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  metaRow: {
    display: "flex",
    flexWrap: "wrap",
    gap: tokens.spacingHorizontalM,
    marginBottom: tokens.spacingVerticalS,
  },
  metaItem: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  divider: {
    marginTop: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalM,
  },
  excerptLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    textTransform: "uppercase",
    letterSpacing: "0.05em",
    marginBottom: tokens.spacingVerticalXS,
  },
  excerpt: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground2,
    borderLeftWidth: "3px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorBrandStroke1,
    paddingLeft: tokens.spacingHorizontalM,
    fontStyle: "italic",
    margin: 0,
  },
  linkRow: {
    marginTop: tokens.spacingVerticalM,
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
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
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

function LegalLibraryWidget(props: SourceWidgetProps<LegalLibraryData>) {
  const { data, isLoading, error, className } = props;
  const styles = useStyles();

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.fallback}>
          <BookOpenRegular fontSize={40} />
          <Text>Loading legal reference…</Text>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.fallback}>
          <BookOpenRegular fontSize={40} />
          <Text className={styles.errorText}>{error}</Text>
        </div>
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      <Card className={styles.card} appearance="filled-alternative">
        <CardHeader
          header={
            <Text className={styles.citationTitle}>{data?.citation}</Text>
          }
        />

        {(data?.court || data?.date) && (
          <div className={styles.metaRow}>
            {data?.court && (
              <span className={styles.metaItem}>
                <BuildingBankRegular fontSize={14} />
                <Text size={200}>{data.court}</Text>
              </span>
            )}
            {data?.date && (
              <span className={styles.metaItem}>
                <CalendarRegular fontSize={14} />
                <Text size={200}>{data.date}</Text>
              </span>
            )}
          </div>
        )}

        <Divider className={styles.divider} />

        <Text className={styles.excerptLabel}>Excerpt</Text>
        <blockquote className={styles.excerpt}>
          {data?.excerpt}
        </blockquote>

        {data?.url && (
          <div className={styles.linkRow}>
            <Link href={data.url} target="_blank" rel="noopener noreferrer">
              View full text
            </Link>
            <OpenRegular fontSize={14} />
          </div>
        )}
      </Card>
    </div>
  );
}

export default LegalLibraryWidget;
