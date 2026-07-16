/**
 * CommunicationActions PCF Control — Virtual Pattern (ADR-022)
 *
 * Communication action bar on the OOB `sprk_communication` form: Reply / Forward /
 * Send / Save Draft / Save to SharePoint. Hosts the shared `<SendEmailPage/>` for
 * compose/reply/forward and calls the existing `/api/communications/send` +
 * `/{id}/archive` via `@spaarke/auth` `authenticatedFetch` (ADR-028). Replaces the
 * ribbon `sprk_communication_send.js` (W4 pivot, task 044).
 *
 * Per ADR-022: virtual control, React 16 + Fluent v9 as platform libraries,
 * `updateView()` returns a React element.
 */

import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { CommunicationActionsHost } from './CommunicationActionsHost';

const CONTROL_VERSION = '1.0.1';

export class CommunicationActions implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  public init(
    _context: ComponentFramework.Context<IInputs>,
    _notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    /* no-op — actions run via authenticatedFetch inside the React app */
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(CommunicationActionsHost, {
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
