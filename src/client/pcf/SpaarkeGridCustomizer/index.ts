/**
 * SpaarkeGridCustomizer - General-purpose Power Apps grid control customizer
 *
 * Set as a grid's "Customizer control" (`GridCustomizerControlFullName` =
 * `sprk_Spaarke.Controls.SpaarkeGridCustomizer`). The grid instantiates it with an `EventName`;
 * `init` answers by firing that event with the customizer (Microsoft Learn, "Customize the editable
 * grid control"). The overrides are built in customizers/GridCustomizer.ts:
 *  - Filing-column lock (unified-access-control-r2 task 168; owner round 25 item 8, widened by owner
 *    round 38): the regarding filing columns (the four core-ancestor roots, the ADR-024 pair and the
 *    non-root sprk_regarding* lookups, from config/regarding-filing-columns.json) are not editable
 *    (editor cancelled, cell shown read-only). Set on the sprk_event and sprk_analysis home grids by
 *    scripts/Set-SpaarkeGridCustomizerOnChildGrids.ps1.
 *  - Regarding links: regarding name / id cells link to the parent record when the row names it.
 *
 * ADR Compliance:
 * - ADR-006: a PCF (no web-resource grid handler)
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs (platform-provided React)
 *
 * @version 1.1.1
 */

import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { CUSTOMIZER_VERSION, createGridCustomizer } from './customizers/GridCustomizer';

interface FactoryWithFireEvent {
  fireEvent?: (eventName: string, payload: unknown) => void;
}

/**
 * SpaarkeGridCustomizer - the customizer control the Power Apps grid instantiates.
 */
export class SpaarkeGridCustomizer implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  public init(
    context: ComponentFramework.Context<IInputs>,
    _notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    const eventName = context.parameters.EventName?.raw;
    if (!eventName) {
      console.warn(`[SpaarkeGridCustomizer v${CUSTOMIZER_VERSION}] no EventName: not running as a grid customizer`);
      return;
    }
    const factory = (context as unknown as { factory?: FactoryWithFireEvent }).factory;
    if (!factory || typeof factory.fireEvent !== 'function') {
      console.error(`[SpaarkeGridCustomizer v${CUSTOMIZER_VERSION}] context.factory.fireEvent is unavailable`);
      return;
    }
    factory.fireEvent(eventName, createGridCustomizer());
    console.log(`[SpaarkeGridCustomizer v${CUSTOMIZER_VERSION}] customizer registered (filing columns not editable)`);
  }

  public updateView(_context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(React.Fragment);
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    // Nothing to release.
  }
}
