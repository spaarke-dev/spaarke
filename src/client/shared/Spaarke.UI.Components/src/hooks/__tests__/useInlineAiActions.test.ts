/**
 * useInlineAiActions Hook Unit Tests
 *
 * Covers:
 *  - handleAction posts correct BroadcastChannel message shape
 *  - Channel name is passed correctly (used to create the BroadcastChannel)
 *  - Lazy channel creation: channel is not created until first dispatch
 *  - Channel is closed on unmount
 *  - BroadcastChannel unavailability is handled gracefully (no throw)
 *
 * @see useInlineAiActions - hook under test
 * @see InlineActionBroadcastEvent - wire format verified here
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 */

import { renderHook, act } from '@testing-library/react';
import { useInlineAiActions } from '../useInlineAiActions';
import type { InlineAiAction, InlineActionBroadcastEvent } from '../../components/InlineAiToolbar/inlineAiToolbar.types';

// ---------------------------------------------------------------------------
// BroadcastChannel mock
// ---------------------------------------------------------------------------

class MockBroadcastChannel {
  static instances: MockBroadcastChannel[] = [];
  name: string;
  closed = false;
  messages: unknown[] = [];

  constructor(name: string) {
    this.name = name;
    MockBroadcastChannel.instances.push(this);
  }

  postMessage(data: unknown): void {
    this.messages.push(data);
  }

  close(): void {
    this.closed = true;
  }

  static reset(): void {
    MockBroadcastChannel.instances = [];
  }

  static latestInstance(): MockBroadcastChannel | undefined {
    return MockBroadcastChannel.instances[MockBroadcastChannel.instances.length - 1];
  }
}

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

const summarizeAction: InlineAiAction = {
  id: 'summarize',
  label: 'Summarize',
  icon: null as unknown as React.ReactElement,
  actionType: 'chat',
  description: 'Summarize the selected text',
};

const simplifyAction: InlineAiAction = {
  id: 'simplify',
  label: 'Simplify',
  icon: null as unknown as React.ReactElement,
  actionType: 'diff',
  description: 'Simplify the selected text and show changes',
};

// ---------------------------------------------------------------------------
// Test setup
// ---------------------------------------------------------------------------

describe('useInlineAiActions', () => {
  const originalBroadcastChannel = (globalThis as Record<string, unknown>).BroadcastChannel;

  beforeEach(() => {
    MockBroadcastChannel.reset();
    (globalThis as Record<string, unknown>).BroadcastChannel = MockBroadcastChannel;
  });

  afterEach(() => {
    MockBroadcastChannel.reset();
    if (originalBroadcastChannel) {
      (globalThis as Record<string, unknown>).BroadcastChannel = originalBroadcastChannel;
    } else {
      delete (globalThis as Record<string, unknown>).BroadcastChannel;
    }
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  // handleAction - message shape
  // -------------------------------------------------------------------------

  describe('handleAction - message shape', () => {
    it('posts_CorrectMessageShape_ForChatAction', () => {
      const { result } = renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      act(() => {
        result.current.handleAction(summarizeAction, 'selected text here');
      });

      const channel = MockBroadcastChannel.latestInstance();
      expect(channel).toBeDefined();
      expect(channel!.messages).toHaveLength(1);

      const message = channel!.messages[0] as InlineActionBroadcastEvent;
      expect(message.type).toBe('inline_action');
      expect(message.actionId).toBe('summarize');
      expect(message.actionType).toBe('chat');
      expect(message.label).toBe('Summarize');
      expect(message.selectedText).toBe('selected text here');
    });

    it('posts_CorrectMessageShape_ForDiffAction', () => {
      const { result } = renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      act(() => {
        result.current.handleAction(simplifyAction, 'text to simplify');
      });

      const channel = MockBroadcastChannel.latestInstance();
      expect(channel).toBeDefined();

      const message = channel!.messages[0] as InlineActionBroadcastEvent;
      expect(message.type).toBe('inline_action');
      expect(message.actionId).toBe('simplify');
      expect(message.actionType).toBe('diff');
      expect(message.label).toBe('Simplify');
      expect(message.selectedText).toBe('text to simplify');
    });

    it('posts_NoSessionId_ByDefault', () => {
      const { result } = renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      act(() => {
        result.current.handleAction(summarizeAction, 'test');
      });

      const channel = MockBroadcastChannel.latestInstance();
      const message = channel!.messages[0] as InlineActionBroadcastEvent;
      // sessionId is optional — should be undefined when not provided
      expect(message.sessionId).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // Channel name
  // -------------------------------------------------------------------------

  describe('channel name', () => {
    it('creates_Channel_WithCorrectName', () => {
      const { result } = renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      act(() => {
        result.current.handleAction(summarizeAction, 'text');
      });

      const channel = MockBroadcastChannel.latestInstance();
      expect(channel!.name).toBe('sprk-inline-action');
    });

    it('creates_Channel_WithCustomChannelName', () => {
      const customChannel = 'sprk-custom-channel-42';

      const { result } = renderHook(() =>
        useInlineAiActions({ channelName: customChannel })
      );

      act(() => {
        result.current.handleAction(summarizeAction, 'text');
      });

      const channel = MockBroadcastChannel.latestInstance();
      expect(channel!.name).toBe(customChannel);
    });
  });

  // -------------------------------------------------------------------------
  // Lazy channel creation
  // -------------------------------------------------------------------------

  describe('lazy channel creation', () => {
    it('doesNotCreate_Channel_BeforeFirstDispatch', () => {
      renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      // No actions dispatched — channel should not have been created
      expect(MockBroadcastChannel.instances).toHaveLength(0);
    });

    it('creates_Channel_OnFirstDispatch', () => {
      const { result } = renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      act(() => {
        result.current.handleAction(summarizeAction, 'text');
      });

      expect(MockBroadcastChannel.instances).toHaveLength(1);
    });

    it('reuses_Channel_ForMultipleDispatches', () => {
      const { result } = renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      act(() => {
        result.current.handleAction(summarizeAction, 'first text');
        result.current.handleAction(simplifyAction, 'second text');
      });

      // Only ONE channel should have been created (reused)
      expect(MockBroadcastChannel.instances).toHaveLength(1);

      const channel = MockBroadcastChannel.latestInstance();
      expect(channel!.messages).toHaveLength(2);
    });
  });

  // -------------------------------------------------------------------------
  // Channel cleanup on unmount
  // -------------------------------------------------------------------------

  describe('cleanup on unmount', () => {
    it('closes_Channel_OnUnmount_AfterDispatch', () => {
      const { result, unmount } = renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      act(() => {
        result.current.handleAction(summarizeAction, 'text');
      });

      const channel = MockBroadcastChannel.latestInstance();
      expect(channel!.closed).toBe(false);

      unmount();

      expect(channel!.closed).toBe(true);
    });

    it('doesNotThrow_OnUnmount_WhenChannelWasNeverCreated', () => {
      const { unmount } = renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      // No dispatch before unmount — channel was never created
      expect(() => unmount()).not.toThrow();
    });
  });

  // -------------------------------------------------------------------------
  // BroadcastChannel unavailability
  // -------------------------------------------------------------------------

  describe('BroadcastChannel unavailability', () => {
    it('doesNotThrow_WhenBroadcastChannelIsUnavailable', () => {
      // Remove BroadcastChannel from globalThis (simulates unsupported environment)
      delete (globalThis as Record<string, unknown>).BroadcastChannel;

      const warnSpy = jest.spyOn(console, 'warn').mockImplementation();

      const { result } = renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      expect(() => {
        act(() => {
          result.current.handleAction(summarizeAction, 'text');
        });
      }).not.toThrow();

      expect(warnSpy).toHaveBeenCalled();

      warnSpy.mockRestore();
    });

    it('logsWarning_WithChannelName_WhenBroadcastChannelUnavailable', () => {
      delete (globalThis as Record<string, unknown>).BroadcastChannel;

      const warnSpy = jest.spyOn(console, 'warn').mockImplementation();

      const { result } = renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      act(() => {
        result.current.handleAction(summarizeAction, 'text');
      });

      expect(warnSpy).toHaveBeenCalledWith(
        expect.stringContaining('sprk-inline-action'),
        expect.anything()
      );

      warnSpy.mockRestore();
    });
  });

  // -------------------------------------------------------------------------
  // Multiple dispatches - message accumulation
  // -------------------------------------------------------------------------

  describe('multiple dispatches', () => {
    it('posts_AllMessages_ToSameChannel', () => {
      const { result } = renderHook(() =>
        useInlineAiActions({ channelName: 'sprk-inline-action' })
      );

      act(() => {
        result.current.handleAction(summarizeAction, 'first');
        result.current.handleAction(simplifyAction, 'second');
        result.current.handleAction(summarizeAction, 'third');
      });

      const channel = MockBroadcastChannel.latestInstance();
      expect(channel!.messages).toHaveLength(3);

      const [msg1, msg2, msg3] = channel!.messages as InlineActionBroadcastEvent[];
      expect(msg1.selectedText).toBe('first');
      expect(msg2.selectedText).toBe('second');
      expect(msg3.selectedText).toBe('third');
    });
  });
});
