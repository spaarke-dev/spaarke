/**
 * Keyboard Shortcuts Hook - Global Keyboard Event Handler
 *
 * Provides keyboard shortcut handling for AI Assistant interactions:
 * - Cmd/Ctrl+K: Toggle AI Assistant modal
 * - Escape: Close modal or cancel current operation
 * - Enter: Send message (in chat input)
 * - Shift+Enter: New line (in chat input)
 *
 * @version 1.0.0
 * Task 053: Implement keyboard shortcuts
 */

import { useEffect, useCallback, useRef } from 'react';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface KeyboardShortcutConfig {
  /** Key code (e.g., 'k', 'Escape', 'Enter') */
  key: string;
  /** Require Ctrl (Windows) or Cmd (Mac) */
  ctrlOrCmd?: boolean;
  /** Require Shift */
  shift?: boolean;
  /** Require Alt */
  alt?: boolean;
  /** Callback when shortcut is triggered */
  handler: (event: KeyboardEvent) => void;
  /** Optional description for help display */
  description?: string;
  /** Whether to prevent default browser behavior */
  preventDefault?: boolean;
  /** Only trigger when this element is focused (or its descendants) */
  scope?: HTMLElement | null;
  /** Disable this shortcut */
  disabled?: boolean;
}

export interface UseKeyboardShortcutsOptions {
  /** Array of shortcut configurations */
  shortcuts: KeyboardShortcutConfig[];
  /** Disable all shortcuts */
  disabled?: boolean;
}

export interface UseKeyboardShortcutsReturn {
  /** Manually register a shortcut */
  registerShortcut: (config: KeyboardShortcutConfig) => () => void;
  /** Manually unregister a shortcut */
  unregisterShortcut: (key: string, ctrlOrCmd?: boolean) => void;
  /** Get formatted shortcut string (e.g., "⌘K" or "Ctrl+K") */
  formatShortcut: (config: Pick<KeyboardShortcutConfig, 'key' | 'ctrlOrCmd' | 'shift' | 'alt'>) => string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Utility Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Detect if running on Mac OS
 */
const isMac = (): boolean => {
  return typeof navigator !== 'undefined' && /Mac|iPod|iPhone|iPad/.test(navigator.platform);
};

/**
 * Check if the modifier key matches (Cmd on Mac, Ctrl on others)
 */
const matchesCtrlOrCmd = (event: KeyboardEvent): boolean => {
  return isMac() ? event.metaKey : event.ctrlKey;
};

/**
 * Format a shortcut for display
 */
const formatShortcutDisplay = (
  config: Pick<KeyboardShortcutConfig, 'key' | 'ctrlOrCmd' | 'shift' | 'alt'>
): string => {
  const parts: string[] = [];
  const mac = isMac();

  if (config.ctrlOrCmd) {
    parts.push(mac ? '⌘' : 'Ctrl');
  }
  if (config.alt) {
    parts.push(mac ? '⌥' : 'Alt');
  }
  if (config.shift) {
    parts.push(mac ? '⇧' : 'Shift');
  }

  // Format the key
  let keyDisplay = config.key.toUpperCase();
  if (config.key === 'Escape') keyDisplay = mac ? 'Esc' : 'Esc';
  if (config.key === 'Enter') keyDisplay = mac ? '↵' : 'Enter';
  if (config.key === ' ') keyDisplay = 'Space';

  parts.push(keyDisplay);

  return mac ? parts.join('') : parts.join('+');
};

/**
 * Check if an element is an input/textarea that should receive keyboard input
 */
const isInputElement = (element: Element | null): boolean => {
  if (!element) return false;
  const tagName = element.tagName.toLowerCase();
  return tagName === 'input' || tagName === 'textarea' || (element as HTMLElement).isContentEditable;
};

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

export const useKeyboardShortcuts = (
  options: UseKeyboardShortcutsOptions
): UseKeyboardShortcutsReturn => {
  const { shortcuts, disabled = false } = options;
  const shortcutsRef = useRef<Map<string, KeyboardShortcutConfig>>(new Map());

  // Generate a unique key for a shortcut
  const getShortcutKey = useCallback(
    (config: Pick<KeyboardShortcutConfig, 'key' | 'ctrlOrCmd' | 'shift' | 'alt'>): string => {
      const parts = [config.key.toLowerCase()];
      if (config.ctrlOrCmd) parts.unshift('ctrlOrCmd');
      if (config.shift) parts.unshift('shift');
      if (config.alt) parts.unshift('alt');
      return parts.join('+');
    },
    []
  );

  // Register initial shortcuts
  useEffect(() => {
    shortcutsRef.current.clear();
    shortcuts.forEach((config) => {
      const key = getShortcutKey(config);
      shortcutsRef.current.set(key, config);
    });
  }, [shortcuts, getShortcutKey]);

  // Handle keydown events
  useEffect(() => {
    if (disabled) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      // Check each registered shortcut
      for (const config of shortcutsRef.current.values()) {
        if (config.disabled) continue;

        // Match key
        if (event.key.toLowerCase() !== config.key.toLowerCase()) continue;

        // Match modifiers
        if (config.ctrlOrCmd && !matchesCtrlOrCmd(event)) continue;
        if (!config.ctrlOrCmd && matchesCtrlOrCmd(event)) continue;
        if (config.shift && !event.shiftKey) continue;
        if (!config.shift && event.shiftKey) continue;
        if (config.alt && !event.altKey) continue;
        if (!config.alt && event.altKey) continue;

        // Check scope
        if (config.scope) {
          const target = event.target as HTMLElement;
          if (!config.scope.contains(target)) continue;
        }

        // Skip if focused on input and no ctrlOrCmd (let normal typing work)
        if (!config.ctrlOrCmd && isInputElement(event.target as Element)) {
          // Allow Escape to work in inputs
          if (config.key.toLowerCase() !== 'escape') continue;
        }

        // Trigger the handler
        if (config.preventDefault !== false) {
          event.preventDefault();
        }
        config.handler(event);
        break;
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [disabled]);

  // Manual registration
  const registerShortcut = useCallback(
    (config: KeyboardShortcutConfig): (() => void) => {
      const key = getShortcutKey(config);
      shortcutsRef.current.set(key, config);
      return () => shortcutsRef.current.delete(key);
    },
    [getShortcutKey]
  );

  // Manual unregistration
  const unregisterShortcut = useCallback(
    (key: string, ctrlOrCmd?: boolean): void => {
      const shortcutKey = getShortcutKey({ key, ctrlOrCmd });
      shortcutsRef.current.delete(shortcutKey);
    },
    [getShortcutKey]
  );

  // Format shortcut for display
  const formatShortcut = useCallback(
    (config: Pick<KeyboardShortcutConfig, 'key' | 'ctrlOrCmd' | 'shift' | 'alt'>): string => {
      return formatShortcutDisplay(config);
    },
    []
  );

  return {
    registerShortcut,
    unregisterShortcut,
    formatShortcut,
  };
};

// ─────────────────────────────────────────────────────────────────────────────
// Pre-built Shortcuts
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Create standard AI Assistant shortcuts
 */
export const createAiAssistantShortcuts = (handlers: {
  onToggleModal: () => void;
  onCloseModal: () => void;
  onSendMessage?: () => void;
}): KeyboardShortcutConfig[] => {
  return [
    {
      key: 'k',
      ctrlOrCmd: true,
      handler: handlers.onToggleModal,
      description: 'Toggle AI Assistant',
      preventDefault: true,
    },
    {
      key: 'Escape',
      handler: handlers.onCloseModal,
      description: 'Close AI Assistant',
      preventDefault: false, // Let inputs handle Escape too
    },
  ];
};

export default useKeyboardShortcuts;
