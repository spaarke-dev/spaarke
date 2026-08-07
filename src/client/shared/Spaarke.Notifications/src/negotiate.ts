import * as signalR from '@microsoft/signalr';
import { ApiError, authenticatedFetch } from '@spaarke/auth';
import type { NotificationSignal } from './types';

/** The SignalR client method name the server sends signals on (mirrors `SignalRDeliveryService.SignalMethod`). */
const SIGNAL_METHOD = 'notification';

/** The BFF negotiate endpoint (task 020, `Api/Notifications/NotificationsEndpoints.cs`). Relative — resolved
 * against the host's `@spaarke/auth` runtime config via `authenticatedFetch` (INV-4/INV-7: one shared config). */
const NEGOTIATE_PATH = '/notifications/negotiate';

/** Connection info returned by the negotiate endpoint. Mirrors `NotificationsNegotiateResponse` (task 020). */
export interface NegotiateResponse {
  /** Azure SignalR client access URL. */
  url: string;
  /**
   * Short-lived client access token minted server-side, scoped to the caller's oid.
   *
   * ADR-028 note: this is NOT a Spaarke BFF bearer token and is not the pattern the
   * `accessToken: string` MUST-NOT rule targets. It is a SignalR-service-scoped token
   * required verbatim by `@microsoft/signalr`'s `accessTokenFactory` contract — the same
   * class of documented exception as MSAL.js result objects / Power BI's
   * `IReportEmbedConfiguration`. It flows directly from this response into
   * `HubConnectionBuilder`'s `accessTokenFactory` closure in {@link connectSignalR} below
   * and is never logged, stored, or exposed as a React prop.
   */
  accessToken: string;
}

/**
 * Thrown when the server reports SignalR delivery is disabled (task 020's
 * Null-Object `NegotiateAsync` throws `FeatureDisabledException`, surfaced as
 * HTTP 503). This is NOT an auth failure — the caller is authenticated fine,
 * real-time delivery is simply unavailable in this environment. Callers
 * should treat this identically to a connect failure: fall back to poll
 * (FR-06) rather than surfacing a hard error to the end user.
 */
export class SignalRUnavailableError extends Error {
  constructor(
    message: string,
    public readonly cause?: unknown
  ) {
    super(message);
    this.name = 'SignalRUnavailableError';
    Object.setPrototypeOf(this, SignalRUnavailableError.prototype);
  }
}

/**
 * Calls task 020's negotiate endpoint via `@spaarke/auth`'s `authenticatedFetch`
 * (per ADR-028 — this is a normal authenticated BFF call, NOT the D-AUTH-7
 * exception; the exception is the SignalR transport connection built from this
 * response, in {@link connectSignalR} below).
 *
 * @throws {SignalRUnavailableError} when the server reports SignalR is disabled (503).
 * @throws {AuthError} (from `@spaarke/auth`) when token acquisition fails — an unauthenticated
 *   or expired-token caller sees this as a rejected promise, never a silent no-op.
 * @throws {ApiError} for any other non-2xx response.
 */
export async function negotiate(): Promise<NegotiateResponse> {
  let response: Response;
  try {
    response = await authenticatedFetch(NEGOTIATE_PATH, { method: 'POST' });
  } catch (err) {
    if (err instanceof ApiError && err.status === 503) {
      throw new SignalRUnavailableError(
        'Real-time notification delivery is disabled for this environment; caller should rely on poll fallback (FR-06).',
        err
      );
    }
    throw err;
  }

  const data = (await response.json()) as Partial<NegotiateResponse>;
  if (!data.url || !data.accessToken) {
    throw new Error('[@spaarke/notifications] negotiate() response is missing url/accessToken.');
  }

  return { url: data.url, accessToken: data.accessToken };
}

/**
 * Negotiates + opens the Layer-C SignalR connection, dispatching every
 * incoming {@link NotificationSignal} to `onSignal`.
 *
 * @param onSignal Invoked for every signal-only push the server sends. Callers
 *   typically wire this to `KindRouter.dispatch`.
 * @returns The live `HubConnection` — the caller owns its lifecycle (wire
 *   `onreconnecting`/`onreconnected`/`onclose` for poll-fallback switchover;
 *   see `NotificationsClient`).
 */
export async function connectSignalR(onSignal: (signal: NotificationSignal) => void): Promise<signalR.HubConnection> {
  const info = await negotiate();

  // Auth v2 (D-AUTH-7): the SignalR client SDK owns its own transport connection
  // (WebSocket, falling back to Server-Sent Events / long-polling) to the Azure
  // SignalR client access URL above — it is NOT a call to the Spaarke BFF, and the
  // SDK internally manages the network calls needed to establish + maintain that
  // connection using the short-lived token minted by negotiate() (which itself
  // used authenticatedFetch, per ADR-028). This is the ONE enumerated raw-fetch
  // exception for this library — every other HTTP call this package makes goes
  // through authenticatedFetch.
  //
  // spaarkeai-assistant-enhancements-r2 Phase 0 FIX 3 (2026-08-07): `accessTokenFactory`
  // used to close over the ONE `info.accessToken` snapshot taken above and return it
  // forever — including on every `withAutomaticReconnect` attempt, by which point that
  // short-lived SignalR-service token may well have expired. The SDK calls
  // `accessTokenFactory` again before each (re)connect attempt specifically so callers
  // can hand back a FRESH token — it supports returning a `Promise<string>`, so we
  // re-run `negotiate()` (a normal `authenticatedFetch` call, itself already
  // 401-retry-with-backoff resilient per authenticatedFetch.ts) on every invocation
  // instead of reusing the snapshot. The originally negotiated `info.url` is reused for
  // the connection's transport endpoint (fixed at `withUrl` build time); only the TOKEN
  // is refreshed per-call.
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(info.url, {
      accessTokenFactory: async () => {
        try {
          const fresh = await negotiate();
          return fresh.accessToken;
        } catch (err) {
          // Non-fatal: fall back to the last-known token rather than throwing out of
          // the SDK's internal token-fetch path (an uncaught rejection here would
          // abort the (re)connect attempt with a less diagnosable error). The
          // subsequent connect/reconnect attempt will surface its own failure/backoff
          // via the normal SignalR retry policy if this stale token is also rejected.
          console.warn('[@spaarke/notifications] accessTokenFactory re-negotiate failed; reusing last-known token:', err);
          return info.accessToken;
        }
      },
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  connection.on(SIGNAL_METHOD, (signal: NotificationSignal) => onSignal(signal));

  await connection.start();
  return connection;
}
