/**
 * ResultsList component
 *
 * Scrollable container for search result cards with infinite scroll support.
 * Displays result count header and sentinel element for load-more trigger.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 * @see spec.md for DOM cap and infinite scroll rules
 */

import * as React from "react";
import { useCallback, useMemo, useState } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Spinner,
    Link,
    Button,
    Popover,
    PopoverTrigger,
    PopoverSurface,
    Tooltip,
} from "@fluentui/react-components";
import { Info20Regular } from "@fluentui/react-icons";
import { IResultsListProps, SearchResult } from "../types";
import { ResultCard } from "./ResultCard";
import { useInfiniteScroll } from "../hooks";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        overflow: "hidden",
    },
    header: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
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
        overflowY: "auto",
        padding: tokens.spacingHorizontalM,
    },
    resultsList: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
    },
    sentinel: {
        height: "1px",
        width: "100%",
    },
    loadingMore: {
        display: "flex",
        justifyContent: "center",
        alignItems: "center",
        padding: tokens.spacingVerticalM,
        gap: tokens.spacingHorizontalS,
    },
    showMoreLink: {
        display: "flex",
        justifyContent: "center",
        padding: tokens.spacingVerticalM,
    },
    domCapMessage: {
        padding: tokens.spacingVerticalM,
        textAlign: "center" as const,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRadius: tokens.borderRadiusMedium,
    },
    infoButton: {
        minWidth: "auto",
        padding: "0px",
    },
    infoPopover: {
        maxWidth: "320px",
        display: "flex",
        flexDirection: "column",
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
    onViewAll,
    compactMode,
}) => {
    const styles = useStyles();

    // After the user clicks "Show more" once, switch to infinite scroll
    const [infiniteScrollEnabled, setInfiniteScrollEnabled] = useState(false);

    // Info popover open state
    const [infoOpen, setInfoOpen] = useState(false);

    // Client-side threshold filtering — hide results below the minimum score
    const thresholdDecimal = threshold / 100;
    const filteredResults = useMemo(() => {
        if (threshold <= 0) return results;
        return results.filter((r) => r.combinedScore >= thresholdDecimal);
    }, [results, threshold, thresholdDecimal]);

    // Check if DOM cap reached
    const isDomCapReached = filteredResults.length >= DOM_CAP;
    const displayedCount = Math.min(filteredResults.length, DOM_CAP);

    // Infinite scroll hook - only active after user clicks "Show more"
    const { sentinelRef } = useInfiniteScroll({
        onLoadMore,
        hasMore: infiniteScrollEnabled && hasMore && !isDomCapReached,
        isLoading: isLoading || isLoadingMore,
        threshold: 0.1,
        rootMargin: "100px",
    });

    // Handle "Show more" click — load next page and enable infinite scroll
    const handleShowMore = useCallback(() => {
        setInfiniteScrollEnabled(true);
        onLoadMore();
    }, [onLoadMore]);

    // How many were hidden by threshold
    const hiddenByThreshold = results.length - filteredResults.length;

    // Format result count message
    const getResultCountMessage = () => {
        if (isLoading) return "Searching...";
        if (totalCount === 0) return "No results";
        if (hiddenByThreshold > 0) {
            return `${filteredResults.length} of ${totalCount} results (${hiddenByThreshold} below ${threshold}% threshold)`;
        }
        if (isDomCapReached) {
            return `Showing ${displayedCount} of ${totalCount} results`;
        }
        if (filteredResults.length === totalCount) {
            return `${totalCount} result${totalCount === 1 ? "" : "s"}`;
        }
        return `Showing ${filteredResults.length} of ${totalCount} results`;
    };

    // Create stable callbacks for result card
    const handleResultClick = useCallback(
        (result: SearchResult) => () => onResultClick(result),
        [onResultClick]
    );

    const handleOpenFile = useCallback(
        (result: SearchResult) => (mode: "web" | "desktop") => onOpenFile(result, mode),
        [onOpenFile]
    );

    const handleOpenRecord = useCallback(
        (result: SearchResult) => (inModal: boolean) => onOpenRecord(result, inModal),
        [onOpenRecord]
    );

    const handleFindSimilar = useCallback(
        (result: SearchResult) => () => onFindSimilar(result),
        [onFindSimilar]
    );

    const handlePreview = useCallback(
        (result: SearchResult) => () => onPreview(result),
        [onPreview]
    );

    const handleSummary = useCallback(
        (result: SearchResult) => () => onSummary(result),
        [onSummary]
    );

    return (
        <div className={styles.container}>
            {/* Results count header */}
            <div className={styles.header}>
                <Text className={styles.resultCount}>
                    {getResultCountMessage()}
                </Text>
                <Popover
                    open={infoOpen}
                    onOpenChange={(_ev, data) => setInfoOpen(data.open)}
                    positioning="below-end"
                    withArrow
                >
                    <PopoverTrigger disableButtonEnhancement>
                        <Tooltip content="How semantic search works" relationship="label">
                            <Button
                                className={styles.infoButton}
                                appearance="subtle"
                                size="small"
                                icon={<Info20Regular />}
                                aria-label="Search info"
                            />
                        </Tooltip>
                    </PopoverTrigger>
                    <PopoverSurface className={styles.infoPopover}>
                        <Text className={styles.infoHeading}>How Semantic Search Works</Text>
                        <Text className={styles.infoText}>
                            Semantic search finds documents by <strong>meaning</strong>, not just keywords.
                            Your query is converted to a mathematical representation of its concept,
                            then matched against document content.
                        </Text>
                        <Text className={styles.infoHeading}>Highlighted Text</Text>
                        <Text className={styles.infoText}>
                            The yellow highlighted passages show the most <strong>semantically relevant</strong> section
                            of each document. These may not contain your exact search words — they represent
                            passages the AI identified as most related to your query{"'"}s meaning.
                        </Text>
                        <Text className={styles.infoHeading}>Similarity Score</Text>
                        <Text className={styles.infoText}>
                            The percentage badge (e.g., 45%) indicates how closely a document{"'"}s content
                            matches your query{"'"}s meaning. Higher = more relevant.
                            Use the <strong>Threshold</strong> slider to hide low-scoring results.
                        </Text>
                        <Text className={styles.infoHeading}>Search Modes</Text>
                        <Text className={styles.infoText}>
                            <strong>Hybrid</strong> (default): Combines meaning-based and keyword search for best overall results.{" "}
                            <strong>Concept Only</strong>: Pure meaning-based search — good for abstract queries.{" "}
                            <strong>Keyword Only</strong>: Traditional exact-word matching — good for specific terms or clause numbers.
                        </Text>
                    </PopoverSurface>
                </Popover>
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
                            compactMode={compactMode}
                        />
                    ))}

                    {/* DOM cap message */}
                    {isDomCapReached && totalCount > DOM_CAP && (
                        <div className={styles.domCapMessage}>
                            <Text size={200}>
                                Showing first {DOM_CAP} of {totalCount} results.{" "}
                                <Link onClick={onViewAll}>
                                    View all →
                                </Link>
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

                    {/* Show more link (before infinite scroll is enabled) */}
                    {hasMore && !isDomCapReached && !isLoadingMore && !infiniteScrollEnabled && (
                        <div className={styles.showMoreLink}>
                            <Link onClick={handleShowMore}>
                                Show more results ({filteredResults.length} of {totalCount})
                            </Link>
                        </div>
                    )}

                    {/* Sentinel element for intersection observer (after Show more clicked) */}
                    {hasMore && !isDomCapReached && !isLoadingMore && infiniteScrollEnabled && (
                        <div ref={sentinelRef} className={styles.sentinel} />
                    )}
                </div>
            </div>
        </div>
    );
};

export default ResultsList;
