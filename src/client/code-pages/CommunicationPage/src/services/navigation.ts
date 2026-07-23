/**
 * Builds the shell's `onSent` / `onClose` navigation callbacks (reference §7.6).
 * These are passed as plain function props into whichever renderer the layout
 * switch mounts (task 041 `<EmailComposer />` consumes them on send/cancel).
 */

import type { ICommunicationNavCallbacks } from '../types/communication';
import { resolveXrm } from './xrm';

export function buildNavCallbacks(): ICommunicationNavCallbacks {
  return {
    // On successful send: open the created/updated sprk_communication record.
    onSent: (communicationId: string) => {
      const xrm = resolveXrm();
      if (xrm?.Navigation?.openForm) {
        xrm.Navigation.openForm({ entityName: 'sprk_communication', entityId: communicationId });
      } else {
        // Outside a Dataverse host (e.g. dev) — best effort.
        window.close();
      }
    },
    // On cancel/close: leave the page.
    onClose: () => {
      window.close();
    },
  };
}
