/**
 * Lint CONTROLS for the catch policy (`no-restricted-syntax` selectors + `spaarke-client/catch-disable-needs-reason`).
 * Never imported, never executed. Marker syntax: see `client-lint.mjs` (`@control must-fire <ruleId>` / `@control must-not-fire`);
 * the marker names the rule that must report on the line AFTER it. Each catch sits on one line so the reported line is the target.
 */
declare function work(): void;
declare function handle(e: unknown): void;
declare function reportClientError(e: unknown): void;
declare const logger: { logError(scope: string, msg: string, e: unknown): void };

export function emptyCatch(): void {
  // @control must-fire no-restricted-syntax
  try { work(); } catch (e) {}
}

export function commentOnlyCatch(): void {
  // @control must-fire no-restricted-syntax
  try { work(); } catch { /* ignored */ }
}

export function consoleOnlyCatch(): void {
  // @control must-fire no-restricted-syntax
  try { work(); } catch (e) { console.error("failed", e); }
}

export function telemetryOnlyCatch(): void {
  // @control must-fire no-restricted-syntax
  try { work(); } catch (e) { reportClientError(e); }
}

export function loggerOnlyCatch(): void {
  // @control must-fire no-restricted-syntax
  try { work(); } catch (e) { logger.logError("x", "failed", e); }
}

export function disableWithoutReason(): void {
  // @control must-fire spaarke-client/catch-disable-needs-reason
  // eslint-disable-next-line no-restricted-syntax
  try { work(); } catch (e) {}
}

export function blockDisableWithoutReason(): void {
  // @control must-fire spaarke-client/catch-disable-needs-reason
  /* eslint-disable-next-line no-restricted-syntax */
  try { work(); } catch (e) {}
}

export function rethrowingCatch(): void {
  // @control must-not-fire
  try { work(); } catch (e) { throw e; }
}

export function handlingCatch(): void {
  // @control must-not-fire
  try { work(); } catch (e) { handle(e); }
}

export function logAndHandleCatch(): number {
  // @control must-not-fire
  try { work(); return 1; } catch (e) { console.error("failed", e); return 0; }
}

export function reasonedDisable(): void {
  // @control must-not-fire
  // eslint-disable-next-line no-restricted-syntax -- best-effort cache warm; failure is harmless
  try { work(); } catch (e) {}
}
