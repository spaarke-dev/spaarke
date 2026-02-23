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
import { useCallback } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Spinner,
    Link,
} from "@fluentui/react-components";
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
    domCapMessage: {
        padding: tokens.spacingVerticalM,
        textAlign: "center" as const,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRadius: tokens.borderRadiusMedium,
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
    onLoadMore,
    onResultClick,
    onOpenFile,
    onOpenRecord,
    onFindSimilar,
    onViewAll,
    compactMode,
}) => {
    const styles = useStyles();

    // Check if DOM cap reached
    const isDomCapReached = results.length >= DOM_CAP;
    const displayedCount = Math.min(results.length, DOM_CAP);

    // Infinite scroll hook - connects sentinel to load-more
    const { sentinelRef } = useInfiniteScroll({
        onLoadMore,
        hasMore: hasMore && !isDomCapReached,
        isLoading: isLoading || isLoadingMore,
        threshold: 0.1,
        rootMargin: "100px",
    });

    // Format result count message
    const getResultCountMessage = () => {
        if (isLoading) return "Searching...";
        if (totalCount === 0) return "No results";
        if (isDomCapReached) {
            return `Showing ${displayedCount} of ${totalCount} results`;
        }
        if (results.length === totalCount) {
            return `${totalCount} result${totalCount === 1 ? "" : "s"}`;
        }
        return `Showing ${results.length} of ${totalCount} results`;
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

    return (
        <div className={styles.container}>
            {/* Results count header */}
            <div className={styles.header}>
                <Text className={styles.resultCount}>
                    {getResultCountMessage()}
                </Text>
            </div>

            {/* Scrollable results area */}
            <div className={styles.scrollContainer}>
                <div className={styles.resultsList}>
                    {/* Render result cards */}
                    {results.slice(0, DOM_CAP).map((result: SearchResult) => (
                        <ResultCard
                            key={result.documentId}
                            result={result}
                            onClick={handleResultClick(result)}
                            onOpenFile={handleOpenFile(result)}
                            onOpenRecord={handleOpenRecord(result)}
                            onFindSimilar={handleFindSimilar(result)}
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

                    {/* Sentinel element for intersection observer */}
                    {hasMore && !isDomCapReached && !isLoadingMore && (
                        <div ref={sentinelRef} className={styles.sentinel} />
                    )}
                </div>
            </div>
        </div>
    );
};

export default ResultsList;
