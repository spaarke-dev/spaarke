/**
 * CommunicationMessageActions PCF Control — Virtual Pattern (ADR-022)
 *
 * Message send/respond accessory on the OOB `sprk_communication` / `sprk_communicationthread`
 * forms: Compose / Send / Respond (reply into the current thread). Hosts the shared
 * `<TimelineComposeBox/>` (task 060) for the compose surface and calls the existing
 * `/api/communications/send` (task 051, extended by task 062 with `communicationType` /
 * `threadId` / `inReplyToMessageId`) via `@spaarke/auth` `authenticatedFetch` (ADR-028).
 *
 * §11 decision (task 062): a NEW control, not an extension of `CommunicationActions` —
 * see `CommunicationMessageActionsApp.tsx` file header for the three-question justification.
 *
 * Per ADR-022: virtual control, React 16 + Fluent v9 as platform libraries,
 * `updateView()` returns a React element. No FCC swap, no Code Page host (ADR-026 Path-A).
 */

import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { CommunicationMessageActionsHost } from './CommunicationMessageActionsHost';

const CONTROL_VERSION = '1.0.0';

export class CommunicationMessageActions implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  public init(
    _context: ComponentFramework.Context<IInputs>,
    _notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    /* no-op — actions run via authenticatedFetch inside the React app */
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(CommunicationMessageActionsHost, {
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
