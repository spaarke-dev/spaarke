/**
 * ResultsList component
 *
 * Scrollable container for search result cards with infinite scroll support.
 * Displays result count header and sentinel element for load-more trigger.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 * @see spec.md for DOM cap and infinite scroll rules
 */

import * as React from 'react';
import { useCallback, useState } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  Link,
  Button,
  Tooltip,
} from '@fluentui/react-components';
import { ArrowClockwise20Regular, Add20Regular, Open20Regular, MailRegular } from '@fluentui/react-icons';
import { IResultsListProps, SearchResult } from '../types';
import { ResultCard } from './ResultCard';
// `useInfiniteScroll` no longer used — "Show more" + sentinel were removed
// 2026-05-12. Users access the long-tail via the "Open full viewer" toolbar icon.

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    overflow: 'hidden',
  },
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  resultCount: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  scrollContainer: {
    flex: 1,
    overflowY: 'auto',
    padding: tokens.spacingHorizontalM,
  },
  resultsList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  sentinel: {
    height: '1px',
    width: '100%',
  },
  loadingMore: {
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    padding: tokens.spacingVerticalM,
    gap: tokens.spacingHorizontalS,
  },
  showMoreLink: {
    display: 'flex',
    justifyContent: 'center',
    padding: tokens.spacingVerticalM,
  },
  domCapMessage: {
    padding: tokens.spacingVerticalM,
    textAlign: 'center' as const,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  infoButton: {
    minWidth: 'auto',
    padding: '0px',
  },
  infoPopover: {
    maxWidth: '320px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  infoHeading: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
  },
  infoText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
  },
});

// DOM cap constant per spec.md
const DOM_CAP = 200;

/**
 * ResultsList component for displaying search results.
 *
 * @param props.results - Array of search results
 * @param props.isLoading - Initial loading state
 * @param props.isLoadingMore - Loading more results state
 * @param props.hasMore - Whether more results are available
 * @param props.totalCount - Total number of results
 * @param props.onLoadMore - Callback to load more results
 * @param props.onResultClick - Callback when result is clicked
 * @param props.onOpenFile - Callback to open file
 * @param props.onOpenRecord - Callback to open record
 * @param props.compactMode - Whether in compact mode
 */
export const ResultsList: React.FC<IResultsListProps> = ({
  results,
  isLoading,
  isLoadingMore,
  hasMore,
  totalCount,
  threshold,
  onLoadMore,
  onResultClick,
  onOpenFile,
  onOpenRecord,
  onFindSimilar,
  onPreview,
  onSummary,
  onEmailDocument,
  onCopyLink,
  onToggleWorkspace,
  isInWorkspace,
  onViewAll,
  onReload,
  onAddDocument,
  onOpenViewer,
  onEmailDocuments,
  compactMode,
}) => {
  const styles = useStyles();

  // Info popover open state
  const [infoOpen, setInfoOpen] = useState(false);

  // The BFF now applies the score threshold server-side, so the displayed count
  // matches what the API returned. No client-side threshold filter needed.
  const filteredResults = results;

  // Check if DOM cap reached
  const isDomCapReached = filteredResults.length >= DOM_CAP;
  const displayedCount = Math.min(filteredResults.length, DOM_CAP);

  // Format result count message. Threshold filtering happens server-side now —
  // totalCount is the post-threshold count, so the displayed text never claims
  // "X of Y" with mismatched numbers.
  const getResultCountMessage = () => {
    if (isLoading) return 'Searching...';
    if (totalCount === 0) return 'No results';
    if (isDomCapReached) {
      return `Showing ${displayedCount} of ${totalCount} results`;
    }
    if (filteredResults.length === totalCount) {
      return `${totalCount} result${totalCount === 1 ? '' : 's'}`;
    }
    return `Showing ${filteredResults.length} of ${totalCount} results`;
  };

  // Create stable callbacks for result card
  const handleResultClick = useCallback((result: SearchResult) => () => onResultClick(result), [onResultClick]);

  const handleOpenFile = useCallback(
    (result: SearchResult) => (mode: 'web' | 'desktop') => onOpenFile(result, mode),
    [onOpenFile]
  );

  const handleOpenRecord = useCallback(
    (result: SearchResult) => (inModal: boolean) => onOpenRecord(result, inModal),
    [onOpenRecord]
  );

  const handleFindSimilar = useCallback((result: SearchResult) => () => onFindSimilar(result), [onFindSimilar]);

  const handlePreview = useCallback((result: SearchResult) => () => onPreview(result), [onPreview]);

  const handleSummary = useCallback((result: SearchResult) => () => onSummary(result), [onSummary]);

  const handleEmailDocument = useCallback((result: SearchResult) => () => onEmailDocument(result), [onEmailDocument]);

  const handleCopyLink = useCallback((result: SearchResult) => () => onCopyLink(result), [onCopyLink]);

  const handleToggleWorkspace = useCallback(
    (result: SearchResult) => () => onToggleWorkspace(result),
    [onToggleWorkspace]
  );

  const getIsInWorkspace = useCallback((result: SearchResult) => isInWorkspace(result), [isInWorkspace]);

  return (
    <div className={styles.container}>
      {/* Results count header */}
      <div className={styles.header}>
        <Text className={styles.resultCount}>{getResultCountMessage()}</Text>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: tokens.spacingHorizontalS,
          }}
        >
          <Tooltip content="Reload results" relationship="label">
            <Button
              className={styles.infoButton}
              appearance="subtle"
              size="small"
              icon={<ArrowClockwise20Regular />}
              aria-label="Reload results"
              onClick={onReload}
            />
          </Tooltip>
          {onAddDocument && (
            <Tooltip content="Add Document" relationship="label">
              <Button
                className={styles.infoButton}
                appearance="subtle"
                size="small"
                icon={<Add20Regular />}
                aria-label="Add Document"
                onClick={onAddDocument}
              />
            </Tooltip>
          )}
          {onEmailDocuments && (
            <Tooltip content="Email Documents" relationship="label">
              <Button
                className={styles.infoButton}
                appearance="subtle"
                size="small"
                icon={<MailRegular />}
                aria-label="Email Documents"
                onClick={onEmailDocuments}
              />
            </Tooltip>
          )}
          {onOpenViewer && (
            <Tooltip content="Open full viewer" relationship="label">
              <Button
                className={styles.infoButton}
                appearance="subtle"
                size="small"
                icon={<Open20Regular />}
                aria-label="Open full viewer"
                onClick={onOpenViewer}
              />
            </Tooltip>
          )}
        </div>
      </div>

      {/* Scrollable results area */}
      <div className={styles.scrollContainer}>
        <div className={styles.resultsList}>
          {/* Render result cards (filtered by threshold) */}
          {filteredResults.slice(0, DOM_CAP).map((result: SearchResult) => (
            <ResultCard
              key={result.documentId}
              result={result}
              onClick={handleResultClick(result)}
              onOpenFile={handleOpenFile(result)}
              onOpenRecord={handleOpenRecord(result)}
              onFindSimilar={handleFindSimilar(result)}
              onPreview={handlePreview(result)}
              onSummary={handleSummary(result)}
              onEmailDocument={handleEmailDocument(result)}
              onCopyLink={handleCopyLink(result)}
              onToggleWorkspace={handleToggleWorkspace(result)}
              isInWorkspace={getIsInWorkspace(result)}
              compactMode={compactMode}
            />
          ))}

          {/* DOM cap message */}
          {isDomCapReached && totalCount > DOM_CAP && (
            <div className={styles.domCapMessage}>
              <Text size={200}>
                Showing first {DOM_CAP} of {totalCount} results. <Link onClick={onViewAll}>View all →</Link>
              </Text>
            </div>
          )}

          {/* Loading more indicator */}
          {isLoadingMore && (
            <div className={styles.loadingMore}>
              <Spinner size="small" />
              <Text size={200}>Loading more results...</Text>
            </div>
          )}

          {/* "Show more results" link removed (2026-05-12): top-N is the inline
              experience; users access the full list via the "Open full viewer"
              toolbar icon. The sentinel-based infinite scroll is therefore also
              removed — sentinelRef is unused. */}
        </div>
      </div>
    </div>
  );
};

export default ResultsList;
