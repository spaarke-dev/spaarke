/**
 * useKeyboardShortcuts Hook Unit Tests
 */

import { renderHook } from '@testing-library/react';
import { useKeyboardShortcuts } from '../useKeyboardShortcuts';
import { ICommand } from '../../types/CommandTypes';
import { createMockCommandContext } from '../../__mocks__/pcfMocks';

describe('useKeyboardShortcuts', () => {
  let mockCommands: ICommand[];
  let mockContext: any;
  let mockHandler: jest.Mock;

  beforeEach(() => {
    mockHandler = jest.fn().mockResolvedValue(undefined);

    mockCommands = [
      {
        key: 'create',
        label: 'New',
        requiresSelection: false,
        keyboardShortcut: 'Ctrl+N',
        handler: mockHandler
      },
      {
        key: 'open',
        label: 'Open',
        requiresSelection: true,
        keyboardShortcut: 'Ctrl+O',
        handler: mockHandler
      },
      {
        key: 'delete',
        label: 'Delete',
        requiresSelection: true,
        keyboardShortcut: 'Delete',
        handler: mockHandler
      },
      {
        key: 'refresh',
        label: 'Refresh',
        requiresSelection: false,
        keyboardShortcut: 'F5',
        handler: mockHandler
      }
    ];

    mockContext = createMockCommandContext({
      selectedRecords: []
    });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('should execute command on Ctrl+N', () => {
    renderHook(() =>
      useKeyboardShortcuts({
        commands: mockCommands,
        context: mockContext,
        enabled: true
      })
    );

    const event = new KeyboardEvent('keydown', {
      key: 'N',
      ctrlKey: true,
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(mockHandler).toHaveBeenCalledWith(mockContext);
  });

  it('should execute command on F5', () => {
    renderHook(() =>
      useKeyboardShortcuts({
        commands: mockCommands,
        context: mockContext,
        enabled: true
      })
    );

    const event = new KeyboardEvent('keydown', {
      key: 'F5',
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(mockHandler).toHaveBeenCalledWith(mockContext);
  });

  it('should execute command on Delete key', () => {
    mockContext.selectedRecords = [{ id: '1', entityName: 'account' }];

    renderHook(() =>
      useKeyboardShortcuts({
        commands: mockCommands,
        context: mockContext,
        enabled: true
      })
    );

    const event = new KeyboardEvent('keydown', {
      key: 'Delete',
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(mockHandler).toHaveBeenCalledWith(mockContext);
  });

  it('should not execute command when disabled', () => {
    renderHook(() =>
      useKeyboardShortcuts({
        commands: mockCommands,
        context: mockContext,
        enabled: false
      })
    );

    const event = new KeyboardEvent('keydown', {
      key: 'N',
      ctrlKey: true,
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(mockHandler).not.toHaveBeenCalled();
  });

  it('should not execute command when selection is required but none selected', () => {
    mockContext.selectedRecords = [];

    renderHook(() =>
      useKeyboardShortcuts({
        commands: mockCommands,
        context: mockContext,
        enabled: true
      })
    );

    const event = new KeyboardEvent('keydown', {
      key: 'O',
      ctrlKey: true,
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(mockHandler).not.toHaveBeenCalled();
  });

  it('should execute command when selection is required and record is selected', () => {
    mockContext.selectedRecords = [{ id: '1', entityName: 'account' }];

    renderHook(() =>
      useKeyboardShortcuts({
        commands: mockCommands,
        context: mockContext,
        enabled: true
      })
    );

    const event = new KeyboardEvent('keydown', {
      key: 'O',
      ctrlKey: true,
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(mockHandler).toHaveBeenCalledWith(mockContext);
  });

  it('should not execute command when multiSelectSupport is false and multiple selected', () => {
    const singleSelectCommand: ICommand = {
      key: 'open',
      label: 'Open',
      requiresSelection: true,
      multiSelectSupport: false,
      keyboardShortcut: 'Ctrl+O',
      handler: mockHandler
    };

    mockContext.selectedRecords = [
      { id: '1', entityName: 'account' },
      { id: '2', entityName: 'account' }
    ];

    renderHook(() =>
      useKeyboardShortcuts({
        commands: [singleSelectCommand],
        context: mockContext,
        enabled: true
      })
    );

    const event = new KeyboardEvent('keydown', {
      key: 'O',
      ctrlKey: true,
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(mockHandler).not.toHaveBeenCalled();
  });

  it('should handle Shift modifier', () => {
    const shiftCommand: ICommand = {
      key: 'save',
      label: 'Save All',
      requiresSelection: false,
      keyboardShortcut: 'Ctrl+Shift+S',
      handler: mockHandler
    };

    renderHook(() =>
      useKeyboardShortcuts({
        commands: [shiftCommand],
        context: mockContext,
        enabled: true
      })
    );

    const event = new KeyboardEvent('keydown', {
      key: 'S',
      ctrlKey: true,
      shiftKey: true,
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(mockHandler).toHaveBeenCalledWith(mockContext);
  });

  it('should handle Alt modifier', () => {
    const altCommand: ICommand = {
      key: 'menu',
      label: 'Open Menu',
      requiresSelection: false,
      keyboardShortcut: 'Alt+M',
      handler: mockHandler
    };

    renderHook(() =>
      useKeyboardShortcuts({
        commands: [altCommand],
        context: mockContext,
        enabled: true
      })
    );

    const event = new KeyboardEvent('keydown', {
      key: 'M',
      altKey: true,
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(mockHandler).toHaveBeenCalledWith(mockContext);
  });

  it('should cleanup event listener on unmount', () => {
    const removeEventListenerSpy = jest.spyOn(window, 'removeEventListener');

    const { unmount } = renderHook(() =>
      useKeyboardShortcuts({
        commands: mockCommands,
        context: mockContext,
        enabled: true
      })
    );

    unmount();

    expect(removeEventListenerSpy).toHaveBeenCalledWith('keydown', expect.any(Function));

    removeEventListenerSpy.mockRestore();
  });

  it('should not execute unknown keyboard shortcut', () => {
    renderHook(() =>
      useKeyboardShortcuts({
        commands: mockCommands,
        context: mockContext,
        enabled: true
      })
    );

    const event = new KeyboardEvent('keydown', {
      key: 'Z',
      ctrlKey: true,
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(mockHandler).not.toHaveBeenCalled();
  });

  it('should handle Space key correctly', () => {
    const spaceCommand: ICommand = {
      key: 'select',
      label: 'Select',
      requiresSelection: false,
      keyboardShortcut: 'Space',
      handler: mockHandler
    };

    renderHook(() =>
      useKeyboardShortcuts({
        commands: [spaceCommand],
        context: mockContext,
        enabled: true
      })
    );

    const event = new KeyboardEvent('keydown', {
      key: ' ',
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(mockHandler).toHaveBeenCalledWith(mockContext);
  });

  it('should re-register listeners when commands change', () => {
    const { rerender } = renderHook(
      ({ commands }) =>
        useKeyboardShortcuts({
          commands,
          context: mockContext,
          enabled: true
        }),
      { initialProps: { commands: mockCommands } }
    );

    const newHandler = jest.fn().mockResolvedValue(undefined);
    const newCommands = [
      {
        key: 'test',
        label: 'Test',
        requiresSelection: false,
        keyboardShortcut: 'Ctrl+T',
        handler: newHandler
      }
    ];

    rerender({ commands: newCommands });

    const event = new KeyboardEvent('keydown', {
      key: 'T',
      ctrlKey: true,
      bubbles: true
    });

    window.dispatchEvent(event);

    expect(newHandler).toHaveBeenCalledWith(mockContext);
    expect(mockHandler).not.toHaveBeenCalled();
  });
});
