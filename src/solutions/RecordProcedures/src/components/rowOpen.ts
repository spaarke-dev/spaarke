/**
 * What makes a table row open its wizard (ebilling-r1 task 084b): a click, or Enter or Space while the row has focus. The row is a tab stop
 * only while it can open something. Nothing here calls the BFF; the handler the list is given decides what happens.
 */

import type * as React from 'react';

export function openProps(enabled: unknown, open: () => void): React.HTMLAttributes<HTMLElement> {
  if (!enabled) return {};
  return {
    tabIndex: 0,
    onClick: open,
    onKeyDown: e => {
      // A key pressed inside a control of the row is that control's; only the row's own Enter / Space opens it.
      if (e.target !== e.currentTarget) return;
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        open();
      }
    },
  };
}
