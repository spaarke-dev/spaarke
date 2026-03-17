/**
 * useInlineAiToolbar Hook Unit Tests
 *
 * Covers:
 *  - visible=false when selection is empty or collapsed
 *  - visible=true when selection is non-empty and within the editor container
 *  - 200ms debounce fires after delay (not immediately)
 *  - Event listener registration and cleanup on unmount
 *
 * @see useInlineAiToolbar - hook under test
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 */

import { renderHook, act } from '@testing-library/react';
import { useInlineAiToolbar } from '../useInlineAiToolbar';
import { createRef } from 'react';

// ---------------------------------------------------------------------------
// window.getSelection mock
// ---------------------------------------------------------------------------

function makeSelection(
  text: string,
  isCollapsed: boolean,
  anchorNode: Node | null = null
): Selection {
  const range: Partial<Range> = {
    commonAncestorContainer: anchorNode ?? document.body,
    getBoundingClientRect: () => ({ top: 100, left: 50, bottom: 116, right: 200, width: 150, height: 16, x: 50, y: 100, toJSON: () => ({}) }),
  };

  return {
    rangeCount: text ? 1 : 0,
    isCollapsed,
    toString: () => text,
    getRangeAt: () => range as Range,
  } as unknown as Selection;
}

// ---------------------------------------------------------------------------
// DOM container helper
// ---------------------------------------------------------------------------

function buildEditorContainer(): HTMLDivElement {
  const container = document.createElement('div');
  document.body.appendChild(container);
  return container;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('useInlineAiToolbar', () => {
  let originalGetSelection: () => Selection | null;
  let container: HTMLDivElement;

  beforeEach(() => {
    originalGetSelection = window.getSelection.bind(window);
    container = buildEditorContainer();
    jest.useFakeTimers();
  });

  afterEach(() => {
    window.getSelection = originalGetSelection;
    document.body.removeChild(container);
    jest.useRealTimers();
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  // visible=false when selection is empty
  // -------------------------------------------------------------------------

  it('visible_IsInitiallyFalse', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    const { result } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef })
    );

    expect(result.current.visible).toBe(false);
  });

  it('visible_RemainsHidden_WhenSelectionIsEmpty', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    // Selection with no text (empty string)
    window.getSelection = () => makeSelection('', true);

    const { result } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef })
    );

    act(() => {
      document.dispatchEvent(new Event('selectionchange'));
      jest.advanceTimersByTime(200);
    });

    expect(result.current.visible).toBe(false);
  });

  it('visible_RemainsHidden_WhenSelectionIsCollapsed', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    // isCollapsed=true means cursor only (no text highlighted)
    window.getSelection = () => makeSelection('', true);

    const { result } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef })
    );

    act(() => {
      document.dispatchEvent(new Event('selectionchange'));
      jest.advanceTimersByTime(200);
    });

    expect(result.current.visible).toBe(false);
  });

  it('visible_RemainsHidden_WhenSelectionIsOutsideContainer', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    // Selection anchored to document.body, which is NOT inside `container`
    const outsideNode = document.createElement('p');
    document.body.appendChild(outsideNode);
    window.getSelection = () => makeSelection('selected text', false, outsideNode);

    const { result } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef })
    );

    act(() => {
      document.dispatchEvent(new Event('selectionchange'));
      jest.advanceTimersByTime(200);
    });

    expect(result.current.visible).toBe(false);

    document.body.removeChild(outsideNode);
  });

  // -------------------------------------------------------------------------
  // visible=true when selection is within the editor container
  // -------------------------------------------------------------------------

  it('visible_BecomesTrue_WhenSelectionIsNonEmptyAndWithinContainer', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    // anchorNode is a child of `container`
    const textNode = document.createTextNode('hello world');
    container.appendChild(textNode);
    window.getSelection = () => makeSelection('hello world', false, textNode);

    const { result } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef })
    );

    act(() => {
      document.dispatchEvent(new Event('selectionchange'));
      jest.advanceTimersByTime(200);
    });

    expect(result.current.visible).toBe(true);
    expect(result.current.selectedText).toBe('hello world');
  });

  it('visible_BecomesTrue_AndPositionIsSet_WhenSelectionIsWithinContainer', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    const textNode = document.createTextNode('analysis text');
    container.appendChild(textNode);
    window.getSelection = () => makeSelection('analysis text', false, textNode);

    const { result } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef })
    );

    act(() => {
      document.dispatchEvent(new Event('selectionchange'));
      jest.advanceTimersByTime(200);
    });

    expect(result.current.visible).toBe(true);
    // Position should be set (exact values depend on getBoundingClientRect mock)
    expect(result.current.position).toBeDefined();
    expect(typeof result.current.position.top).toBe('number');
    expect(typeof result.current.position.left).toBe('number');
  });

  // -------------------------------------------------------------------------
  // 200ms debounce
  // -------------------------------------------------------------------------

  it('debounce_DoesNotFireBeforeDelay', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    const textNode = document.createTextNode('debounce test');
    container.appendChild(textNode);
    window.getSelection = () => makeSelection('debounce test', false, textNode);

    const { result } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef })
    );

    // Dispatch but do NOT advance timers to the 200ms threshold
    act(() => {
      document.dispatchEvent(new Event('selectionchange'));
      jest.advanceTimersByTime(199);
    });

    // Should still be hidden — debounce has not fired yet
    expect(result.current.visible).toBe(false);
  });

  it('debounce_FiresAfter200ms', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    const textNode = document.createTextNode('debounce fires');
    container.appendChild(textNode);
    window.getSelection = () => makeSelection('debounce fires', false, textNode);

    const { result } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef })
    );

    act(() => {
      document.dispatchEvent(new Event('selectionchange'));
      jest.advanceTimersByTime(200);
    });

    expect(result.current.visible).toBe(true);
  });

  it('debounce_ResetsTimer_OnRapidEvents', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    const textNode = document.createTextNode('rapid select');
    container.appendChild(textNode);
    window.getSelection = () => makeSelection('rapid select', false, textNode);

    const { result } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef })
    );

    act(() => {
      // Fire multiple events in quick succession
      document.dispatchEvent(new Event('selectionchange'));
      jest.advanceTimersByTime(50);
      document.dispatchEvent(new Event('selectionchange'));
      jest.advanceTimersByTime(50);
      document.dispatchEvent(new Event('selectionchange'));
      // After third event, only 100ms total since last event — not yet at 200ms
      jest.advanceTimersByTime(100);
    });

    // Still hidden — 200ms from the LAST event has not elapsed
    expect(result.current.visible).toBe(false);

    act(() => {
      // Advance past the 200ms mark from the last event
      jest.advanceTimersByTime(100);
    });

    expect(result.current.visible).toBe(true);
  });

  // -------------------------------------------------------------------------
  // Event listener lifecycle
  // -------------------------------------------------------------------------

  it('cleanup_RemovesSelectionChangeListener_OnUnmount', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    const removeEventListenerSpy = jest.spyOn(document, 'removeEventListener');

    const { unmount } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef })
    );

    unmount();

    expect(removeEventListenerSpy).toHaveBeenCalledWith(
      'selectionchange',
      expect.any(Function)
    );

    removeEventListenerSpy.mockRestore();
  });

  // -------------------------------------------------------------------------
  // Default actions
  // -------------------------------------------------------------------------

  it('actions_ReturnsDefaultInlineActions_WhenNoneProvided', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    const { result } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef })
    );

    expect(Array.isArray(result.current.actions)).toBe(true);
    expect(result.current.actions.length).toBeGreaterThan(0);
  });

  it('actions_UsesCustomActions_WhenProvided', () => {
    const editorContainerRef = createRef<HTMLDivElement>();
    (editorContainerRef as React.MutableRefObject<HTMLDivElement | null>).current = container;

    const customActions = [
      {
        id: 'custom',
        label: 'Custom',
        icon: null as unknown as React.ReactElement,
        actionType: 'chat' as const,
      },
    ];

    const { result } = renderHook(() =>
      useInlineAiToolbar({ editorContainerRef, actions: customActions })
    );

    expect(result.current.actions).toBe(customActions);
    expect(result.current.actions).toHaveLength(1);
    expect(result.current.actions[0].id).toBe('custom');
  });
});
