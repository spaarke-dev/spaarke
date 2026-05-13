/**
 * AssociationResolver PCF Control — Virtual Pattern (ADR-022)
 *
 * Migrated from ComponentFramework.StandardControl to ReactControl. Platform
 * provides React + Fluent UI via <platform-library> declarations.
 *
 * Output handling: the React component invokes onRecordSelected which mutates
 * private class fields (so getOutputs() returns the latest values) and calls
 * notifyOutputChanged() so Power Apps picks up the new values.
 */

import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { AssociationResolverHost } from './AssociationResolverHost';

const CONTROL_VERSION = '1.1.0';

export class AssociationResolver
  implements ComponentFramework.ReactControl<IInputs, IOutputs>
{
  private notifyOutputChanged: () => void = () => undefined;
  private _regardingRecordId = '';
  private _regardingRecordName = '';

  constructor() {
    this.handleRecordSelected = this.handleRecordSelected.bind(this);
  }

  public init(
    _context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    this.notifyOutputChanged = notifyOutputChanged;
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(AssociationResolverHost, {
      context,
      onRecordSelected: this.handleRecordSelected,
      version: CONTROL_VERSION,
    });
  }

  public getOutputs(): IOutputs {
    return {
      regardingRecordId: this._regardingRecordId,
      regardingRecordName: this._regardingRecordName,
    };
  }

  public destroy(): void {
    /* no-op for virtual controls */
  }

  private handleRecordSelected(recordId: string, recordName: string): void {
    this._regardingRecordId = recordId;
    this._regardingRecordName = recordName;
    this.notifyOutputChanged();
  }
}
