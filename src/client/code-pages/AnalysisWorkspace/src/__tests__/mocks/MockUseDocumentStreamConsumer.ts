/**
 * Mock module for @spaarke/ui-components/components/RichTextEditor/hooks/useDocumentStreamConsumer
 *
 * Returns a stable mock result for tests that don't exercise streaming.
 */

export function useDocumentStreamConsumer(_options: unknown) {
    return {
        isStreaming: false,
        operationId: null as string | null,
        tokenCount: 0,
    };
}
