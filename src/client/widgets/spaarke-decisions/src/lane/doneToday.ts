/**
 * The Decide lane's "done today" in the widget (DEF-001 #2832, spec FR-13).
 *
 * The Console's All card says "n of m done today": n is the number of the Decide lane's items in the caller's closed-today set
 * (`WorklistWidget.doneToday`), m is n plus what is still open. `spaarke_show_decisions` now returns that n as `closedToday` (a count per
 * category key; `all` is the Decide lane's total, the Console's number), read by the server from the same set as the caller. The widget
 * shows the `all` figure on the All card, which is the only card the shared panel gives a progress line (`laneCountFilters`).
 *
 * Absent means no line, never "0 done": an older server does not send `closedToday`, and a server whose closed-today read did not answer
 * leaves it out (the Console shows no line either when its closed-today set did not load).
 */

/**
 * `closedToday.all` from the `structuredContent` the host delivered for `spaarke_show_decisions` (the call that opened the widget):
 * a whole number of at least 0, or `undefined` when the result is not there yet, carries no `closedToday`, or carries something else.
 */
export function doneTodayOf(toolResult: unknown): number | undefined {
  if (typeof toolResult !== 'object' || toolResult === null) return undefined;
  const closedToday = (toolResult as { closedToday?: unknown }).closedToday;
  if (typeof closedToday !== 'object' || closedToday === null) return undefined;
  const all = (closedToday as { all?: unknown }).all;
  return typeof all === 'number' && Number.isInteger(all) && all >= 0 ? all : undefined;
}
