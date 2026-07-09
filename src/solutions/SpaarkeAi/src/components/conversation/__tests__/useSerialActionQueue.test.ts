/**
 * useSerialActionQueue tests (FR-18 / task 032).
 *
 * Covers the POML's testable acceptance criteria:
 *   - Two rapid-fire requests (e.g. Compare then Draft) do NOT overlap — the
 *     second dispatch does not start until the first settles (proves no
 *     interleaved SSE streams, since dispatchConsumer's Promise only settles
 *     after its stream reaches a terminal state).
 *   - Dispatch order matches enqueue (arrival) order — proves ledger writes
 *     (which happen server-side before dispatchConsumer's Promise resolves,
 *     per ADR-040) are never out-of-order.
 *   - An errored/aborted in-flight request still releases the queue so a
 *     subsequent request runs (NEGATIVE: no deadlock).
 *   - Queue state (`pendingCount`, `inFlightId`) reflects the FIFO accurately.
 *
 * `dispatchConsumer` is injected as a plain mock function — this hook is
 * deliberately dispatcher-agnostic (see module JSDoc's contract-naming note),
 * so no SSE/fetch machinery is exercised here (that is `dispatchConsumer.ts`'s
 * own test suite's job).
 */

import { act } from "react";
import { renderHook } from "@testing-library/react";
import type { DispatchConsumer, DispatchConsumerResult } from "@spaarke/ui-components";
import { useSerialActionQueue } from "../useSerialActionQueue";

function createDeferred<T>(): {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (error: unknown) => void;
} {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

describe("useSerialActionQueue", () => {
  it("does not start the second dispatch until the first settles (no interleaved streams)", async () => {
    const calls: string[] = [];
    const deferredCompare = createDeferred<DispatchConsumerResult>();
    const deferredDraft = createDeferred<DispatchConsumerResult>();

    const dispatchConsumer: DispatchConsumer = jest.fn((bindingId: string) => {
      calls.push(bindingId);
      return bindingId === "binding-compare" ? deferredCompare.promise : deferredDraft.promise;
    });

    const { result } = renderHook(() => useSerialActionQueue(dispatchConsumer));

    let p1!: Promise<DispatchConsumerResult>;
    let p2!: Promise<DispatchConsumerResult>;
    act(() => {
      p1 = result.current.enqueue({ id: "1", bindingId: "binding-compare" });
      p2 = result.current.enqueue({ id: "2", bindingId: "binding-draft" });
    });

    // Only the FIRST dispatch has been invoked — the second is queued behind it.
    expect(dispatchConsumer).toHaveBeenCalledTimes(1);
    expect(calls).toEqual(["binding-compare"]);
    expect(result.current.inFlightId).toBe("1");
    expect(result.current.pendingCount).toBe(1);

    await act(async () => {
      deferredCompare.resolve({ streamId: "s1", status: "complete" });
      await p1;
    });

    // The FIRST request's Promise settling releases the SECOND — arrival order.
    expect(dispatchConsumer).toHaveBeenCalledTimes(2);
    expect(calls).toEqual(["binding-compare", "binding-draft"]);
    expect(result.current.inFlightId).toBe("2");
    expect(result.current.pendingCount).toBe(0);

    await act(async () => {
      deferredDraft.resolve({ streamId: "s2", status: "complete" });
      await p2;
    });

    expect(result.current.inFlightId).toBeNull();
    expect(result.current.pendingCount).toBe(0);
    // Order never changes after settlement — no interleaving occurred.
    expect(calls).toEqual(["binding-compare", "binding-draft"]);
  });

  it("releases the queue when an in-flight action errors (no deadlock)", async () => {
    const calls: string[] = [];
    const deferredExplain = createDeferred<DispatchConsumerResult>();

    const dispatchConsumer: DispatchConsumer = jest.fn((bindingId: string) => {
      calls.push(bindingId);
      if (bindingId === "binding-explain") return deferredExplain.promise;
      return Promise.resolve<DispatchConsumerResult>({ streamId: "s2", status: "complete" });
    });

    const { result } = renderHook(() => useSerialActionQueue(dispatchConsumer));

    let p1!: Promise<DispatchConsumerResult>;
    let p2!: Promise<DispatchConsumerResult>;
    act(() => {
      p1 = result.current.enqueue({ id: "1", bindingId: "binding-explain" });
      p2 = result.current.enqueue({ id: "2", bindingId: "binding-draft" });
    });

    await act(async () => {
      deferredExplain.reject(new Error("stream failed"));
      await expect(p1).rejects.toThrow("stream failed");
    });

    // The second (queued) request still runs — the error did not deadlock the queue.
    await act(async () => {
      await expect(p2).resolves.toEqual({ streamId: "s2", status: "complete" });
    });

    expect(calls).toEqual(["binding-explain", "binding-draft"]);
    expect(result.current.inFlightId).toBeNull();
    expect(result.current.pendingCount).toBe(0);
  });

  it("preserves FIFO order across more than two rapid-fire requests", async () => {
    const calls: string[] = [];
    const deferreds = [createDeferred<DispatchConsumerResult>(), createDeferred<DispatchConsumerResult>(), createDeferred<DispatchConsumerResult>()];
    let callIndex = 0;

    const dispatchConsumer: DispatchConsumer = jest.fn((bindingId: string) => {
      calls.push(bindingId);
      return deferreds[callIndex++].promise;
    });

    const { result } = renderHook(() => useSerialActionQueue(dispatchConsumer));

    const promises: Promise<DispatchConsumerResult>[] = [];
    act(() => {
      promises.push(result.current.enqueue({ id: "a", bindingId: "binding-a" }));
      promises.push(result.current.enqueue({ id: "b", bindingId: "binding-b" }));
      promises.push(result.current.enqueue({ id: "c", bindingId: "binding-c" }));
    });

    expect(calls).toEqual(["binding-a"]);
    expect(result.current.pendingCount).toBe(2);

    await act(async () => {
      deferreds[0].resolve({ streamId: "s0", status: "complete" });
      await promises[0];
    });
    expect(calls).toEqual(["binding-a", "binding-b"]);
    expect(result.current.pendingCount).toBe(1);

    await act(async () => {
      deferreds[1].resolve({ streamId: "s1", status: "complete" });
      await promises[1];
    });
    expect(calls).toEqual(["binding-a", "binding-b", "binding-c"]);
    expect(result.current.pendingCount).toBe(0);

    await act(async () => {
      deferreds[2].resolve({ streamId: "s2", status: "complete" });
      await promises[2];
    });
    expect(result.current.inFlightId).toBeNull();
  });

  it("forwards bindingId + args verbatim to the dispatchConsumer seam (no reinterpretation)", async () => {
    const dispatchConsumer: DispatchConsumer = jest
      .fn()
      .mockResolvedValue({ streamId: "s1", status: "complete" } satisfies DispatchConsumerResult);

    const { result } = renderHook(() => useSerialActionQueue(dispatchConsumer));

    await act(async () => {
      await result.current.enqueue({
        id: "1",
        bindingId: "binding-explain",
        args: { slots: { selection: "clause text" }, requiresAttachments: false },
      });
    });

    expect(dispatchConsumer).toHaveBeenCalledWith("binding-explain", {
      slots: { selection: "clause text" },
      requiresAttachments: false,
    });
  });
});
