/**
 * Unit tests for useInfiniteScroll hook
 *
 * @see useInfiniteScroll.ts for implementation
 */
import { renderHook } from "@testing-library/react-hooks";
import { useInfiniteScroll } from "../../hooks/useInfiniteScroll";

describe("useInfiniteScroll", () => {
    let mockObserve: jest.Mock;
    let mockUnobserve: jest.Mock;
    let mockDisconnect: jest.Mock;
    let observerCallback: IntersectionObserverCallback;

    beforeEach(() => {
        mockObserve = jest.fn();
        mockUnobserve = jest.fn();
        mockDisconnect = jest.fn();

        // Capture the callback when IntersectionObserver is instantiated
        (global.IntersectionObserver as jest.Mock).mockImplementation(
            (callback: IntersectionObserverCallback) => {
                observerCallback = callback;
                return {
                    observe: mockObserve,
                    unobserve: mockUnobserve,
                    disconnect: mockDisconnect,
                };
            }
        );
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    it("should return a sentinelRef", () => {
        const onLoadMore = jest.fn();

        const { result } = renderHook(() =>
            useInfiniteScroll({
                onLoadMore,
                hasMore: true,
                isLoading: false,
            })
        );

        expect(result.current.sentinelRef).toBeDefined();
    });

    it("should create IntersectionObserver with correct options", () => {
        const onLoadMore = jest.fn();

        renderHook(() =>
            useInfiniteScroll({
                onLoadMore,
                hasMore: true,
                isLoading: false,
                threshold: 0.5,
                rootMargin: "200px",
            })
        );

        expect(global.IntersectionObserver).toHaveBeenCalledWith(
            expect.any(Function),
            expect.objectContaining({
                threshold: 0.5,
                rootMargin: "200px",
            })
        );
    });

    it("should use default threshold and rootMargin", () => {
        const onLoadMore = jest.fn();

        renderHook(() =>
            useInfiniteScroll({
                onLoadMore,
                hasMore: true,
                isLoading: false,
            })
        );

        expect(global.IntersectionObserver).toHaveBeenCalledWith(
            expect.any(Function),
            expect.objectContaining({
                threshold: 0.1,
                rootMargin: "100px",
            })
        );
    });

    it("should not call onLoadMore when not intersecting", () => {
        const onLoadMore = jest.fn();

        renderHook(() =>
            useInfiniteScroll({
                onLoadMore,
                hasMore: true,
                isLoading: false,
            })
        );

        // Simulate non-intersecting entry
        observerCallback(
            [{ isIntersecting: false } as IntersectionObserverEntry],
            {} as IntersectionObserver
        );

        expect(onLoadMore).not.toHaveBeenCalled();
    });

    it("should call onLoadMore when intersecting with hasMore=true and not loading", () => {
        const onLoadMore = jest.fn();

        renderHook(() =>
            useInfiniteScroll({
                onLoadMore,
                hasMore: true,
                isLoading: false,
            })
        );

        // Simulate intersecting entry
        observerCallback(
            [{ isIntersecting: true } as IntersectionObserverEntry],
            {} as IntersectionObserver
        );

        expect(onLoadMore).toHaveBeenCalledTimes(1);
    });

    it("should not call onLoadMore when hasMore is false", () => {
        const onLoadMore = jest.fn();

        renderHook(() =>
            useInfiniteScroll({
                onLoadMore,
                hasMore: false,
                isLoading: false,
            })
        );

        observerCallback(
            [{ isIntersecting: true } as IntersectionObserverEntry],
            {} as IntersectionObserver
        );

        expect(onLoadMore).not.toHaveBeenCalled();
    });

    it("should not call onLoadMore when isLoading is true", () => {
        const onLoadMore = jest.fn();

        renderHook(() =>
            useInfiniteScroll({
                onLoadMore,
                hasMore: true,
                isLoading: true,
            })
        );

        observerCallback(
            [{ isIntersecting: true } as IntersectionObserverEntry],
            {} as IntersectionObserver
        );

        expect(onLoadMore).not.toHaveBeenCalled();
    });

    it("should disconnect observer on unmount", () => {
        const onLoadMore = jest.fn();

        const { unmount } = renderHook(() =>
            useInfiniteScroll({
                onLoadMore,
                hasMore: true,
                isLoading: false,
            })
        );

        unmount();

        expect(mockDisconnect).toHaveBeenCalled();
    });
});
