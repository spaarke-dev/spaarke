/**
 * defaultWorkspaceRenderer — slot semantics tests (R4 task 052 / C-4)
 *
 * Covers:
 *   1. Initial state — slot is empty (`getDefaultWorkspaceRenderer()` returns null).
 *   2. setDefaultWorkspaceRenderer() stores the renderer.
 *   3. Repeated registration overwrites (last writer wins).
 *   4. clearDefaultWorkspaceRenderer() resets to null.
 *   5. Type contract — registered renderer is callable with `WorkspaceRendererProps`.
 *
 * The slot is module-level state; each test calls
 * `clearDefaultWorkspaceRenderer()` in afterEach to isolate. Tests are
 * intentionally minimal — the slot has no async behaviour, no React tree, no
 * side effects to mock.
 */

import * as React from 'react';
import {
  setDefaultWorkspaceRenderer,
  getDefaultWorkspaceRenderer,
  clearDefaultWorkspaceRenderer,
  type WorkspaceRenderer,
  type WorkspaceRendererProps,
} from '../index';

describe('defaultWorkspaceRenderer', () => {
  afterEach(() => {
    clearDefaultWorkspaceRenderer();
  });

  it('returns null before any renderer is registered', () => {
    // Initial state in this test (afterEach clears between tests).
    expect(getDefaultWorkspaceRenderer()).toBeNull();
  });

  it('stores a registered renderer and returns it via getDefaultWorkspaceRenderer()', () => {
    const StubRenderer: WorkspaceRenderer = () => React.createElement('div', { 'data-testid': 'stub-renderer' });

    setDefaultWorkspaceRenderer(StubRenderer);

    expect(getDefaultWorkspaceRenderer()).toBe(StubRenderer);
  });

  it('overwrites the previously-registered renderer on repeat call (last writer wins)', () => {
    const FirstRenderer: WorkspaceRenderer = () => React.createElement('div', { 'data-testid': 'first' });
    const SecondRenderer: WorkspaceRenderer = () => React.createElement('div', { 'data-testid': 'second' });

    setDefaultWorkspaceRenderer(FirstRenderer);
    expect(getDefaultWorkspaceRenderer()).toBe(FirstRenderer);

    setDefaultWorkspaceRenderer(SecondRenderer);
    expect(getDefaultWorkspaceRenderer()).toBe(SecondRenderer);
  });

  it('returns null after clearDefaultWorkspaceRenderer()', () => {
    const StubRenderer: WorkspaceRenderer = () => React.createElement('div', null);
    setDefaultWorkspaceRenderer(StubRenderer);
    expect(getDefaultWorkspaceRenderer()).toBe(StubRenderer);

    clearDefaultWorkspaceRenderer();

    expect(getDefaultWorkspaceRenderer()).toBeNull();
  });

  it('accepts a renderer that consumes the full WorkspaceRendererProps shape (type contract)', () => {
    // This test exists primarily as a compile-time contract check: if the
    // interface or component-type alias drift, this stub will fail to satisfy
    // `WorkspaceRenderer` and the test will not type-check.
    const FullPropsRenderer: WorkspaceRenderer = (props: WorkspaceRendererProps) => {
      return React.createElement(
        'div',
        { 'data-testid': 'full-props' },
        `version=${props.version} | layoutId=${props.initialWorkspaceId ?? ''} | embedded=${String(
          props.embedded ?? false
        )} | user=${props.userId}`
      );
    };

    setDefaultWorkspaceRenderer(FullPropsRenderer);
    expect(getDefaultWorkspaceRenderer()).toBe(FullPropsRenderer);
  });
});
