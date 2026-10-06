/**
 * taskPaneWidthService — widen the task pane by 75 px over the host default (task 095, owner UAT round 4, item 1).
 *
 * Microsoft Learn (verified 2026-10-04, see projects/spaarkeai-word-add-in-r1/notes/095-related-to-and-save-as.md):
 * there is NO manifest setting for a task pane's width in a released schema, but a RUNTIME API exists —
 * `Office.extensionLifeCycle.taskpane.setWidth(px)` (requirement set TaskPaneApi 1.1; Word on the web, Office on
 * Windows 2507+, Mac 16.100.4+). A value outside the platform's limits is silently ignored (no error), and the
 * call returns void, so there is nothing to check afterwards.
 *
 * Per-platform default + the +75 px target (limits: web 330-500; Windows 86 px-50% of the window; Mac 270 px-50%):
 *   Word on the web 330 -> 405 · Windows 320 -> 395 · Mac 270 -> 345.
 *
 * NFR-10: decided by requirement set + platform, never by host type. Best effort: any failure is swallowed — the
 * pane simply keeps the host's default width.
 */

/** Pixels added to each platform's default task pane width (owner, 2026-10-04). */
export const TASK_PANE_WIDTH_INCREASE_PX = 75;

/** Documented default task pane widths by platform (Office.TaskPane docs). */
const DEFAULT_WIDTH_BY_PLATFORM: Readonly<Record<string, number>> = {
  OfficeOnline: 330,
  PC: 320,
  Mac: 270,
};

/** The preferred width for a platform, or `null` when the platform's default is not documented. */
export function preferredTaskPaneWidth(platform: string | undefined): number | null {
  if (!platform) return null;
  const base = DEFAULT_WIDTH_BY_PLATFORM[platform];
  return base === undefined ? null : base + TASK_PANE_WIDTH_INCREASE_PX;
}

interface TaskPaneLifeCycle {
  extensionLifeCycle?: { taskpane?: { setWidth?: (width: number) => void } };
}

/**
 * Asks the host for the wider task pane. Returns the width requested, or `null` when nothing was requested
 * (API unsupported, unknown platform, the pane is already at least that wide, or a failure). Never throws.
 *
 * Widen only, never shrink: the pane runs at load, so a pane the user has already dragged wider than the target
 * (`currentWidth`, the pane's own viewport width) is left alone rather than snapped back on every open.
 */
export function requestWiderTaskPane(currentWidth: number = window.innerWidth): number | null {
  try {
    if (!Office.context.requirements.isSetSupported('TaskPaneApi', '1.1')) return null;
    const width = preferredTaskPaneWidth(String(Office.context.platform));
    if (width === null) return null;
    if (Number.isFinite(currentWidth) && currentWidth >= width) return null;
    const taskpane = (Office as unknown as TaskPaneLifeCycle).extensionLifeCycle?.taskpane;
    if (!taskpane || typeof taskpane.setWidth !== 'function') return null;
    taskpane.setWidth(width);
    return width;
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// Expand / collapse (task 103, owner UAT round 6 item 6)
// ---------------------------------------------------------------------------

/** The expanded pane targets this multiple of the platform default (owner: "3x"). */
export const TASK_PANE_EXPAND_FACTOR = 3;

/** Word on the web caps the pane at 500 px (Office.TaskPane docs), so it is a known-good last step there. */
const WEB_MAX_WIDTH_PX = 500;

/** Fractions of the screen's available width tried after the 3x request (Windows/Mac allow up to 50% of the Word window). */
const SCREEN_FRACTION_STEPS: readonly number[] = [0.5, 0.4, 0.3];

/** How long to wait for the host to apply a width before treating the request as ignored. */
const RESIZE_SETTLE_TIMEOUT_MS = 300;

/** A width change smaller than this is not treated as the host applying the request. */
const RESIZE_TOLERANCE_PX = 4;

function getSetWidth(): ((width: number) => void) | null {
  const taskpane = (Office as unknown as TaskPaneLifeCycle).extensionLifeCycle?.taskpane;
  return taskpane && typeof taskpane.setWidth === 'function' ? taskpane.setWidth.bind(taskpane) : null;
}

/**
 * Whether this host can resize the pane at runtime: the TaskPaneApi 1.1 requirement set is supported, `setWidth`
 * exists, and the platform's default width is documented. Decided by requirement set, never by host type (NFR-10).
 * Never throws.
 */
export function isTaskPaneResizeSupported(): boolean {
  try {
    if (!Office.context.requirements.isSetSupported('TaskPaneApi', '1.1')) return false;
    if (preferredTaskPaneWidth(String(Office.context.platform)) === null) return false;
    return getSetWidth() !== null;
  } catch {
    return false;
  }
}

/**
 * The widths to try for "expand", widest first: 3x the platform default, then fractions of the screen, then the
 * web cap. Out-of-limit requests are silently ignored by the host, so the caller steps down until one takes.
 * Only widths meaningfully above `currentWidth` are candidates.
 */
export function expandCandidateWidths(
  platform: string | undefined,
  currentWidth: number,
  screenWidth: number = typeof window !== 'undefined' ? (window.screen?.availWidth ?? 0) : 0
): number[] {
  if (!platform) return [];
  const base = DEFAULT_WIDTH_BY_PLATFORM[platform];
  if (base === undefined) return [];
  const candidates = [base * TASK_PANE_EXPAND_FACTOR];
  if (Number.isFinite(screenWidth) && screenWidth > 0) {
    for (const fraction of SCREEN_FRACTION_STEPS) candidates.push(Math.floor(screenWidth * fraction));
  }
  if (platform === 'OfficeOnline') candidates.push(WEB_MAX_WIDTH_PX);
  const floor = Number.isFinite(currentWidth) ? currentWidth + RESIZE_TOLERANCE_PX : 0;
  return Array.from(new Set(candidates))
    .filter(width => width > floor)
    .sort((a, b) => b - a);
}

/** Resolves once the window resizes or the timeout passes, whichever is first. */
function waitForResize(timeoutMs: number): Promise<void> {
  return new Promise(resolve => {
    const timer = setTimeout(() => done(), timeoutMs);
    function done(): void {
      window.removeEventListener('resize', done);
      clearTimeout(timer);
      resolve();
    }
    window.addEventListener('resize', done);
  });
}

/**
 * Widens the pane as far as the host allows, up to 3x the platform default: requests each candidate width from
 * widest to narrowest until the pane's own viewport width actually changes. Returns the width that took, or
 * `null` when none did (API unsupported, nothing wider than the current width, every request ignored, or a
 * failure). Never throws; a failure leaves the width unchanged.
 */
export async function expandTaskPane(): Promise<number | null> {
  try {
    if (!isTaskPaneResizeSupported()) return null;
    const setWidth = getSetWidth();
    if (!setWidth) return null;
    const candidates = expandCandidateWidths(String(Office.context.platform), window.innerWidth);
    for (const width of candidates) {
      const before = window.innerWidth;
      setWidth(width);
      await waitForResize(RESIZE_SETTLE_TIMEOUT_MS);
      if (Math.abs(window.innerWidth - before) > RESIZE_TOLERANCE_PX) return width;
    }
    return null;
  } catch {
    return null;
  }
}

/**
 * Returns the pane to the round-4 width (`preferredTaskPaneWidth`, default + 75). Returns the width requested, or
 * `null` when nothing was requested. Unlike {@link requestWiderTaskPane} this does shrink: it is the explicit
 * "collapse" action. Never throws.
 */
export function collapseTaskPane(): number | null {
  try {
    if (!isTaskPaneResizeSupported()) return null;
    const width = preferredTaskPaneWidth(String(Office.context.platform));
    const setWidth = getSetWidth();
    if (width === null || !setWidth) return null;
    setWidth(width);
    return width;
  } catch {
    return null;
  }
}
