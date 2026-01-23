/**
 * useInfiniteScroll hook
 *
 * Uses Intersection Observer API to detect when the user scrolls near the bottom
 * of a results list, triggering load-more functionality.
 *
 * @see spec.md for intersection observer configuration
 */

import { useRef, useEffect, useCallback } from "react";

interface UseInfiniteScrollOptions {
    /** Callback to load more items */
    onLoadMore: () => void;
    /** Whether currently loading more items */
    isLoading: boolean;
    /** Whether there are more items to load */
    hasMore: boolean;
    /** Intersection threshold (default: 0.1) */
    threshold?: number;
    /** Root margin for early trigger (default: "100px") */
    rootMargin?: string;
}

interface UseInfiniteScrollResult {
    /** Ref to attach to the sentinel element at the bottom of the list */
    sentinelRef: React.RefObject<HTMLDivElement>;
}

/**
 * Hook for implementing infinite scroll with Intersection Observer.
 *
 * @example
 * ```tsx
 * const { sentinelRef } = useInfiniteScroll({
 *   onLoadMore: () => loadNextPage(),
 *   isLoading: isLoadingMore,
 *   hasMore: results.length < totalCount
 * });
 *
 * return (
 *   <div>
 *     {results.map(item => <ResultCard key={item.id} {...item} />)}
 *     <div ref={sentinelRef} style={{ height: 1 }} />
 *   </div>
 * );
 * ```
 */
export function useInfiniteScroll({
    onLoadMore,
    isLoading,
    hasMore,
    threshold = 0.1,
    rootMargin = "100px",
}: UseInfiniteScrollOptions): UseInfiniteScrollResult {
    const sentinelRef = useRef<HTMLDivElement>(null);

    // Memoize the callback to prevent unnecessary observer recreations
    const handleIntersect = useCallback(
        (entries: IntersectionObserverEntry[]) => {
            const [entry] = entries;

            // Only trigger if:
            // 1. Sentinel is intersecting (visible)
            // 2. Not currently loading
            // 3. There are more items to load
            if (entry.isIntersecting && !isLoading && hasMore) {
                onLoadMore();
            }
        },
        [isLoading, hasMore, onLoadMore]
    );

    useEffect(() => {
        const sentinel = sentinelRef.current;
        if (!sentinel) {
            return;
        }

        // Create observer with specified options
        const observer = new IntersectionObserver(handleIntersect, {
            threshold,
            rootMargin,
        });

        // Start observing the sentinel element
        observer.observe(sentinel);

        // Cleanup: disconnect observer when component unmounts or dependencies change
        return () => {
            observer.disconnect();
        };
    }, [handleIntersect, threshold, rootMargin]);

    return { sentinelRef };
}

export default useInfiniteScroll;
