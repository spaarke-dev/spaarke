/**
 * Unit tests for useInfiniteScroll hook
 *
 * @see useInfiniteScroll.ts for implementation
 */
import * as React from 'react';
import { render } from '@testing-library/react';
import { useInfiniteScroll } from '../../hooks/useInfiniteScroll';

type ScrollProps = Parameters<typeof useInfiniteScroll>[0];

/**
 * The hook only creates its IntersectionObserver once `sentinelRef.current` is attached to a
 * DOM node (it early-returns on a null ref). `renderHook` never mounts an element, so the
 * observer was never created and every observer assertion failed. Mount the hook inside a
 * component that actually renders the sentinel `<div ref={sentinelRef} />`.
 */
function SentinelHarness(props: ScrollProps): React.ReactElement {
  const { sentinelRef } = useInfiniteScroll(props);
  return React.createElement('div', { ref: sentinelRef, 'data-testid': 'sentinel' });
}

function mountSentinel(props: ScrollProps) {
  return render(React.createElement(SentinelHarness, props));
}

describe('useInfiniteScroll', () => {
  let mockObserve: jest.Mock;
  let mockUnobserve: jest.Mock;
  let mockDisconnect: jest.Mock;
  let observerCallback: IntersectionObserverCallback;

  beforeEach(() => {
    mockObserve = jest.fn();
    mockUnobserve = jest.fn();
    mockDisconnect = jest.fn();

    // Capture the callback when IntersectionObserver is instantiated
    (global.IntersectionObserver as jest.Mock).mockImplementation((callback: IntersectionObserverCallback) => {
      observerCallback = callback;
      return {
        observe: mockObserve,
        unobserve: mockUnobserve,
        disconnect: mockDisconnect,
      };
    });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('should return a sentinelRef', () => {
    const onLoadMore = jest.fn();

    const { getByTestId } = mountSentinel({ onLoadMore, hasMore: true, isLoading: false });

    // The ref is attached to the rendered sentinel and that element is the one observed.
    expect(getByTestId('sentinel')).toBeInTheDocument();
    expect(mockObserve).toHaveBeenCalledWith(getByTestId('sentinel'));
  });

  it('should create IntersectionObserver with correct options', () => {
    const onLoadMore = jest.fn();

    mountSentinel({
      onLoadMore,
      hasMore: true,
      isLoading: false,
      threshold: 0.5,
      rootMargin: '200px',
    });

    expect(global.IntersectionObserver).toHaveBeenCalledWith(
      expect.any(Function),
      expect.objectContaining({
        threshold: 0.5,
        rootMargin: '200px',
      })
    );
  });

  it('should use default threshold and rootMargin', () => {
    const onLoadMore = jest.fn();

    mountSentinel({
      onLoadMore,
      hasMore: true,
      isLoading: false,
    });

    expect(global.IntersectionObserver).toHaveBeenCalledWith(
      expect.any(Function),
      expect.objectContaining({
        threshold: 0.1,
        rootMargin: '100px',
      })
    );
  });

  it('should not call onLoadMore when not intersecting', () => {
    const onLoadMore = jest.fn();

    mountSentinel({
      onLoadMore,
      hasMore: true,
      isLoading: false,
    });

    // Simulate non-intersecting entry
    observerCallback([{ isIntersecting: false } as IntersectionObserverEntry], {} as IntersectionObserver);

    expect(onLoadMore).not.toHaveBeenCalled();
  });

  it('should call onLoadMore when intersecting with hasMore=true and not loading', () => {
    const onLoadMore = jest.fn();

    mountSentinel({
      onLoadMore,
      hasMore: true,
      isLoading: false,
    });

    // Simulate intersecting entry
    observerCallback([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver);

    expect(onLoadMore).toHaveBeenCalledTimes(1);
  });

  it('should not call onLoadMore when hasMore is false', () => {
    const onLoadMore = jest.fn();

    mountSentinel({
      onLoadMore,
      hasMore: false,
      isLoading: false,
    });

    observerCallback([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver);

    expect(onLoadMore).not.toHaveBeenCalled();
  });

  it('should not call onLoadMore when isLoading is true', () => {
    const onLoadMore = jest.fn();

    mountSentinel({
      onLoadMore,
      hasMore: true,
      isLoading: true,
    });

    observerCallback([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver);

    expect(onLoadMore).not.toHaveBeenCalled();
  });

  it('should disconnect observer on unmount', () => {
    const onLoadMore = jest.fn();

    const { unmount } = mountSentinel({
      onLoadMore,
      hasMore: true,
      isLoading: false,
    });

    unmount();

    expect(mockDisconnect).toHaveBeenCalled();
  });

  it('should read the latest hasMore/isLoading without reconnecting the observer', () => {
    const onLoadMore = jest.fn();
    const { rerender } = mountSentinel({ onLoadMore, hasMore: true, isLoading: false });
    expect(global.IntersectionObserver).toHaveBeenCalledTimes(1);

    // Same threshold/rootMargin, but the list is now exhausted.
    rerender(React.createElement(SentinelHarness, { onLoadMore, hasMore: false, isLoading: false }));

    // Observer was NOT recreated (config unchanged) and the captured callback sees the new props.
    expect(global.IntersectionObserver).toHaveBeenCalledTimes(1);
    observerCallback([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver);
    expect(onLoadMore).not.toHaveBeenCalled();
  });
});
