/**
 * CitationWidget
 *
 * Renders a numbered list of footnote/citation references extracted from an
 * AI response. Each citation displays its source type icon, citation text,
 * and an optional URL link.
 *
 * Intended to give users a consolidated view of all sources referenced in an
 * AI-generated analysis, with one-click access to each source.
 *
 * NOT PCF-safe — React 19.
 */

import React from 'react';
import { makeStyles, tokens, Text, Link, mergeClasses } from '@fluentui/react-components';
import { DocumentRegular, GlobeRegular, BookOpenRegular, QuestionCircleRegular } from '@fluentui/react-icons';
import type { SourceWidgetProps } from '../types/widget-types';

// ---------------------------------------------------------------------------
// Payload type
// ---------------------------------------------------------------------------

export type CitationSourceType = 'web' | 'document' | 'legal' | 'other';

export interface Citation {
  /** Unique citation identifier. */
  id: string;
  /** Display index (1-based). */
  index: number;
  /** Full citation text. */
  text: string;
  /** Optional URL to the source. */
  url?: string;
  /** Type of source — used to pick the icon. */
  sourceType: CitationSourceType;
}

export interface CitationData {
  /** All citations from the AI response, in display order. */
  citations: Citation[];
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: 'auto',
    padding: tokens.spacingHorizontalM,
  },
  header: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    marginBottom: tokens.spacingVerticalM,
    flexShrink: 0,
  },
  list: {
    listStyleType: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  listItem: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingVerticalS,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    ':last-child': {
      borderBottomWidth: '0px',
    },
  },
  indexBadge: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: '24px',
    height: '24px',
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground2,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    flexShrink: 0,
    marginTop: '2px',
  },
  iconWrapper: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
    marginTop: '3px',
  },
  citationContent: {
    flexGrow: 1,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  citationText: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
  },
  citationLink: {
    fontSize: tokens.fontSizeBase200,
    wordBreak: 'break-all',
  },
  empty: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
  errorText: {
    color: tokens.colorPaletteCranberryForeground2,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function SourceIcon({ sourceType }: { sourceType: CitationSourceType }) {
  switch (sourceType) {
    case 'document':
      return <DocumentRegular fontSize={14} />;
    case 'web':
      return <GlobeRegular fontSize={14} />;
    case 'legal':
      return <BookOpenRegular fontSize={14} />;
    default:
      return <QuestionCircleRegular fontSize={14} />;
  }
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

function CitationWidget(props: SourceWidgetProps<CitationData>) {
  const { data, isLoading, error, className } = props;
  const styles = useStyles();

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.empty}>
          <BookOpenRegular fontSize={40} />
          <Text>Loading citations…</Text>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.empty}>
          <BookOpenRegular fontSize={40} />
          <Text className={styles.errorText}>{error}</Text>
        </div>
      </div>
    );
  }

  const citations = data?.citations ?? [];

  if (citations.length === 0) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.empty}>
          <BookOpenRegular fontSize={40} />
          <Text>No citations available.</Text>
        </div>
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      <Text className={styles.header}>
        {citations.length} {citations.length === 1 ? 'Citation' : 'Citations'}
      </Text>

      <ol className={styles.list}>
        {citations.map(citation => (
          <li key={citation.id} className={styles.listItem}>
            <span className={styles.indexBadge}>{citation.index}</span>

            <span className={styles.iconWrapper}>
              <SourceIcon sourceType={citation.sourceType} />
            </span>

            <div className={styles.citationContent}>
              <Text className={styles.citationText}>{citation.text}</Text>
              {citation.url && (
                <Link className={styles.citationLink} href={citation.url} target="_blank" rel="noopener noreferrer">
                  {citation.url}
                </Link>
              )}
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
}

export default CitationWidget;
