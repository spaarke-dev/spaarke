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
import { useCallback, useEffect, useRef, useState } from 'react';
import { makeStyles, tokens, Text, Spinner, Link, Button, Tooltip } from '@fluentui/react-components';
import { ArrowClockwise20Regular, Add20Regular, Open20Regular, MailRegular } from '@fluentui/react-icons';
import { IResultsListProps, SearchResult } from '../types';
import { ResultCard } from './ResultCard';
// v1.1.49 — IntersectionObserver-based sentinel re-enabled to power
// lazy-load infinite scroll (Item 9). The hook is host-owned via the
// `onLoadMoreSentinel` callback so the same load-more flow can serve
// both ListView and ResultsList.

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
  // v1.1.47 — Card grid (replaces vertical stack to match prototype).
  // `auto-fill` lets the grid pack as many ~200 px columns as the container
  // width permits; min width keeps cards from squashing on narrow surfaces.
  // Compact-mode form-section hosts naturally end up at 1-2 columns, full-
  // page hosts at 5-6 — matches the prototype reference.
  resultsList: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))',
    gap: tokens.spacingHorizontalM,
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

// v1.1.49 — DOM cap raised to 2000 so lazy-load infinite scroll (Item 9)
// can accumulate substantially more results before the historical safety
// cap kicks in. The original 200 cap was a v0 safety guard against
// pathological page-size requests; with sentinel-driven 25-row pagination
// we now expect the user to scroll into the long tail naturally.
const DOM_CAP = 2000;

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
  selectedIds,
  onToggleSelect,
  onOpenPreview,
  hideToolbar,
  onLoadMoreSentinel,
  compactMode,
}) => {
  const styles = useStyles();

  // Info popover open state
  const [infoOpen, setInfoOpen] = useState(false);

  // v1.1.49 — Sentinel-driven lazy load (Item 9). When the host wires
  // `onLoadMoreSentinel`, the bottom <div ref={sentinelRef}> observes
  // viewport intersection and fires the callback to request the next
  // page. Empty effect closure when no callback supplied so older
  // callers (DOM-cap link path) continue to work unchanged.
  const sentinelRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    if (!onLoadMoreSentinel) return;
    const node = sentinelRef.current;
    if (!node) return;
    const observer = new IntersectionObserver(
      entries => {
        const [entry] = entries;
        if (entry.isIntersecting) {
          onLoadMoreSentinel();
        }
      },
      { threshold: 0.1, rootMargin: '200px' }
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [onLoadMoreSentinel]);

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
      {/* Results count header.
          v1.1.49 — when `hideToolbar === true` the host is rendering its own
          consolidated single-row toolbar above us (Item 2) and this inner
          row is suppressed to avoid the duplicate-toolbar regression UAT
          flagged. We still render a slim row showing JUST the count text
          so the user always sees "N of M results". */}
      {hideToolbar ? (
        <div className={styles.header}>
          <Text className={styles.resultCount}>{getResultCountMessage()}</Text>
        </div>
      ) : (
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
      )}

      {/* Scrollable results area */}
      <div className={styles.scrollContainer}>
        <div className={styles.resultsList}>
          {/* Render result cards (filtered by threshold).
              v1.1.49 — selection + host-preview props piped through so the
              card surfaces a checkbox overlay (Item 1) and routes
              preview-open to the host-level FilePreviewDialog (Item 6). */}
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
              isSelected={selectedIds?.has(result.documentId)}
              onToggleSelect={onToggleSelect ? () => onToggleSelect(result.documentId) : undefined}
              onOpenPreview={onOpenPreview ? () => onOpenPreview(result) : undefined}
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

          {/* v1.1.49 — Lazy-load sentinel (Item 9). When the host wires
              `onLoadMoreSentinel`, this 1-px element becomes the
              IntersectionObserver target. As it enters the viewport the
              host fetches the next page of results which the parent
              appends to `results`. The element renders unconditionally
              (so the observer attaches regardless of current state) but
              the host's `onLoadMoreSentinel` is a no-op when nothing
              more can be loaded. */}
          {onLoadMoreSentinel && <div ref={sentinelRef} className={styles.sentinel} aria-hidden="true" />}
        </div>
      </div>
    </div>
  );
};

export default ResultsList;
