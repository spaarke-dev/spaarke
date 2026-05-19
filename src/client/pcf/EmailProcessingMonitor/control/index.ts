/**
 * EmailProcessingMonitor PCF Control — Virtual Pattern (ADR-022)
 *
 * Migrated from ComponentFramework.StandardControl (imperative ReactDOM.render)
 * to ComponentFramework.ReactControl. Platform now provides React + Fluent UI
 * via <platform-library> declarations — the bundle no longer ships React.
 *
 * Per ADR-022 + /pcf-deploy skill's "async init in ReactControl" rule:
 * @spaarke/auth initialization lives inside `EmailProcessingMonitorHost` via
 * useState + useEffect — NOT in this PCF class's init(). That guarantees the
 * component re-renders when auth completes, independent of whether the
 * framework propagates notifyOutputChanged() back into updateView().
 */
import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import { EmailProcessingMonitorHost } from './EmailProcessingMonitorHost';

const VERSION = '1.1.1';

export class EmailProcessingMonitor
  implements ComponentFramework.ReactControl<IInputs, IOutputs>
{
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  private notifyOutputChanged: () => void = () => undefined;

  constructor() {
    console.log(`[EmailProcessingMonitor] Control instance created. Version: ${VERSION}`);
  }

  public init(
    _context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    this.notifyOutputChanged = notifyOutputChanged;
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(EmailProcessingMonitorHost, {
      context: context as ComponentFramework.Context<IInputs>,
      version: VERSION,
    });
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    console.log('[EmailProcessingMonitor] Destroying control...');
  }
}
