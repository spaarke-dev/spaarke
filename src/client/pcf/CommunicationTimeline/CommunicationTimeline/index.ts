/**
 * CommunicationTimeline PCF Control — Virtual Pattern (ADR-022)
 *
 * Form-bound viewer for the shared `<CommunicationTimeline/>` polling
 * message timeline (task 060) on the OOB `sprk_communication` /
 * `sprk_communicationthread` forms. Renders the thread's persisted
 * messages (email + chat interleaved), an unread indicator, and the
 * compose/send box — all internal to the shared component — polling the
 * BFF on a ~5s interval via `@spaarke/auth` `authenticatedFetch` (ADR-028).
 *
 * Per ADR-022: virtual control, React 16 + Fluent v9 as platform libraries,
 * `updateView()` returns a React element. No FCC swap, no Code Page host
 * (ADR-026 Path-A exception; mirrors `CommunicationMessageActions`, task 062).
 */

import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { CommunicationTimelineHost } from './CommunicationTimelineHost';

const CONTROL_VERSION = '1.0.1';

export class CommunicationTimeline implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  public init(
    _context: ComponentFramework.Context<IInputs>,
    _notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    /* no-op — data flows via authenticatedFetch/polling inside the React app */
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(CommunicationTimelineHost, {
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
