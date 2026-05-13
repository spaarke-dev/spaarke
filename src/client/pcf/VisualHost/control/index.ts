/**
 * Visual Host PCF Control — Virtual Pattern (ADR-022)
 *
 * Migrated from ComponentFramework.StandardControl (imperative ReactDOM.render)
 * to ComponentFramework.ReactControl. Platform provides React + Fluent UI via
 * <platform-library> declarations — the bundle no longer ships React.
 */

import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { VisualHostHost } from './VisualHostHost';
import { logger } from './utils/logger';

export class VisualHost
  implements ComponentFramework.ReactControl<IInputs, IOutputs>
{
  private notifyOutputChanged: () => void = () => undefined;

  constructor() {
    logger.info('VisualHost', 'Constructor called (virtual)');
  }

  public init(
    _context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    this.notifyOutputChanged = notifyOutputChanged;
    logger.info('VisualHost', 'Init complete');
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(VisualHostHost, {
      context,
      notifyOutputChanged: this.notifyOutputChanged,
    });
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    logger.info('VisualHost', 'Destroy');
  }
}
