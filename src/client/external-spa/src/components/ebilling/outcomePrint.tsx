/**
 * outcomePrint — the print surface of the outcome report (ebilling-r1 task 134, EB-D-254, review fix round 1 F1).
 *
 * WHY. The report lives in hosts that clip it: the record modal's content scrolls (`overflow-y: auto`) and the submission wizard's
 * frame has a fixed height and `overflow: hidden`, so printing the page prints only the slice that is visible on screen. Printing
 * therefore uses a COPY of the report in a surface with no container limits.
 *
 * HOW. "Print or save as PDF" calls {@link useOutcomePrint}`.start()`, which, in this order:
 *   1. appends a dedicated root (`#sprk-outcome-print-root`) to `document.body`;
 *   2. renders the copy into it through a React portal, inside a `FluentProvider` with the LIGHT theme (so a dark-mode page prints
 *      in light colours), flushed synchronously so it exists when the browser snapshots the page;
 *   3. sets `sprk-outcome-printing` on `<html>`;
 *   4. calls `window.print()`.
 * It undoes all of it (class, portal, root) on `afterprint`, and also on a fallback timer (a browser that never reports
 * `afterprint`), and on unmount. Only one print is in progress per report.
 *
 * THE ONE GLOBAL RULE (`PRINT_GLOBAL_CSS`, injected once with Griffel's `makeStaticStyles`) is gated on the html class and on
 * `@media print`: with the class set, everything in `<body>` except the print root is hidden and the page's own fixed height and
 * clipping (`index.html`: `html, body { height: 100%; overflow: hidden }`) are lifted. Without the class the rule matches nothing,
 * so it never touches the record modal or the grids. The print root is `display: none` on screen and shown only in print, so
 * it is never visible between the click and the print dialog.
 */
import * as React from 'react';
import { createPortal, flushSync } from 'react-dom';
import { FluentProvider, makeStaticStyles, webLightTheme } from '@fluentui/react-components';

/** The class set on `<html>` while a report print is in progress. */
export const PRINTING_CLASS = 'sprk-outcome-printing';
/** The id of the dedicated print root appended to `<body>`. */
export const PRINT_ROOT_ID = 'sprk-outcome-print-root';
/** How long after `window.print()` returns the surface is removed when `afterprint` never arrives. */
export const PRINT_FALLBACK_MS = 10000;

// One CSS rule per array entry: `insertRule` takes a single rule, so a multi-rule string would be refused by the browser.
export const PRINT_GLOBAL_CSS: readonly string[] = [
  `#${PRINT_ROOT_ID} { display: none; }`,
  `@media print {
  html.${PRINTING_CLASS}, html.${PRINTING_CLASS} body {
    height: auto !important;
    width: auto !important;
    overflow: visible !important;
  }
  html.${PRINTING_CLASS} body > *:not(#${PRINT_ROOT_ID}) { display: none !important; }
  html.${PRINTING_CLASS} body > #${PRINT_ROOT_ID} {
    display: block !important;
    position: static !important;
    height: auto !important;
    max-height: none !important;
    overflow: visible !important;
  }
}`,
];

// (Griffel makes this hook a no-op when NODE_ENV is "test", so the unit tests check the exported rule text, not the injection.)
const useGlobalPrintStyles = makeStaticStyles([...PRINT_GLOBAL_CSS]);

export interface OutcomePrint {
  /** Starts a print of the copy (a no-op while one is in progress). */
  start: () => void;
  /** The portal to render next to the report: null unless a print is in progress. */
  portal: React.ReactPortal | null;
}

/**
 * @param renderCopy the report copy to print (rendered inside a light-theme `FluentProvider`); it must not itself offer printing.
 */
export function useOutcomePrint(renderCopy: () => React.ReactNode): OutcomePrint {
  useGlobalPrintStyles();
  const [root, setRoot] = React.useState<HTMLElement | null>(null);
  const finish = React.useRef<((fromUnmount: boolean) => void) | null>(null);
  const renderRef = React.useRef(renderCopy);
  renderRef.current = renderCopy;

  const start = React.useCallback(() => {
    if (finish.current) return;
    const printRoot = document.createElement('div');
    printRoot.id = PRINT_ROOT_ID;
    document.body.appendChild(printRoot);
    let timer: ReturnType<typeof setTimeout> | undefined;
    const done = (fromUnmount: boolean): void => {
      window.removeEventListener('afterprint', onAfterPrint);
      if (timer !== undefined) clearTimeout(timer);
      document.documentElement.classList.remove(PRINTING_CLASS);
      finish.current = null;
      // On unmount React removes the portal's content itself; a state change would be to an unmounted component.
      if (!fromUnmount) flushSync(() => setRoot(null));
      printRoot.remove();
    };
    const onAfterPrint = (): void => done(false);
    finish.current = done;
    window.addEventListener('afterprint', onAfterPrint);
    flushSync(() => setRoot(printRoot));
    document.documentElement.classList.add(PRINTING_CLASS);
    try {
      window.print();
    } catch {
      // A blocked print is not a crash of the page; the clean-up below still runs.
      console.error('[ebilling] The browser did not start the print.');
    } finally {
      // Chrome blocks in print() until the dialog closes and then fires afterprint; a browser that returns early or never fires it
      // is cleaned up here, so the page is never left in print-only mode.
      timer = setTimeout(() => finish.current?.(false), PRINT_FALLBACK_MS);
    }
  }, []);

  React.useEffect(
    () => () => {
      finish.current?.(true);
    },
    []
  );

  const portal = root
    ? createPortal(<FluentProvider theme={webLightTheme}>{renderRef.current()}</FluentProvider>, root)
    : null;
  return { start, portal };
}
