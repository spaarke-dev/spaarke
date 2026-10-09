/**
 * useAttachments — the `/documents` promote retry for a server failure.
 *
 * The promote paths retry a 5xx once ("4xx won't succeed on retry"), but that rule lived only in the
 * `!response.ok` branch. `@spaarke/auth`'s authenticatedFetch THROWS ApiError for a non-OK response, so
 * a 503 landed in the catch, which retried network errors only: the chip was failed at once and the
 * carried-over file got "couldn't attach" without a second attempt. The fetch here throws the REAL
 * ApiError (the `@spaarke/auth` jest stub re-exports the real class).
 */
import { act, renderHook, waitFor } from "@testing-library/react";
import { ApiError } from "@spaarke/auth";
import type { AttachmentChip, ChatAttachment } from "@spaarke/ui-components";
import { useAttachments, type AttachmentsDeps } from "../useAttachments";
import type { EventBatchMachine } from "../useEventBatch";

const FILENAME = "brief.txt";

const chip = (): AttachmentChip =>
  ({ id: "chip-1", filename: FILENAME, sizeBytes: 5, mimeType: "text/plain", status: "ready" }) as AttachmentChip;

const attachment = (): ChatAttachment => ({ filename: FILENAME, contentType: "text/plain", textContent: "hello" });

function makeEventBatch() {
  return {
    noteChipsChanged: jest.fn(),
    noteChipRemoved: jest.fn(),
    markEventFilePromotionFailed: jest.fn(),
    queueDocumentUploadedEvent: jest.fn(),
    noteOutboundMessage: jest.fn(),
    fireForPromotedFile: jest.fn(),
  };
}

function setup(fetchImpl: jest.Mock, overrides: Partial<AttachmentsDeps> = {}) {
  const eventBatch = makeEventBatch();
  const inject = jest.fn();
  const initial: AttachmentsDeps = {
    bffBaseUrl: "https://bff.example.com",
    chatSessionId: "session-1",
    hasActiveWorkspaceDocument: false,
    hasPriorMessages: false,
    authenticatedFetch: fetchImpl,
    dispatch: jest.fn(),
    inject,
    eventBatch: eventBatch as unknown as EventBatchMachine,
    ...overrides,
  };
  const hook = renderHook((deps: AttachmentsDeps) => useAttachments(deps), { initialProps: initial });
  return { hook, eventBatch, inject, initial };
}

const accepted = () =>
  ({ ok: true, status: 202, json: async () => ({ documentId: "doc-1" }) }) as unknown as Response;

beforeEach(() => {
  jest.spyOn(console, "error").mockImplementation(() => undefined);
  jest.spyOn(console, "log").mockImplementation(() => undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
});

describe("auto-promote of a ready chip", () => {
  it("a thrown 503 on the first attempt is retryable — the chip is NOT failed", async () => {
    const fetchImpl = jest.fn().mockRejectedValueOnce(new ApiError("HTTP 503", 503, null));
    const { hook, eventBatch } = setup(fetchImpl);

    act(() => {
      hook.result.current.handleAttachmentReady(attachment());
      hook.result.current.handleAttachmentsChanged([chip()]);
    });

    await waitFor(() => expect(fetchImpl).toHaveBeenCalledTimes(1), { timeout: 3000 });
    // The retry path waits PROMOTE_RETRY_DELAY_MS and leaves the chip eligible.
    await waitFor(() => expect(hook.result.current.isPromoting).toBe(false), { timeout: 3000 });
    expect(eventBatch.markEventFilePromotionFailed).not.toHaveBeenCalled();
  });

  it("a thrown 400 is permanent — the chip is failed without a retry", async () => {
    const fetchImpl = jest.fn().mockRejectedValueOnce(new ApiError("Unsupported file", 400, null));
    const { hook, eventBatch } = setup(fetchImpl);

    act(() => {
      hook.result.current.handleAttachmentReady(attachment());
      hook.result.current.handleAttachmentsChanged([chip()]);
    });

    await waitFor(() => expect(eventBatch.markEventFilePromotionFailed).toHaveBeenCalledWith("chip-1"), {
      timeout: 3000,
    });
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });
});

describe("carry-over promote into a new session", () => {
  async function carryOver(fetchImpl: jest.Mock) {
    const { hook, inject, initial } = setup(fetchImpl, { hasPriorMessages: true });
    act(() => {
      hook.result.current.handleAttachmentReady(attachment());
      hook.result.current.handleAttachmentsChanged([chip()]);
    });
    act(() => {
      hook.result.current.prepareFilesForNewSession();
    });
    hook.rerender({ ...initial, hasPriorMessages: false, chatSessionId: "session-2" });
    return { inject };
  }

  it("a thrown 503 is retried once and the second attempt attaches the file", async () => {
    const fetchImpl = jest
      .fn()
      .mockRejectedValueOnce(new ApiError("HTTP 503", 503, null))
      .mockResolvedValueOnce(accepted());
    const { inject } = await carryOver(fetchImpl);

    await waitFor(() => expect(fetchImpl).toHaveBeenCalledTimes(2), { timeout: 4000 });
    await waitFor(() =>
      expect(inject).toHaveBeenCalledWith(
        expect.objectContaining({ content: expect.stringContaining(`Started a new session with "${FILENAME}"`) })
      )
    );
  });

  it("a thrown 400 is not retried", async () => {
    const fetchImpl = jest.fn().mockRejectedValueOnce(new ApiError("Unsupported file", 400, null));
    const { inject } = await carryOver(fetchImpl);

    await waitFor(() =>
      expect(inject).toHaveBeenCalledWith(
        expect.objectContaining({ content: expect.stringContaining("couldn't attach") })
      )
    );
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });
});
