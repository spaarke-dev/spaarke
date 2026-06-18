/**
 * RegardingResolver PCF Control — Virtual Pattern (ADR-022)
 *
 * Reusable polymorphic regarding picker for any host entity that follows the
 * ADR-024 resolver pattern (sprk_todo, sprk_communication today; any future
 * entity tomorrow per FR-22). Bound to the host entity's hidden
 * `sprk_regardingrecordtype` lookup; writes the four side-effect fields
 * (sprk_regardingrecordid / name / url + entity-specific lookup) via
 * `Xrm.WebApi.updateRecord` through `PolymorphicResolverService.applyResolverFields`.
 *
 * Modeled on `AssociationResolver` v1.1.0 (the existence proof — same shape;
 * already deployed in spaarkedev1 as `Spaarke.Controls.AssociationResolver`).
 *
 * Per ADR-022:
 *   - virtual control (`control-type="virtual"` in manifest)
 *   - React 16 + Fluent v9 supplied as platform libraries (NOT bundled)
 *   - updateView() returns a React element instead of rendering imperatively
 *
 * Per ADR-024 / FR-21:
 *   - mutual-exclusivity logic lives entirely inside the shared service; the
 *     PCF NEVER reimplements the field-write logic
 *
 * Per FR-22:
 *   - `entity` is a manifest input property (`sprk_todo` | `sprk_communication`
 *     | future). NO entity-specific code branches anywhere in this PCF.
 */

import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { RegardingResolverHost } from './RegardingResolverHost';

const CONTROL_VERSION = '1.1.0';

export class RegardingResolver implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  private notifyOutputChanged: () => void = () => undefined;
  /**
   * Cached output for the bound `regardingRecordType` lookup. Mutated by the
   * React app via the onRecordTypeChanged callback so `getOutputs()` returns
   * the latest value when notifyOutputChanged() fires.
   */
  private _regardingRecordType: ComponentFramework.LookupValue[] | undefined;

  constructor() {
    this.handleRecordTypeChanged = this.handleRecordTypeChanged.bind(this);
  }

  public init(
    _context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    this.notifyOutputChanged = notifyOutputChanged;
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(RegardingResolverHost, {
      context,
      onRecordTypeChanged: this.handleRecordTypeChanged,
      version: CONTROL_VERSION,
    });
  }

  public getOutputs(): IOutputs {
    return {
      regardingRecordType: this._regardingRecordType,
    };
  }

  public destroy(): void {
    /* no-op for virtual controls */
  }

  /**
   * Callback invoked by the React app when the user selects (or clears) a
   * regarding target. Mutates the private cache and notifies the framework so
   * the bound `sprk_regardingrecordtype` lookup is updated on the form. The
   * other four resolver fields are written by `applyResolverFields` via
   * `Xrm.WebApi.updateRecord` as a side effect inside the React app.
   *
   * Passing `null` clears the bound lookup.
   */
  private handleRecordTypeChanged(value: ComponentFramework.LookupValue | null): void {
    this._regardingRecordType = value ? [value] : undefined;
    this.notifyOutputChanged();
  }
}
