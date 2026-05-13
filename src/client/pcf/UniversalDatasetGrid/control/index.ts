/**
 * Universal Dataset Grid PCF Control — Virtual Pattern (ADR-022)
 *
 * Migrated from ComponentFramework.StandardControl (imperative ReactDOM.render)
 * to ComponentFramework.ReactControl. Platform now provides React + Fluent UI
 * via `<platform-library>` declarations — the bundle no longer ships React.
 *
 * Per ADR-022 + the /pcf-deploy skill's "async init in ReactControl" rule:
 * @spaarke/auth initialization lives inside `UniversalDatasetGridHost` via
 * useState + useEffect — NOT in this PCF class's init(). That guarantees the
 * component re-renders when auth completes, independent of whether the
 * framework propagates notifyOutputChanged() back into updateView().
 *
 * Output handling: the row-click handler captures the selected date in the
 * PCF class so getOutputs() can return it. The React component invokes the
 * handler via the `onRowClick` prop; the handler then calls
 * notifyOutputChanged() so Power Apps picks up the new value.
 */
import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { UniversalDatasetGridHost } from './UniversalDatasetGridHost';
import { logger } from './utils/logger';

export class UniversalDatasetGrid
  implements ComponentFramework.ReactControl<IInputs, IOutputs>
{
  private notifyOutputChanged: () => void = () => undefined;
  /** Selected event date from row click (Task 012 — bi-directional calendar sync). */
  private _selectedEventDate: string | null = null;

  constructor() {
    logger.info('Control', 'Constructor called');
    this.handleRowClick = this.handleRowClick.bind(this);
  }

  public init(
    _context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    this.notifyOutputChanged = notifyOutputChanged;
    logger.info('Control', 'Init complete (virtual)');
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(UniversalDatasetGridHost, {
      context,
      notifyOutputChanged: this.notifyOutputChanged,
      onRowClick: this.handleRowClick,
    });
  }

  public getOutputs(): IOutputs {
    return {
      selectedEventDate: this._selectedEventDate ?? undefined,
    };
  }

  public destroy(): void {
    logger.info('Control', 'Destroy');
  }

  /**
   * Captures the selected row date and notifies Power Apps of the new output.
   * Bound in constructor — passed to the React host via props.
   */
  private handleRowClick(date: string | null): void {
    logger.info('Control', `Row clicked - date: ${date}`);
    this._selectedEventDate = date;
    this.notifyOutputChanged();
  }
}
