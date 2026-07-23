/**
 * CommunicationTimelineRegarding PCF Control — Virtual Pattern (ADR-022)
 *
 * Form-bound viewer mounting the shared `<CommunicationTimeline/>` component
 * in **regarding mode** (task 020) on the 11 ADR-024 regarding-family entity
 * forms. Resolves the host record's `(entityType, id)` from the Xrm page
 * context and renders that record's threads as collapsible groups, polling
 * the BFF `by-regarding` endpoint (task 010) via `@spaarke/auth`
 * `authenticatedFetch` (ADR-028).
 *
 * Per ADR-022: virtual control, React 16 + Fluent v9 as platform libraries,
 * `updateView()` returns a React element. No FCC swap, no Code Page host
 * (ADR-026 Path-A exception; mirrors the sibling `CommunicationTimeline` PCF,
 * task 061). Task 021, FR-04.
 */

import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { CommunicationTimelineRegardingHost } from './CommunicationTimelineRegardingHost';

const CONTROL_VERSION = '1.0.0';

export class CommunicationTimelineRegarding implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  public init(
    _context: ComponentFramework.Context<IInputs>,
    _notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    /* no-op — data flows via authenticatedFetch/polling inside the React app */
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(CommunicationTimelineRegardingHost, {
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
