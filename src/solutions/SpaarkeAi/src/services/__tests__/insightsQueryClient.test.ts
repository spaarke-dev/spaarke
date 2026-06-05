/**
 * insightsQueryClient — unit tests (R5 task 025 / D2-15).
 *
 * Covers the 12 acceptance criteria from `tasks/025-subject-resolution-http-client.poml`:
 *
 *   1. Subject resolution — happy paths (matter / project / invoice)
 *   2. Subject resolution — no-subject paths (undefined / unmapped / braces stripped)
 *   3. v1.1 SSE happy path (Accept negotiation + delta accumulation)
 *   4. Fresh token per call (no snapshot — ADR-028 / R5 CLAUDE.md §10)
 *   5. Correlation-id propagation (FR-17 / SC-16)
 *   6. v1.0 JSON direct path (no fallback needed)
 *   7. 406 fallback to JSON (NFR-11 graceful degradation)
 *   8. 12 contract error codes mapping (FR-16 / ADR-019)
 *   9. forceMode semantics (FR-12 / SC-17)
 *  10. v1.1 forward-compat passthrough (unknown fields survive)
 *  11. Zone B boundary grep (no server-internal imports)
 *  12. AbortSignal cancellation
 *
 * Test strategy:
 *   - Mock `@spaarke/auth` module-level: `authenticatedFetch` is a Jest mock
 *     fn we drive per-test; `ApiError` / `AuthError` are real classes.
 *   - For SSE tests, we construct mock `Response` objects with a stub
 *     `ReadableStream` body emitting hand-crafted SSE frames.
 *   - For token-rotation Test 4, we verify the mock `authenticatedFetch` is
 *     called twice (once per `callInsightsQuery` invocation) — proving the
 *     client does NOT close over a cached token from the first call. The
 *     fresh-token-per-call discipline is owned by `authenticatedFetch`
 *     itself; this test asserts the consumer does NOT bypass that.
 *
 * @see src/solutions/SpaarkeAi/src/services/insightsQueryClient.ts
 * @see projects/spaarke-ai-platform-unification-r5/notes/insights-engine-assistant-integration-brief.md
 * @see projects/spaarke-ai-platform-unification-r5/notes/insights-engine-contract-v1.1-request.md
 */

// ── Module-level mock for @spaarke/auth ─────────────────────────────────────
//
// The default test-setup `__mocks__/@spaarke/auth.ts` (mapped via jest.config
// moduleNameMapper) stubs `authenticatedFetch` + `useAuth` but does NOT export
// `ApiError` / `AuthError`. Re-mock at this test's module scope to provide
// the real error classes + a per-test-controlled `authenticatedFetch` mock.

jest.mock("@spaarke/auth", () => {
  // Inline class definitions matching `src/client/shared/Spaarke.Auth/src/errors.ts`.
  class ApiError extends Error {
    public readonly status: number;
    public readonly problemDetails: Record<string, unknown> | null;
    constructor(message: string, status: number, problemDetails: Record<string, unknown> | null = null) {
      super(message);
      this.name = "ApiError";
      this.status = status;
      this.problemDetails = problemDetails;
      Object.setPrototypeOf(this, ApiError.prototype);
    }
  }
  class AuthError extends Error {
    public readonly code: string;
    constructor(message: string, code = "auth_failed") {
      super(message);
      this.name = "AuthError";
      this.code = code;
      Object.setPrototypeOf(this, AuthError.prototype);
    }
  }
  return {
    ApiError,
    AuthError,
    authenticatedFetch: jest.fn(),
  };
});

import * as fs from "fs";
import * as path from "path";

import { ApiError, AuthError, authenticatedFetch } from "@spaarke/auth";

import {
  callInsightsQuery,
  InsightsQueryError,
  resolveSubject,
} from "../insightsQueryClient";

// ── Test helpers ────────────────────────────────────────────────────────────

const mockedFetch = authenticatedFetch as unknown as jest.Mock;

/** Build a mock `Response` with `application/json` body. */
function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers({ "content-type": "application/json" }),
    json: async () => body,
  } as unknown as Response;
}

/** Build a mock `Response` whose body is a `ReadableStream` of SSE frames. */
function sseResponse(frames: string[]): Response {
  const encoder = new TextEncoder();
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      for (const frame of frames) {
        controller.enqueue(encoder.encode(frame));
      }
      controller.close();
    },
  });
  return {
    ok: true,
    status: 200,
    headers: new Headers({ "content-type": "text/event-stream" }),
    body: stream,
    json: async () => ({}),
  } as unknown as Response;
}

beforeEach(() => {
  mockedFetch.mockReset();
});

// ───────────────────────────────────────────────────────────────────────────
// Test 1 — Subject resolution: happy paths (matter / project / invoice)
// ───────────────────────────────────────────────────────────────────────────

describe("resolveSubject — happy paths", () => {
  it("maps sprk_matter → matter:<guid>", () => {
    const result = resolveSubject("sprk_matter", "da116923-d65a-f111-a825-3833c5d9bcb1");
    expect(result).toEqual({
      kind: "subject",
      subject: "matter:da116923-d65a-f111-a825-3833c5d9bcb1",
      scheme: "matter",
      entityId: "da116923-d65a-f111-a825-3833c5d9bcb1",
    });
  });

  it("maps sprk_project → project:<guid>", () => {
    const result = resolveSubject("sprk_project", "27845394-8e5f-f111-a825-70a8a59455f4");
    expect(result).toEqual({
      kind: "subject",
      subject: "project:27845394-8e5f-f111-a825-70a8a59455f4",
      scheme: "project",
      entityId: "27845394-8e5f-f111-a825-70a8a59455f4",
    });
  });

  it("maps sprk_invoice → invoice:<guid>", () => {
    const result = resolveSubject("sprk_invoice", "05c8ef8d-8e5f-f111-a825-70a8a59455f4");
    expect(result).toEqual({
      kind: "subject",
      subject: "invoice:05c8ef8d-8e5f-f111-a825-70a8a59455f4",
      scheme: "invoice",
      entityId: "05c8ef8d-8e5f-f111-a825-70a8a59455f4",
    });
  });
});

// ───────────────────────────────────────────────────────────────────────────
// Test 2 — Subject resolution: no-subject paths
// ───────────────────────────────────────────────────────────────────────────

describe("resolveSubject — no-subject paths", () => {
  it("returns no-subject when both inputs are undefined", () => {
    expect(resolveSubject(undefined, undefined)).toEqual({ kind: "no-subject" });
  });

  it("returns no-subject when entityId is undefined", () => {
    expect(resolveSubject("sprk_matter", undefined)).toEqual({ kind: "no-subject" });
  });

  it("returns no-subject when entityLogicalName is undefined", () => {
    expect(resolveSubject(undefined, "abc-123")).toEqual({ kind: "no-subject" });
  });

  it("returns no-subject for an unmapped logical name (e.g., sprk_account)", () => {
    expect(resolveSubject("sprk_account", "abc-123")).toEqual({ kind: "no-subject" });
  });

  it("does NOT silently coerce unknown logical names to matter", () => {
    // Defensive — the canonical mapping is exhaustive and one-way; any unknown
    // logical name MUST return no-subject so a bug in the launch surface
    // doesn't get masked by an incorrect 'matter' default. (R5 CLAUDE.md §10)
    const result = resolveSubject("foo_bar", "abc-123");
    expect(result.kind).toBe("no-subject");
  });

  it("strips braces defensively (handles {guid} inputs)", () => {
    const result = resolveSubject("sprk_matter", "{abc-123-def}");
    expect(result).toEqual({
      kind: "subject",
      subject: "matter:abc-123-def",
      scheme: "matter",
      entityId: "abc-123-def",
    });
  });

  it("returns no-subject for an empty-string entityId after braces stripping", () => {
    expect(resolveSubject("sprk_matter", "{}")).toEqual({ kind: "no-subject" });
  });
});

// ───────────────────────────────────────────────────────────────────────────
// Test 3 — v1.1 SSE happy path
// ───────────────────────────────────────────────────────────────────────────

describe("callInsightsQuery — v1.1 SSE happy path", () => {
  it("consumes SSE delta + complete events and resolves to assembled envelope", async () => {
    mockedFetch.mockResolvedValueOnce(
      sseResponse([
        'event: metadata\ndata: {"path":"rag","correlationId":"server-id"}\n\n',
        'event: delta\ndata: {"content":"Hello "}\n\n',
        'event: delta\ndata: {"content":"World"}\n\n',
        'event: complete\ndata: {"answer":"Hello World","citations":[],"confidence":0.9}\n\n',
      ]),
    );

    const result = await callInsightsQuery({
      query: "What are the closing conditions?",
      subject: "matter:da116923-d65a-f111-a825-3833c5d9bcb1",
    });

    expect(result.kind).toBe("rag");
    expect(result.correlationId).toBeTruthy();
    expect(result.envelope.answer).toBe("Hello World");
    expect(result.envelope.path).toBe("rag");
    expect(result.envelope.confidence).toBe(0.9);

    // Outbound request shape: SSE opt-in + correlation-id header + JSON body.
    expect(mockedFetch).toHaveBeenCalledTimes(1);
    const [url, init] = mockedFetch.mock.calls[0];
    expect(url).toBe("/api/insights/assistant/query");
    expect((init.headers as Record<string, string>).Accept).toBe("text/event-stream");
    expect((init.headers as Record<string, string>)["x-correlation-id"]).toBe(result.correlationId);
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body)).toEqual({
      query: "What are the closing conditions?",
      subject: "matter:da116923-d65a-f111-a825-3833c5d9bcb1",
    });
  });
});

// ───────────────────────────────────────────────────────────────────────────
// Test 4 — Fresh token per call (no snapshot — ADR-028)
// ───────────────────────────────────────────────────────────────────────────

describe("callInsightsQuery — fresh token per call (ADR-028)", () => {
  it("invokes authenticatedFetch exactly once per call (no token snapshot)", async () => {
    // The client MUST NOT cache a token. By delegating to `authenticatedFetch`
    // exactly once per outbound call, the fresh-token-per-call discipline is
    // owned by the auth library (which re-acquires from the provider's cache
    // per request per ADR-028 / useAuth.ts line 36–46 comment).
    mockedFetch
      .mockResolvedValueOnce(jsonResponse({ path: "playbook", answer: "A" }))
      .mockResolvedValueOnce(jsonResponse({ path: "playbook", answer: "B" }));

    const r1 = await callInsightsQuery({ query: "q1", subject: "matter:abc" });
    const r2 = await callInsightsQuery({ query: "q2", subject: "matter:def" });

    expect(mockedFetch).toHaveBeenCalledTimes(2);
    expect(r1.correlationId).not.toBe(r2.correlationId); // distinct IDs ⇒ distinct calls
    // No module-level state — distinct results per call.
    expect((r1.envelope as Record<string, unknown>).answer).toBe("A");
    expect((r2.envelope as Record<string, unknown>).answer).toBe("B");
  });

  it("never references getAccessToken directly (defers token acquisition to authenticatedFetch)", () => {
    // Static check on the source — confirms no `getAccessToken()` call.
    // (This complements the runtime test above with a source-level guarantee.)
    const source = fs.readFileSync(
      path.join(__dirname, "..", "insightsQueryClient.ts"),
      "utf8",
    );
    expect(source).not.toMatch(/getAccessToken\(\)/);
    // Should also not have any module-level token cache.
    expect(source).not.toMatch(/let\s+\w*[Tt]oken\w*\s*=/);
    expect(source).not.toMatch(/const\s+\w*[Tt]oken[Cc]ache\w*\s*=/);
  });
});

// ───────────────────────────────────────────────────────────────────────────
// Test 5 — Correlation-id propagation (FR-17 / SC-16)
// ───────────────────────────────────────────────────────────────────────────

describe("callInsightsQuery — correlation-id propagation", () => {
  it("generates a fresh correlation-id per call and propagates it as request header", async () => {
    mockedFetch
      .mockResolvedValueOnce(jsonResponse({ path: "rag", answer: "x" }))
      .mockResolvedValueOnce(jsonResponse({ path: "rag", answer: "y" }));

    const r1 = await callInsightsQuery({ query: "a", subject: "matter:1" });
    const r2 = await callInsightsQuery({ query: "b", subject: "matter:2" });

    const headers1 = mockedFetch.mock.calls[0][1].headers as Record<string, string>;
    const headers2 = mockedFetch.mock.calls[1][1].headers as Record<string, string>;
    expect(headers1["x-correlation-id"]).toBeTruthy();
    expect(headers2["x-correlation-id"]).toBeTruthy();
    expect(headers1["x-correlation-id"]).not.toBe(headers2["x-correlation-id"]);
    expect(r1.correlationId).toBe(headers1["x-correlation-id"]);
    expect(r2.correlationId).toBe(headers2["x-correlation-id"]);
  });

  it("returns the same correlation-id on the success result and on a typed error", async () => {
    // Confirm error path also carries the client-generated correlation-id
    // (or the server-supplied one from problemDetails, when present).
    mockedFetch.mockRejectedValueOnce(
      new ApiError("bad request", 400, {
        title: "Invalid",
        detail: "Subject missing",
        errorCode: "subject.required",
        correlationId: "server-correlation-xyz",
      } as unknown as null),
    );

    await expect(
      callInsightsQuery({ query: "x", subject: "matter:1" }),
    ).rejects.toMatchObject({
      errorCode: "subject.required",
      correlationId: "server-correlation-xyz",
    });
  });
});

// ───────────────────────────────────────────────────────────────────────────
// Test 6 — v1.0 JSON direct path (no fallback)
// ───────────────────────────────────────────────────────────────────────────

describe("callInsightsQuery — v1.0 JSON direct path", () => {
  it("parses application/json envelope when server returns single-shot JSON", async () => {
    mockedFetch.mockResolvedValueOnce(
      jsonResponse({
        path: "playbook",
        answer: "Predicted cost ~$280k",
        playbookId: "predict-matter-cost@v1",
        confidence: 0.92,
        structuredResult: { kind: "inference", envelope: { predictedCost: 280000 } },
      }),
    );

    const result = await callInsightsQuery({
      query: "What's the cost?",
      subject: "matter:abc",
      forceMode: "playbook",
    });

    expect(result.kind).toBe("playbook");
    expect(result.envelope.playbookId).toBe("predict-matter-cost@v1");
    expect((result.envelope.structuredResult as Record<string, unknown>).kind).toBe("inference");
    // Only ONE outbound call — no 406 fallback was triggered.
    expect(mockedFetch).toHaveBeenCalledTimes(1);
  });
});

// ───────────────────────────────────────────────────────────────────────────
// Test 7 — 406 fallback to JSON (NFR-11)
// ───────────────────────────────────────────────────────────────────────────

describe("callInsightsQuery — 406 fallback to JSON", () => {
  it("retries once with Accept: application/json when first call returns 406", async () => {
    mockedFetch
      .mockRejectedValueOnce(new ApiError("Not Acceptable", 406, null))
      .mockResolvedValueOnce(
        jsonResponse({ path: "rag", answer: "fallback result", citations: [] }),
      );

    const result = await callInsightsQuery({ query: "q", subject: "matter:abc" });

    expect(result.kind).toBe("rag");
    expect(result.envelope.answer).toBe("fallback result");
    expect(mockedFetch).toHaveBeenCalledTimes(2);

    // First call: SSE opt-in
    const headers1 = mockedFetch.mock.calls[0][1].headers as Record<string, string>;
    expect(headers1.Accept).toBe("text/event-stream");

    // Second call: JSON fallback, SAME correlation-id reused
    const headers2 = mockedFetch.mock.calls[1][1].headers as Record<string, string>;
    expect(headers2.Accept).toBe("application/json");
    expect(headers2["x-correlation-id"]).toBe(headers1["x-correlation-id"]);
    expect(result.correlationId).toBe(headers1["x-correlation-id"]);
  });

  it("propagates fallback errors (e.g., server returns 500 on JSON retry)", async () => {
    mockedFetch
      .mockRejectedValueOnce(new ApiError("Not Acceptable", 406, null))
      .mockRejectedValueOnce(
        new ApiError("Internal", 500, {
          title: "Internal Server Error",
          detail: "Unexpected failure",
          errorCode: "INSIGHTS_ASSISTANT_INTERNAL_ERROR",
          correlationId: "abc",
        } as unknown as null),
      );

    await expect(
      callInsightsQuery({ query: "q", subject: "matter:abc" }),
    ).rejects.toBeInstanceOf(InsightsQueryError);
  });
});

// ───────────────────────────────────────────────────────────────────────────
// Test 8 — All 12 contract error codes
// ───────────────────────────────────────────────────────────────────────────

describe("callInsightsQuery — 12 contract error codes (FR-16 / ADR-019)", () => {
  /**
   * Per integration brief §5.1. Each maps a non-2xx ApiError → InsightsQueryError
   * preserving the 5 ProblemDetails fields. `auth.401` + `rate-limit.429` are
   * synthetic codes added when the server's ProblemDetails do not include
   * `errorCode` (e.g., the default 401 auth challenge, the default 429
   * rate-limit response).
   */
  const codes: Array<{ status: number; errorCode: string; title: string; detail: string }> = [
    { status: 400, errorCode: "query.required", title: "Bad Request", detail: "Query is required." },
    { status: 400, errorCode: "subject.required", title: "Bad Request", detail: "Subject is required." },
    { status: 400, errorCode: "subject.invalid", title: "Bad Request", detail: "Subject scheme is unknown." },
    { status: 400, errorCode: "forceMode.invalid", title: "Bad Request", detail: "forceMode must be 'playbook' or 'rag'." },
    { status: 400, errorCode: "conversationContext.invalid", title: "Bad Request", detail: "previousTurnSummary exceeds 2000 chars." },
    { status: 503, errorCode: "ai.insights.disabled", title: "Service Unavailable", detail: "Insights temporarily disabled." },
    { status: 503, errorCode: "ai.rag.disabled", title: "Service Unavailable", detail: "RAG path disabled." },
    { status: 503, errorCode: "ai.intent-classification.disabled", title: "Service Unavailable", detail: "Classifier disabled; provide forceMode." },
    { status: 503, errorCode: "ai.assistant-default-playbook.unconfigured", title: "Service Unavailable", detail: "Default playbook not configured." },
    { status: 500, errorCode: "INSIGHTS_ASSISTANT_INTERNAL_ERROR", title: "Internal Server Error", detail: "Unexpected internal failure." },
  ];

  codes.forEach(({ status, errorCode, title, detail }) => {
    it(`maps ${errorCode} (HTTP ${status}) → InsightsQueryError preserving the 5 ProblemDetails fields`, async () => {
      mockedFetch.mockRejectedValueOnce(
        new ApiError(detail, status, {
          title,
          detail,
          errorCode,
          correlationId: "server-corr-id",
        } as unknown as null),
      );

      let caught: unknown;
      try {
        await callInsightsQuery({ query: "x", subject: "matter:1" });
      } catch (e) {
        caught = e;
      }
      expect(caught).toBeInstanceOf(InsightsQueryError);
      const err = caught as InsightsQueryError;
      expect(err.errorCode).toBe(errorCode);
      expect(err.correlationId).toBe("server-corr-id");
      expect(err.status).toBe(status);
      expect(err.title).toBe(title);
      expect(err.detail).toBe(detail);
    });
  });

  // ── Synthetic 401 ───────────────────────────────────────────────────────────
  it("synthesizes auth.401 when AuthError is thrown (auth exhausted after retries)", async () => {
    mockedFetch.mockRejectedValueOnce(new AuthError("auth exhausted", "auth_exhausted"));

    let caught: unknown;
    try {
      await callInsightsQuery({ query: "x", subject: "matter:1" });
    } catch (e) {
      caught = e;
    }
    expect(caught).toBeInstanceOf(InsightsQueryError);
    const err = caught as InsightsQueryError;
    expect(err.errorCode).toBe("auth.401");
    expect(err.status).toBe(401);
  });

  // ── Synthetic 429 ─────────────────────────────────────────────────────────
  it("synthesizes rate-limit.429 with retryAfterSeconds when 429 + Retry-After present", async () => {
    mockedFetch.mockRejectedValueOnce(
      new ApiError("Too Many Requests", 429, {
        title: "Too Many Requests",
        detail: "Rate limit exceeded.",
        retryAfterSeconds: 30,
      } as unknown as null),
    );

    let caught: unknown;
    try {
      await callInsightsQuery({ query: "x", subject: "matter:1" });
    } catch (e) {
      caught = e;
    }
    expect(caught).toBeInstanceOf(InsightsQueryError);
    const err = caught as InsightsQueryError;
    expect(err.errorCode).toBe("rate-limit.429");
    expect(err.status).toBe(429);
    expect(err.retryAfterSeconds).toBe(30);
  });

  it("does NOT auto-retry on 429 (ADR-016 — chat-agent orchestration owns retry policy)", async () => {
    mockedFetch.mockRejectedValueOnce(
      new ApiError("Too Many Requests", 429, {
        title: "Too Many Requests",
        detail: "Slow down.",
      } as unknown as null),
    );

    await expect(
      callInsightsQuery({ query: "x", subject: "matter:1" }),
    ).rejects.toBeInstanceOf(InsightsQueryError);
    // Exactly ONE outbound call — no auto-retry.
    expect(mockedFetch).toHaveBeenCalledTimes(1);
  });
});

// ───────────────────────────────────────────────────────────────────────────
// Test 9 — forceMode semantics (FR-12 / SC-17)
// ───────────────────────────────────────────────────────────────────────────

describe("callInsightsQuery — forceMode semantics", () => {
  it("omits forceMode from request body when undefined", async () => {
    mockedFetch.mockResolvedValueOnce(jsonResponse({ path: "rag", answer: "" }));
    await callInsightsQuery({ query: "q", subject: "matter:1" });
    const body = JSON.parse(mockedFetch.mock.calls[0][1].body);
    expect(body).toEqual({ query: "q", subject: "matter:1" });
    expect(body).not.toHaveProperty("forceMode");
  });

  it("forwards forceMode='playbook' to request body", async () => {
    mockedFetch.mockResolvedValueOnce(jsonResponse({ path: "playbook", answer: "" }));
    await callInsightsQuery({ query: "q", subject: "matter:1", forceMode: "playbook" });
    const body = JSON.parse(mockedFetch.mock.calls[0][1].body);
    expect(body.forceMode).toBe("playbook");
  });

  it("forwards forceMode='rag' to request body", async () => {
    mockedFetch.mockResolvedValueOnce(jsonResponse({ path: "rag", answer: "" }));
    await callInsightsQuery({ query: "q", subject: "matter:1", forceMode: "rag" });
    const body = JSON.parse(mockedFetch.mock.calls[0][1].body);
    expect(body.forceMode).toBe("rag");
  });
});

// ───────────────────────────────────────────────────────────────────────────
// Test 10 — v1.1 forward-compat (unknown fields survive)
// ───────────────────────────────────────────────────────────────────────────

describe("callInsightsQuery — v1.1 forward-compat passthrough", () => {
  it("preserves unknown response fields untouched (citations[].href, streaming, confidence)", async () => {
    mockedFetch.mockResolvedValueOnce(
      jsonResponse({
        path: "rag",
        answer: "Cited prose",
        citations: [
          {
            n: 1,
            source: "Acme APA.pdf",
            excerpt: "Estimated cost: $282k",
            href: "https://spaarke-bff-dev.azurewebsites.net/api/v1/documents/abc/preview",
          },
        ],
        confidence: 0.42,
        streaming: true,
        v12FutureField: { unknownNested: ["a", "b"] },
      }),
    );

    const result = await callInsightsQuery({ query: "q", subject: "matter:1" });
    expect(result.kind).toBe("rag");
    const citations = result.envelope.citations as Array<Record<string, unknown>>;
    expect(citations[0].href).toBe(
      "https://spaarke-bff-dev.azurewebsites.net/api/v1/documents/abc/preview",
    );
    expect(result.envelope.confidence).toBe(0.42);
    expect(result.envelope.streaming).toBe(true);
    expect(result.envelope.v12FutureField).toEqual({ unknownNested: ["a", "b"] });
  });
});

// ───────────────────────────────────────────────────────────────────────────
// Test 11 — Zone B boundary grep
// ───────────────────────────────────────────────────────────────────────────

describe("Zone B boundary (R5 CLAUDE.md §3.5 / ADR-013 §3.5)", () => {
  it("module source contains NO imports from server-internal paths", () => {
    const source = fs.readFileSync(
      path.join(__dirname, "..", "insightsQueryClient.ts"),
      "utf8",
    );
    // No imports from `src/server/api/...` or `../server/...` or `../../../server/...`.
    expect(source).not.toMatch(/from\s+['"][^'"]*src\/server\/api[^'"]*['"]/);
    expect(source).not.toMatch(/from\s+['"](?:\.{2}\/){2,}server\//);
    expect(source).not.toMatch(/from\s+['"][^'"]*\/Insights\/[^'"]*['"]/);
    // No reference to server-internal Insights namespaces.
    expect(source).not.toMatch(/Sprk\.Bff\.Api/);
  });
});

// ───────────────────────────────────────────────────────────────────────────
// Test 12 — AbortSignal cancellation
// ───────────────────────────────────────────────────────────────────────────

describe("callInsightsQuery — AbortSignal cancellation", () => {
  it("forwards AbortSignal to the underlying fetch call", async () => {
    mockedFetch.mockResolvedValueOnce(jsonResponse({ path: "rag", answer: "" }));
    const controller = new AbortController();
    await callInsightsQuery({ query: "q", subject: "matter:1", signal: controller.signal });
    expect(mockedFetch.mock.calls[0][1].signal).toBe(controller.signal);
  });

  it("propagates AbortError unwrapped (does NOT translate to InsightsQueryError)", async () => {
    const abortError = new DOMException("The operation was aborted.", "AbortError");
    mockedFetch.mockRejectedValueOnce(abortError);
    const controller = new AbortController();
    controller.abort();

    await expect(
      callInsightsQuery({ query: "q", subject: "matter:1", signal: controller.signal }),
    ).rejects.toMatchObject({ name: "AbortError" });
  });
});
