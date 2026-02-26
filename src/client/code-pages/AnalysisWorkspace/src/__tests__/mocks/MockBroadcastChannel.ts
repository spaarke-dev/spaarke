/**
 * MockBroadcastChannel - Simulates cross-tab BroadcastChannel delivery for tests.
 *
 * Maintains a static registry of all active instances so postMessage can
 * deliver to same-name channels (excluding the sender) just like the real API.
 */

export class MockBroadcastChannel {
    static instances: MockBroadcastChannel[] = [];

    name: string;
    onmessage: ((event: MessageEvent) => void) | null = null;
    closed = false;

    constructor(name: string) {
        this.name = name;
        MockBroadcastChannel.instances.push(this);
    }

    postMessage(data: unknown): void {
        if (this.closed) {
            throw new DOMException("BroadcastChannel is closed");
        }
        for (const instance of MockBroadcastChannel.instances) {
            if (instance !== this && instance.name === this.name && !instance.closed) {
                if (instance.onmessage) {
                    instance.onmessage(new MessageEvent("message", { data }));
                }
            }
        }
    }

    close(): void {
        this.closed = true;
        const idx = MockBroadcastChannel.instances.indexOf(this);
        if (idx >= 0) {
            MockBroadcastChannel.instances.splice(idx, 1);
        }
    }

    addEventListener(_type: string, _listener: EventListener): void {
        // no-op for test compatibility
    }

    removeEventListener(_type: string, _listener: EventListener): void {
        // no-op for test compatibility
    }

    static reset(): void {
        for (const instance of MockBroadcastChannel.instances) {
            instance.closed = true;
        }
        MockBroadcastChannel.instances = [];
    }

    /**
     * Install MockBroadcastChannel as globalThis.BroadcastChannel.
     * Returns a teardown function to restore the original.
     */
    static install(): () => void {
        const original = (globalThis as Record<string, unknown>).BroadcastChannel;
        (globalThis as Record<string, unknown>).BroadcastChannel = MockBroadcastChannel;
        return () => {
            MockBroadcastChannel.reset();
            if (original) {
                (globalThis as Record<string, unknown>).BroadcastChannel = original;
            } else {
                delete (globalThis as Record<string, unknown>).BroadcastChannel;
            }
        };
    }
}
