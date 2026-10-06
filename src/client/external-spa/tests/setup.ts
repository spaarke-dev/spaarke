// Test setup for the external SPA's vitest suite (unified-access-control-r2 task 140).
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

// jsdom has no ResizeObserver; Fluent v9's MessageBar measures itself with one. A no-op is enough — nothing here
// asserts layout reflow.
class NoopResizeObserver {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

if (typeof window !== 'undefined' && !('ResizeObserver' in window)) {
  (window as unknown as { ResizeObserver: unknown }).ResizeObserver = NoopResizeObserver;
  (globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver = NoopResizeObserver;
}

// Unmount every rendered tree between tests (the suite does not enable vitest globals, so RTL cannot register this).
afterEach(() => cleanup());
