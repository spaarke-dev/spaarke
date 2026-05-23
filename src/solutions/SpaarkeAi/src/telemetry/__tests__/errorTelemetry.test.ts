/**
 * errorTelemetry — unit tests
 *
 * Covers the acceptance criteria for task 013:
 *   - When App Insights is wired, properties pass through to `trackEvent` and
 *     the event name matches the input.
 *   - When App Insights is undefined / not initialized, the helper returns
 *     silently without throwing.
 *   - All three exported event-name constants are prefixed with
 *     `spaarke-ai-error.`.
 *   - Calling `setAppInsightsInstance(null | undefined)` reverts to no-op
 *     mode.
 *   - `trackEvent` throwing internally never propagates to the caller.
 */

import type { ApplicationInsights } from "@microsoft/applicationinsights-web";

import {
  TELEMETRY_DAILY_BRIEFING_429,
  TELEMETRY_FILE_EXTRACTION_FAILURE,
  TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE,
  __resetForTests,
  logTelemetryError,
  setAppInsightsInstance,
} from "../errorTelemetry";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface FakeAppInsights {
  trackEvent: jest.Mock;
}

function makeFakeAppInsights(): FakeAppInsights {
  return {
    trackEvent: jest.fn(),
  };
}

beforeEach(() => {
  __resetForTests();
});

// ---------------------------------------------------------------------------
// Event-name constants
// ---------------------------------------------------------------------------

describe("errorTelemetry — exported event-name constants", () => {
  it("exports the Daily Briefing 429 event name with the correct prefix", () => {
    expect(TELEMETRY_DAILY_BRIEFING_429).toMatch(/^spaarke-ai-error\./);
    expect(TELEMETRY_DAILY_BRIEFING_429).toBe(
      "spaarke-ai-error.daily-briefing.rate-limited",
    );
  });

  it("exports the file-extraction-failure event name with the correct prefix", () => {
    expect(TELEMETRY_FILE_EXTRACTION_FAILURE).toMatch(/^spaarke-ai-error\./);
    expect(TELEMETRY_FILE_EXTRACTION_FAILURE).toBe(
      "spaarke-ai-error.chat.file-extraction-failure",
    );
  });

  it("exports the HistoryOverlay load-failure event name with the correct prefix", () => {
    expect(TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE).toMatch(
      /^spaarke-ai-error\./,
    );
    expect(TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE).toBe(
      "spaarke-ai-error.history-overlay.load-failure",
    );
  });

  it("all three constants are distinct", () => {
    const names = new Set([
      TELEMETRY_DAILY_BRIEFING_429,
      TELEMETRY_FILE_EXTRACTION_FAILURE,
      TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE,
    ]);
    expect(names.size).toBe(3);
  });
});

// ---------------------------------------------------------------------------
// No-op fallback (App Insights unavailable)
// ---------------------------------------------------------------------------

describe("errorTelemetry — no-op fallback when App Insights is absent", () => {
  it("returns silently when no instance has been wired", () => {
    expect(() =>
      logTelemetryError(TELEMETRY_DAILY_BRIEFING_429, { statusCode: 429 }),
    ).not.toThrow();
  });

  it("returns silently after the instance is explicitly cleared with null", () => {
    const fake = makeFakeAppInsights();
    setAppInsightsInstance(fake as unknown as ApplicationInsights);
    setAppInsightsInstance(null);

    expect(() => logTelemetryError("spaarke-ai-error.test", {})).not.toThrow();
    expect(fake.trackEvent).not.toHaveBeenCalled();
  });

  it("returns silently after the instance is explicitly cleared with undefined", () => {
    const fake = makeFakeAppInsights();
    setAppInsightsInstance(fake as unknown as ApplicationInsights);
    setAppInsightsInstance(undefined);

    expect(() => logTelemetryError("spaarke-ai-error.test", {})).not.toThrow();
    expect(fake.trackEvent).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// Happy path — instance wired, trackEvent invoked
// ---------------------------------------------------------------------------

describe("errorTelemetry — when App Insights is wired", () => {
  it("invokes trackEvent with the event name and properties unchanged", () => {
    const fake = makeFakeAppInsights();
    setAppInsightsInstance(fake as unknown as ApplicationInsights);

    const props = {
      statusCode: 429,
      retryAfterSeconds: 30,
      correlationId: "abc123",
    };
    logTelemetryError(TELEMETRY_DAILY_BRIEFING_429, props);

    expect(fake.trackEvent).toHaveBeenCalledTimes(1);
    expect(fake.trackEvent).toHaveBeenCalledWith(
      { name: TELEMETRY_DAILY_BRIEFING_429 },
      props,
    );
  });

  it("preserves nested property values without mutation", () => {
    const fake = makeFakeAppInsights();
    setAppInsightsInstance(fake as unknown as ApplicationInsights);

    const props: Record<string, unknown> = {
      nested: { reason: "PDF parse failed", page: 7 },
      timestamps: [1, 2, 3],
    };
    logTelemetryError(TELEMETRY_FILE_EXTRACTION_FAILURE, props);

    // Verify pass-through reference equality — the helper does not clone
    // properties (clone-on-pass would be wasteful for error paths).
    const [, passedProps] = fake.trackEvent.mock.calls[0];
    expect(passedProps).toBe(props);
    expect(passedProps).toEqual({
      nested: { reason: "PDF parse failed", page: 7 },
      timestamps: [1, 2, 3],
    });
  });

  it("handles an empty properties object", () => {
    const fake = makeFakeAppInsights();
    setAppInsightsInstance(fake as unknown as ApplicationInsights);

    logTelemetryError(TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE, {});

    expect(fake.trackEvent).toHaveBeenCalledTimes(1);
    expect(fake.trackEvent).toHaveBeenCalledWith(
      { name: TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE },
      {},
    );
  });

  it("accepts a custom event name (not one of the exported constants)", () => {
    const fake = makeFakeAppInsights();
    setAppInsightsInstance(fake as unknown as ApplicationInsights);

    logTelemetryError("spaarke-ai-error.custom.test-event", { foo: "bar" });

    expect(fake.trackEvent).toHaveBeenCalledWith(
      { name: "spaarke-ai-error.custom.test-event" },
      { foo: "bar" },
    );
  });
});

// ---------------------------------------------------------------------------
// Defensive — trackEvent throws internally
// ---------------------------------------------------------------------------

describe("errorTelemetry — defensive behavior", () => {
  it("never propagates exceptions from trackEvent to the caller", () => {
    const fake: FakeAppInsights = {
      trackEvent: jest.fn().mockImplementation(() => {
        throw new Error("simulated App Insights failure");
      }),
    };
    setAppInsightsInstance(fake as unknown as ApplicationInsights);

    expect(() =>
      logTelemetryError(TELEMETRY_DAILY_BRIEFING_429, { statusCode: 429 }),
    ).not.toThrow();
    expect(fake.trackEvent).toHaveBeenCalledTimes(1);
  });
});
