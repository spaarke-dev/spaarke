/**
 * useExecutionStream - SSE hook for playbook execution events
 *
 * Connects to the playbook execution SSE endpoint and dispatches
 * events to the execution store for visualization.
 *
 * @version 1.0.0
 */

import { useEffect, useRef, useCallback, useState } from 'react';
import { useExecutionStore, ExecutionEvent } from '../stores/executionStore';

interface UseExecutionStreamOptions {
  /** Base URL for the API endpoint */
  apiBaseUrl: string;
  /** Access token for authentication */
  accessToken: string;
  /** Playbook ID to execute */
  playbookId: string;
  /** Record ID (scope) for execution context */
  recordId: string;
  /** Callback when execution completes */
  onComplete?: () => void;
  /** Callback when execution fails */
  onError?: (error: string) => void;
}

interface UseExecutionStreamReturn {
  /** Start playbook execution */
  startExecution: () => void;
  /** Stop/cancel execution */
  stopExecution: () => void;
  /** Whether currently executing */
  isExecuting: boolean;
  /** Connection status */
  connectionStatus: 'disconnected' | 'connecting' | 'connected' | 'error';
  /** Connection error message */
  connectionError: string | null;
}

/**
 * Hook for managing SSE connection to playbook execution endpoint.
 * Handles connection lifecycle, reconnection, and event dispatching.
 */
export function useExecutionStream(
  options: UseExecutionStreamOptions
): UseExecutionStreamReturn {
  const { apiBaseUrl, accessToken, playbookId, recordId, onComplete, onError } = options;

  const eventSourceRef = useRef<EventSource | null>(null);
  const [connectionStatus, setConnectionStatus] = useState<
    'disconnected' | 'connecting' | 'connected' | 'error'
  >('disconnected');
  const [connectionError, setConnectionError] = useState<string | null>(null);

  const {
    startExecution: storeStartExecution,
    handleEvent,
    stopExecution: storeStopExecution,
    resetExecution,
    isExecuting,
  } = useExecutionStore();

  // Clean up EventSource on unmount
  useEffect(() => {
    return () => {
      if (eventSourceRef.current) {
        eventSourceRef.current.close();
        eventSourceRef.current = null;
      }
    };
  }, []);

  const startExecution = useCallback(() => {
    // Close existing connection if any
    if (eventSourceRef.current) {
      eventSourceRef.current.close();
    }

    setConnectionStatus('connecting');
    setConnectionError(null);
    resetExecution();

    // Construct SSE URL with query parameters
    const sseUrl = new URL(`${apiBaseUrl}/api/playbooks/${playbookId}/execute/stream`);
    sseUrl.searchParams.set('recordId', recordId);

    console.info('[useExecutionStream] Connecting to SSE:', sseUrl.toString());

    // Note: EventSource doesn't support custom headers natively.
    // For authenticated SSE, we pass token as query param (backend validates it)
    // In production, consider using fetch + ReadableStream for better auth support
    sseUrl.searchParams.set('token', accessToken);

    const eventSource = new EventSource(sseUrl.toString());
    eventSourceRef.current = eventSource;

    eventSource.onopen = () => {
      console.info('[useExecutionStream] SSE connection opened');
      setConnectionStatus('connected');
    };

    eventSource.onmessage = (event) => {
      try {
        const data: ExecutionEvent = JSON.parse(event.data);
        console.debug('[useExecutionStream] Received event:', data.eventType);

        // Initialize execution on first event if needed
        if (data.eventType === 'execution_started' && data.executionId) {
          storeStartExecution(data.executionId);
        }

        handleEvent(data);

        // Handle completion
        if (data.eventType === 'execution_completed') {
          setConnectionStatus('disconnected');
          eventSource.close();
          eventSourceRef.current = null;
          onComplete?.();
        }

        // Handle failure
        if (data.eventType === 'execution_failed') {
          setConnectionStatus('error');
          eventSource.close();
          eventSourceRef.current = null;
          onError?.(data.error ?? 'Execution failed');
        }
      } catch (error) {
        console.error('[useExecutionStream] Failed to parse event:', error);
      }
    };

    eventSource.onerror = (error) => {
      console.error('[useExecutionStream] SSE error:', error);
      setConnectionStatus('error');
      setConnectionError('Connection lost. Please try again.');
      eventSource.close();
      eventSourceRef.current = null;
      onError?.('Connection error');
    };
  }, [
    apiBaseUrl,
    accessToken,
    playbookId,
    recordId,
    handleEvent,
    storeStartExecution,
    resetExecution,
    onComplete,
    onError,
  ]);

  const stopExecution = useCallback(() => {
    if (eventSourceRef.current) {
      eventSourceRef.current.close();
      eventSourceRef.current = null;
    }
    storeStopExecution();
    setConnectionStatus('disconnected');
    console.info('[useExecutionStream] Execution stopped by user');
  }, [storeStopExecution]);

  return {
    startExecution,
    stopExecution,
    isExecuting: isExecuting(),
    connectionStatus,
    connectionError,
  };
}
