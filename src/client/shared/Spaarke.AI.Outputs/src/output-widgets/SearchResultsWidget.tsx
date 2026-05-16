/**
 * SearchResultsWidget
 *
 * Renders a ranked list of AI search result cards. Each card shows the result
 * title (optionally linked), a text excerpt, and a relevance score Badge.
 * Designed for use in the AI output pane when the AI returns a SearchResults
 * SSE payload.
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 *
 * Data is passed via props — no direct API calls inside this widget.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from "react";
import {
  makeStyles,
  mergeClasses,
  tokens,
  Text,
  Badge,
  Link,
  Card,
  CardHeader,
  Spinner,
} from "@fluentui/react-components";
import type { OutputWidgetProps } from "../types";

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

export interface SearchResultItem {
  /** Unique identifier for this result. */
  id: string;
  /** Document or page title. */
  title: string;
  /** Short excerpt or summary of the matching content. */
  excerpt: string;
  /**
   * Relevance score in [0, 1] range. Displayed as a percentage Badge
   * (e.g. 0.87 → "87%").
   */
  score: number;
  /** Optional URL — when present, the title renders as a clickable Link. */
  url?: string;
}

export interface SearchResultsData {
  /** The search query that produced these results. */
  query: string;
  /** Ordered list of search results (highest relevance first). */
  results: SearchResultItem[];
}

export type SearchResultsWidgetProps = OutputWidgetProps<SearchResultsData>;

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
  },
  queryHeader: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },
  queryLabel: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
  },
  queryText: {
    fontWeight: tokens.fontWeightSemibold,
  },
  resultList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  card: {
    width: "100%",
    boxSizing: "border-box",
  },
  cardBody: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  excerpt: {
    color: tokens.colorNeutralForeground2,
  },
  noResults: {
    color: tokens.colorNeutralForeground3,
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatScore(score: number): string {
  return `${Math.round(score * 100)}%`;
}

function scoreToBadgeAppearance(score: number): "filled" | "tint" | "ghost" | "outline" {
  return score >= 0.8 ? "filled" : score >= 0.5 ? "tint" : "ghost";
}

function scoreToBadgeColor(
  score: number
): "success" | "warning" | "important" | "informative" {
  if (score >= 0.8) return "success";
  if (score >= 0.5) return "informative";
  if (score >= 0.3) return "warning";
  return "important";
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * SearchResultsWidget renders the AI's search results as a Card list.
 * Each card shows:
 * - Title (linked if url is present)
 * - Excerpt text
 * - Relevance score as a colour-coded Badge
 */
export default function SearchResultsWidget({
  data,
  isLoading,
  error,
  className,
}: SearchResultsWidgetProps): React.ReactElement {
  const styles = useStyles();

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label="Searching..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      <div className={styles.queryHeader}>
        <Text size={200} className={styles.queryLabel}>
          Search results for
        </Text>
        <Text size={400} className={styles.queryText}>
          {data.query}
        </Text>
      </div>

      {data.results.length === 0 ? (
        <Text className={styles.noResults}>No results found.</Text>
      ) : (
        <div className={styles.resultList}>
          {data.results.map((result) => (
            <Card key={result.id} className={styles.card} appearance="filled-alternative">
              <CardHeader
                header={
                  result.url ? (
                    <Link href={result.url} target="_blank" rel="noopener noreferrer">
                      <Text size={400} weight="semibold">
                        {result.title}
                      </Text>
                    </Link>
                  ) : (
                    <Text size={400} weight="semibold">
                      {result.title}
                    </Text>
                  )
                }
                action={
                  <Badge
                    appearance={scoreToBadgeAppearance(result.score)}
                    color={scoreToBadgeColor(result.score)}
                    size="medium"
                  >
                    {formatScore(result.score)}
                  </Badge>
                }
              />
              <div className={styles.cardBody}>
                <Text size={300} className={styles.excerpt}>
                  {result.excerpt}
                </Text>
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
