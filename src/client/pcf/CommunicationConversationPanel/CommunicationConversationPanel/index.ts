/**
 * CommunicationConversationPanel PCF Control — Virtual Pattern (ADR-022)
 *
 * Surface 1 of R3 (FR-13): a form-bound record right-pane conversation panel on
 * the 11 ADR-024 regarding-family entity forms. Resolves the host record's
 * `(entityType, id)` from the Xrm page context and renders a COMPACT PREVIEW of
 * that record's conversations (max 3 threads, last 5 communications, default
 * thread auto-expanded) with a footer "N of M" counter and per-message
 * `<MessageQuickView/>`. An open/expand affordance launches the shared
 * `<ConversationWorkspace/>` two-pane widget as a record-FILTERED modal
 * (regarding = the host record) that wires `<ConversationView/>` through the
 * shell's `renderConversation` seam — the shared widget owns thread navigation
 * once open (the user never returns to the PCF to switch threads).
 *
 * Mirrors the sibling `CommunicationTimelineRegarding` host-wrapper pattern
 * (manifest, this lifecycle, host-context/regarding extraction, `@spaarke/auth`
 * bootstrap, FluentProvider ownership) but presents the preview + modal launch,
 * NOT the vertical timeline. Reuses the shared conversation widget as-is — no
 * reimplemented bubbles / thread-list / quick-view (NFR-06).
 *
 * Per ADR-022: virtual control, React 16 + Fluent v9 as platform libraries,
 * `updateView()` returns a React element. No FCC swap, no Code Page host
 * (ADR-026 Path-A exception; same as R2's CommunicationTimeline* PCFs — cited
 * in the deploy PR, task 034). Task 030, FR-13.
 */

import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { CommunicationConversationPanelHost } from './CommunicationConversationPanelHost';

const CONTROL_VERSION = '1.10.0';

export class CommunicationConversationPanel implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  public init(
    _context: ComponentFramework.Context<IInputs>,
    _notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    /* no-op — data flows via authenticatedFetch/polling inside the React app */
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(CommunicationConversationPanelHost, {
      context,
      version: CONTROL_VERSION,
    });
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    /* no-op for virtual controls */
  }
}
