/**
 * Copy-to-clipboard with a fallback for environments where the async Clipboard API is blocked.
 *
 * WHY A SHARED HELPER (task 028, CLAUDE.md §11). Two hand-rolled copies of this logic already exist
 * — `FileDetailPanel.tsx` and `ItemResultsGrid.tsx` — each a closure bound to its own component
 * state, so neither is extendable. Task 028 needs copying in two more places (the Containers grid and
 * the container detail panel), which would have made four. The fallback branch is the part that
 * matters: this app runs as a Dataverse code page inside an IFRAME, where `navigator.clipboard` is
 * gated by the host's Permissions-Policy and can reject even in a secure context. A fourth
 * hand-copied fallback is a fourth chance to get that branch subtly wrong.
 *
 * The two existing call sites are deliberately NOT refactored here — that is unrelated to FR-C10 and
 * would widen this task's blast radius into the file-browser and search screens. Recorded in
 * notes/task-028-findings.md as a follow-up.
 */

/**
 * Writes `text` to the clipboard.
 *
 * @returns `true` when the text was copied, `false` when both the API and the fallback failed.
 *          Callers MUST branch on this rather than assuming success — silently showing "Copied!"
 *          when nothing reached the clipboard is exactly the class of lie this project exists to
 *          remove, and the admin only discovers it when they paste into Purview and get nothing.
 */
export async function copyToClipboard(text: string): Promise<boolean> {
  if (!text) return false;

  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    // Blocked by Permissions-Policy, a non-secure context, or an unfocused document.
    // Fall through to the legacy path rather than reporting failure straight away.
  }

  // Legacy fallback. The element must remain part of the layout and selectable —
  // `display: none`, `hidden`, and `visibility: hidden` all make execCommand("copy") a no-op.
  let el: HTMLTextAreaElement | null = null;
  try {
    el = document.createElement("textarea");
    el.value = text;
    el.setAttribute("readonly", "");
    el.setAttribute("aria-hidden", "true");
    el.style.position = "fixed";
    el.style.top = "0";
    el.style.left = "0";
    el.style.width = "1px";
    el.style.height = "1px";
    el.style.padding = "0";
    el.style.border = "none";
    el.style.outline = "none";
    el.style.boxShadow = "none";
    el.style.background = "transparent";
    // iOS Safari ignores .select() unless a range is set explicitly.
    el.style.fontSize = "16px";

    document.body.appendChild(el);
    el.select();
    el.setSelectionRange(0, text.length);

    return document.execCommand("copy");
  } catch {
    return false;
  } finally {
    if (el?.parentNode) el.parentNode.removeChild(el);
  }
}
