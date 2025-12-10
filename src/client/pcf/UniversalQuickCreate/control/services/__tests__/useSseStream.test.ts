/**
 * useSseStream Unit Tests
 *
 * Tests for the SSE Stream hook.
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import { useSseStream, SseStreamStatus, UseSseStreamOptions } from '../useSseStream';

// Helper to create a mock ReadableStream
const createMockStream = (chunks: string[]) => {
    let index = 0;
    return new ReadableStream({
        async pull(controller) {
            if (index < chunks.length) {
                const encoder = new TextEncoder();
                controller.enqueue(encoder.encode(chunks[index]));
                index++;
            } else {
                controller.close();
            }
        }
    });
};

// Helper to create mock fetch response
const createMockResponse = (chunks: string[], status = 200) => {
    return {
        ok: status >= 200 && status < 300,
        status,
        statusText: status === 200 ? 'OK' : 'Error',
        body: createMockStream(chunks),
        text: async () => 'Error message'
    } as unknown as Response;
};

describe('useSseStream', () => {
    const defaultOptions: UseSseStreamOptions = {
        url: '/api/ai/document-intelligence/analyze',
        body: { documentId: 'doc-123' }
    };

    beforeEach(() => {
        jest.resetAllMocks();
    });

    describe('initial state', () => {
        it('should start with idle status', () => {
            const { result } = renderHook(() => useSseStream(defaultOptions));

            expect(result.current.status).toBe('idle');
            expect(result.current.data).toBe('');
            expect(result.current.error).toBeNull();
        });

        it('should provide start, abort, and reset functions', () => {
            const { result } = renderHook(() => useSseStream(defaultOptions));

            expect(typeof result.current.start).toBe('function');
            expect(typeof result.current.abort).toBe('function');
            expect(typeof result.current.reset).toBe('function');
        });
    });

    describe('streaming', () => {
        it('should transition to connecting then streaming status', async () => {
            const chunks = [
                'data: {"content": "Hello"}\n\n',
                'data: {"done": true}\n\n'
            ];
            global.fetch = jest.fn().mockResolvedValue(createMockResponse(chunks));

            const { result } = renderHook(() => useSseStream(defaultOptions));

            // Start should trigger connecting
            act(() => {
                result.current.start();
            });

            // Should eventually complete
            await waitFor(() => {
                expect(result.current.status).toBe('complete');
            });
        });

        it('should accumulate data from chunks', async () => {
            const chunks = [
                'data: {"content": "Hello "}\n\n',
                'data: {"content": "World"}\n\n',
                'data: {"done": true}\n\n'
            ];
            global.fetch = jest.fn().mockResolvedValue(createMockResponse(chunks));

            const { result } = renderHook(() => useSseStream(defaultOptions));

            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('complete');
            });

            expect(result.current.data).toBe('Hello World');
        });

        it('should call onChunk callback for each chunk', async () => {
            const chunks = [
                'data: {"content": "A"}\n\n',
                'data: {"content": "B"}\n\n',
                'data: {"done": true}\n\n'
            ];
            global.fetch = jest.fn().mockResolvedValue(createMockResponse(chunks));

            const onChunk = jest.fn();
            const { result } = renderHook(() =>
                useSseStream({ ...defaultOptions, onChunk })
            );

            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('complete');
            });

            expect(onChunk).toHaveBeenCalledWith({ content: 'A' });
            expect(onChunk).toHaveBeenCalledWith({ content: 'B' });
        });

        it('should call onComplete with final data', async () => {
            const chunks = [
                'data: {"content": "Final"}\n\n',
                'data: {"done": true}\n\n'
            ];
            global.fetch = jest.fn().mockResolvedValue(createMockResponse(chunks));

            const onComplete = jest.fn();
            const { result } = renderHook(() =>
                useSseStream({ ...defaultOptions, onComplete })
            );

            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('complete');
            });

            expect(onComplete).toHaveBeenCalledWith('Final');
        });
    });

    describe('SSE parsing', () => {
        it('should handle [DONE] marker', async () => {
            const chunks = [
                'data: {"content": "Test"}\n\n',
                'data: [DONE]\n\n'
            ];
            global.fetch = jest.fn().mockResolvedValue(createMockResponse(chunks));

            const { result } = renderHook(() => useSseStream(defaultOptions));

            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('complete');
            });

            expect(result.current.data).toBe('Test');
        });

        it('should skip empty lines and comments', async () => {
            const chunks = [
                ': this is a comment\n',
                '\n',
                'data: {"content": "Valid"}\n\n',
                'data: {"done": true}\n\n'
            ];
            global.fetch = jest.fn().mockResolvedValue(createMockResponse(chunks));

            const { result } = renderHook(() => useSseStream(defaultOptions));

            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('complete');
            });

            expect(result.current.data).toBe('Valid');
        });

        it('should handle plain text content after data: prefix', async () => {
            const chunks = [
                'data: plain text content\n\n',
                'data: {"done": true}\n\n'
            ];
            global.fetch = jest.fn().mockResolvedValue(createMockResponse(chunks));

            const { result } = renderHook(() => useSseStream(defaultOptions));

            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('complete');
            });

            expect(result.current.data).toBe('plain text content');
        });
    });

    describe('error handling', () => {
        it('should set error status on HTTP error', async () => {
            global.fetch = jest.fn().mockResolvedValue({
                ok: false,
                status: 500,
                statusText: 'Internal Server Error',
                text: async () => 'Server error message'
            });

            const onError = jest.fn();
            const { result } = renderHook(() =>
                useSseStream({ ...defaultOptions, onError })
            );

            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('error');
            });

            expect(result.current.error).toBe('Server error message');
            expect(onError).toHaveBeenCalledWith('Server error message');
        });

        it('should set error status on network error', async () => {
            global.fetch = jest.fn().mockRejectedValue(new Error('Network failed'));

            const { result } = renderHook(() => useSseStream(defaultOptions));

            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('error');
            });

            expect(result.current.error).toBe('Network failed');
        });

        it('should handle error in SSE data', async () => {
            const chunks = [
                'data: {"error": "Processing failed"}\n\n'
            ];
            global.fetch = jest.fn().mockResolvedValue(createMockResponse(chunks));

            const { result } = renderHook(() => useSseStream(defaultOptions));

            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('error');
            });

            expect(result.current.error).toBe('Processing failed');
        });
    });

    describe('abort', () => {
        it('should set aborted status when abort is called', async () => {
            // Create a slow stream that doesn't complete immediately
            let resolveRead: () => void;
            const slowStream = new ReadableStream({
                async pull(controller) {
                    await new Promise<void>((resolve) => {
                        resolveRead = resolve;
                    });
                    controller.close();
                }
            });

            global.fetch = jest.fn().mockResolvedValue({
                ok: true,
                status: 200,
                body: slowStream
            });

            const { result } = renderHook(() => useSseStream(defaultOptions));

            act(() => {
                result.current.start();
            });

            // Wait for connecting/streaming
            await waitFor(() => {
                expect(['connecting', 'streaming']).toContain(result.current.status);
            });

            act(() => {
                result.current.abort();
            });

            expect(result.current.status).toBe('aborted');

            // Cleanup
            resolveRead!();
        });
    });

    describe('reset', () => {
        it('should reset to initial state', async () => {
            const chunks = [
                'data: {"content": "Test"}\n\n',
                'data: {"done": true}\n\n'
            ];
            global.fetch = jest.fn().mockResolvedValue(createMockResponse(chunks));

            const { result } = renderHook(() => useSseStream(defaultOptions));

            // Start and complete
            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('complete');
            });

            expect(result.current.data).toBe('Test');

            // Reset
            act(() => {
                result.current.reset();
            });

            expect(result.current.status).toBe('idle');
            expect(result.current.data).toBe('');
            expect(result.current.error).toBeNull();
        });
    });

    describe('authorization', () => {
        it('should include Authorization header when token provided', async () => {
            const chunks = ['data: {"done": true}\n\n'];
            global.fetch = jest.fn().mockResolvedValue(createMockResponse(chunks));

            const { result } = renderHook(() =>
                useSseStream({ ...defaultOptions, token: 'test-token' })
            );

            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('complete');
            });

            expect(global.fetch).toHaveBeenCalledWith(
                defaultOptions.url,
                expect.objectContaining({
                    headers: expect.objectContaining({
                        Authorization: 'Bearer test-token'
                    })
                })
            );
        });
    });

    describe('request body', () => {
        it('should stringify body in request', async () => {
            const chunks = ['data: {"done": true}\n\n'];
            global.fetch = jest.fn().mockResolvedValue(createMockResponse(chunks));

            const body = { documentId: 'doc-123', driveId: 'drive-456' };
            const { result } = renderHook(() =>
                useSseStream({ url: '/api/stream', body })
            );

            act(() => {
                result.current.start();
            });

            await waitFor(() => {
                expect(result.current.status).toBe('complete');
            });

            expect(global.fetch).toHaveBeenCalledWith(
                '/api/stream',
                expect.objectContaining({
                    method: 'POST',
                    body: JSON.stringify(body)
                })
            );
        });
    });
});
