/**
 * useVirtualization Hook Unit Tests
 */

import { renderHook } from '@testing-library/react';
import { useVirtualization } from '../useVirtualization';

describe('useVirtualization', () => {
  it('should not virtualize when record count is below threshold', () => {
    const { result } = renderHook(() => useVirtualization(50));

    expect(result.current.shouldVirtualize).toBe(false);
  });

  it('should not virtualize when record count equals threshold', () => {
    const { result } = renderHook(() => useVirtualization(100));

    expect(result.current.shouldVirtualize).toBe(false);
  });

  it('should virtualize when record count exceeds threshold', () => {
    const { result } = renderHook(() => useVirtualization(101));

    expect(result.current.shouldVirtualize).toBe(true);
  });

  it('should virtualize for large datasets', () => {
    const { result } = renderHook(() => useVirtualization(10000));

    expect(result.current.shouldVirtualize).toBe(true);
  });

  it('should respect custom threshold', () => {
    const { result } = renderHook(() =>
      useVirtualization(150, { threshold: 200 })
    );

    expect(result.current.shouldVirtualize).toBe(false);
  });

  it('should virtualize with custom threshold when exceeded', () => {
    const { result } = renderHook(() =>
      useVirtualization(250, { threshold: 200 })
    );

    expect(result.current.shouldVirtualize).toBe(true);
  });

  it('should use default item height', () => {
    const { result } = renderHook(() => useVirtualization(200));

    expect(result.current.itemHeight).toBe(44); // Default
  });

  it('should use custom item height', () => {
    const { result } = renderHook(() =>
      useVirtualization(200, { itemHeight: 60 })
    );

    expect(result.current.itemHeight).toBe(60);
  });

  it('should use default overscan count', () => {
    const { result } = renderHook(() => useVirtualization(200));

    expect(result.current.overscanCount).toBe(5); // Default
  });

  it('should use custom overscan count', () => {
    const { result } = renderHook(() =>
      useVirtualization(200, { overscanCount: 10 })
    );

    expect(result.current.overscanCount).toBe(10);
  });

  it('should not virtualize when explicitly disabled', () => {
    const { result } = renderHook(() =>
      useVirtualization(10000, { enabled: false })
    );

    expect(result.current.shouldVirtualize).toBe(false);
  });

  it('should virtualize when explicitly enabled above threshold', () => {
    const { result } = renderHook(() =>
      useVirtualization(200, { enabled: true })
    );

    expect(result.current.shouldVirtualize).toBe(true);
  });

  it('should update result when record count changes', () => {
    const { result, rerender } = renderHook(
      ({ count }) => useVirtualization(count),
      { initialProps: { count: 50 } }
    );

    expect(result.current.shouldVirtualize).toBe(false);

    rerender({ count: 150 });

    expect(result.current.shouldVirtualize).toBe(true);
  });

  it('should update result when config changes', () => {
    const { result, rerender } = renderHook(
      ({ config }) => useVirtualization(200, config),
      { initialProps: { config: { threshold: 100 } } }
    );

    expect(result.current.shouldVirtualize).toBe(true);

    rerender({ config: { threshold: 300 } });

    expect(result.current.shouldVirtualize).toBe(false);
  });

  it('should return stable result for same inputs', () => {
    const { result, rerender } = renderHook(() => useVirtualization(200));

    const firstResult = result.current;
    rerender();
    const secondResult = result.current;

    expect(firstResult).toBe(secondResult);
  });
});
