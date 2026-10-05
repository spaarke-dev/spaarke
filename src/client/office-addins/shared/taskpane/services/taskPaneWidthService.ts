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
 * (API unsupported, unknown platform, or a failure). Never throws.
 */
export function requestWiderTaskPane(): number | null {
  try {
    if (!Office.context.requirements.isSetSupported('TaskPaneApi', '1.1')) return null;
    const width = preferredTaskPaneWidth(String(Office.context.platform));
    if (width === null) return null;
    const taskpane = (Office as unknown as TaskPaneLifeCycle).extensionLifeCycle?.taskpane;
    if (!taskpane || typeof taskpane.setWidth !== 'function') return null;
    taskpane.setWidth(width);
    return width;
  } catch {
    return null;
  }
}
