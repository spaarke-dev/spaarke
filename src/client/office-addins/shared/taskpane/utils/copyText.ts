/**
 * copyText — task 116 (owner UAT 2026-10-08, B2B guest in Outlook on the web): "Copy Link" said "Couldn't copy" with
 * the console reporting "The Clipboard API has been blocked because of a permissions policy applied to the current
 * document". The add-in runs in an iframe whose host (Outlook on the web) may not delegate `clipboard-write`.
 *
 * Tries the async Clipboard API first, then the selection-based `document.execCommand('copy')`, which that permissions
 * policy does not govern (it needs only the click's user activation). Returns whether the text was copied; never
 * throws. A caller whose copy still fails should show the text for the user to copy by hand.
 */
export async function copyText(text: string): Promise<boolean> {
  try {
    if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // Blocked by the host's permissions policy (or unavailable) — fall through to the selection-based copy.
  }
  return copyWithSelection(text);
}

function copyWithSelection(text: string): boolean {
  if (typeof document === 'undefined' || typeof document.execCommand !== 'function') return false;
  const area = document.createElement('textarea');
  area.value = text;
  area.setAttribute('readonly', '');
  area.style.position = 'fixed';
  area.style.top = '-1000px';
  area.style.opacity = '0';
  document.body.appendChild(area);
  try {
    area.select();
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    document.body.removeChild(area);
  }
}
