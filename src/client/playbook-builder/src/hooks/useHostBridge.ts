/**
 * useHostBridge Hook
 *
 * Connects the HostBridge service with the canvasStore.
 * Handles initialization, dirty state sync, and save operations.
 *
 * Usage:
 *   const { isReady, save, isDirty } = useHostBridge();
 */

import { useEffect, useCallback, useRef } from 'react';
import { HostBridge } from '../services/hostBridge';
import { useCanvasStore } from '../stores/canvasStore';
import type { InitMessage, SaveSuccessMessage, SaveErrorMessage } from '../types/messages';

interface UseHostBridgeOptions {
  /** Callback when save is successful */
  onSaveSuccess?: () => void;
  /** Callback when save fails */
  onSaveError?: (error: string) => void;
  /** Callback when theme changes from host */
  onThemeChange?: (theme: 'light' | 'dark') => void;
}

interface UseHostBridgeResult {
  /** Whether the bridge is initialized and ready */
  isReady: boolean;
  /** Whether the canvas has unsaved changes */
  isDirty: boolean;
  /** Whether we're running in an iframe (embedded in host) */
  isEmbedded: boolean;
  /** Save current canvas state to host */
  save: () => void;
  /** Playbook ID from host */
  playbookId: string;
  /** Playbook name from host */
  playbookName: string;
}

export function useHostBridge(options: UseHostBridgeOptions = {}): UseHostBridgeResult {
  const { onSaveSuccess, onSaveError, onThemeChange } = options;

  // Store selectors
  const isDirty = useCanvasStore((s) => s.isDirty);
  const loadFromJson = useCanvasStore((s) => s.loadFromJson);
  const toJson = useCanvasStore((s) => s.toJson);
  const markSaved = useCanvasStore((s) => s.markSaved);

  // Bridge state refs (to avoid re-renders)
  const bridgeRef = useRef<HostBridge | null>(null);
  const isReadyRef = useRef(false);
  const playbookIdRef = useRef('');
  const playbookNameRef = useRef('');

  // Track previous dirty state for change detection
  const prevIsDirtyRef = useRef(isDirty);

  // Handle INIT message from host
  const handleInit = useCallback(
    (payload: InitMessage['payload']) => {
      console.info('[useHostBridge] Received INIT from host');
      playbookIdRef.current = payload.playbookId;
      playbookNameRef.current = payload.playbookName;

      if (payload.canvasJson) {
        loadFromJson(payload.canvasJson);
      }

      isReadyRef.current = true;
    },
    [loadFromJson]
  );

  // Handle SAVE_SUCCESS message from host
  const handleSaveSuccess = useCallback(
    (_payload: SaveSuccessMessage['payload']) => {
      console.info('[useHostBridge] Save successful');
      markSaved();
      onSaveSuccess?.();
    },
    [markSaved, onSaveSuccess]
  );

  // Handle SAVE_ERROR message from host
  const handleSaveError = useCallback(
    (payload: SaveErrorMessage['payload']) => {
      console.error('[useHostBridge] Save failed:', payload.error);
      onSaveError?.(payload.error);
    },
    [onSaveError]
  );

  // Handle THEME_CHANGE message from host
  const handleThemeChange = useCallback(
    (payload: { theme: 'light' | 'dark' }) => {
      console.info('[useHostBridge] Theme changed:', payload.theme);
      onThemeChange?.(payload.theme);
    },
    [onThemeChange]
  );

  // Initialize bridge on mount
  useEffect(() => {
    const bridge = HostBridge.getInstance({
      onInit: handleInit,
      onSaveSuccess: handleSaveSuccess,
      onSaveError: handleSaveError,
      onThemeChange: handleThemeChange,
    });

    bridgeRef.current = bridge;
    bridge.initialize();

    return () => {
      // Don't destroy on unmount - singleton persists
    };
  }, [handleInit, handleSaveSuccess, handleSaveError, handleThemeChange]);

  // Sync dirty state changes to host
  useEffect(() => {
    if (prevIsDirtyRef.current !== isDirty) {
      prevIsDirtyRef.current = isDirty;
      bridgeRef.current?.sendDirtyChange(isDirty);
    }
  }, [isDirty]);

  // Save function
  const save = useCallback(() => {
    if (!bridgeRef.current) {
      console.warn('[useHostBridge] Bridge not initialized');
      return;
    }

    const canvasJson = toJson();
    bridgeRef.current.sendSaveRequest(canvasJson);
    console.info('[useHostBridge] Save request sent');
  }, [toJson]);

  // Check if embedded (memo'd to avoid re-computation)
  const isEmbedded = bridgeRef.current?.isEmbedded() ?? false;

  return {
    isReady: isReadyRef.current,
    isDirty,
    isEmbedded,
    save,
    playbookId: playbookIdRef.current,
    playbookName: playbookNameRef.current,
  };
}
