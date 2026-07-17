/**
 * CommunicationConnections PCF Control — Virtual Pattern (ADR-022)
 *
 * Multi-association review surface for `sprk_communication`. Renders the
 * task-015 association provenance (`sprk_associationprovenance`) as typed
 * connection slots — per-slot confidence + confirm/change, Accept-all,
 * Link-another, create-from-email — and writes the chosen regarding via the
 * shared `PolymorphicResolverService.applyResolverFields` (ADR-024). Hosted in
 * the OOB `sprk_communication` form's right accessories column (W4 pivot).
 *
 * Per ADR-022:
 *   - virtual control (`control-type="virtual"` in manifest)
 *   - React 16 + Fluent v9 supplied as platform libraries (NOT bundled)
 *   - updateView() returns a React element instead of rendering imperatively
 *
 * Bound properties are READ-ONLY from the PCF's perspective: the underlying
 * columns are written via `context.webAPI.updateRecord` through the shared
 * service, NOT via PCF outputs. `getOutputs()` echoes the framework's last
 * values so we never clobber them with `undefined`.
 */

import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { CommunicationConnectionsHost } from './CommunicationConnectionsHost';

const CONTROL_VERSION = '1.1.2';

export class CommunicationConnections implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  private _associationProvenance: string | undefined;
  private _associationStatus: number | undefined;

  public init(
    _context: ComponentFramework.Context<IInputs>,
    _notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    /* no-op — writes happen via context.webAPI inside the React app */
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    // Echo bound values on every updateView so getOutputs() never clobbers them.
    const provenanceRaw = context.parameters.associationProvenance?.raw;
    this._associationProvenance = typeof provenanceRaw === 'string' ? provenanceRaw : undefined;
    const statusRaw = context.parameters.associationStatus?.raw;
    this._associationStatus = typeof statusRaw === 'number' ? statusRaw : undefined;

    return React.createElement(CommunicationConnectionsHost, {
      context,
      version: CONTROL_VERSION,
    });
  }

  public getOutputs(): IOutputs {
    return {
      associationProvenance: this._associationProvenance,
      associationStatus: this._associationStatus,
    };
  }

  public destroy(): void {
    /* no-op for virtual controls */
  }
}
