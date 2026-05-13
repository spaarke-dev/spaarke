/**
 * SpeDocumentViewer PCF Control — Virtual Pattern (ADR-022)
 *
 * Migrated from ComponentFramework.StandardControl (imperative ReactDOM.render)
 * to ComponentFramework.ReactControl. Platform now provides React + Fluent UI
 * via `<platform-library>` declarations, so the bundle no longer ships React
 * (~30 MB → expected ~1-2 MB).
 *
 * Per ADR-022 + the /pcf-deploy skill's "async init in ReactControl" rule:
 * auth initialization lives inside `SpeDocumentViewerHost` via useState +
 * useEffect — NOT in this PCF class's init(). That guarantees the component
 * re-renders when auth completes, independent of whether the framework
 * propagates notifyOutputChanged() back into updateView().
 */
import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import { v4 as uuidv4 } from 'uuid';
import { createLogger } from '@spaarke/ui-components/dist/utils/logger';
import { SpeDocumentViewerHost } from './SpeDocumentViewerHost';

const logger = createLogger('SpeDocumentViewer');

export class SpeDocumentViewer implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  private correlationId: string;
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  private notifyOutputChanged: () => void = () => undefined;

  constructor() {
    this.correlationId = uuidv4();
    logger.logInfo('SpeDocumentViewer', `Control instance created. Correlation ID: ${this.correlationId}`);
  }

  /**
   * Virtual-control init: no container parameter, no React rendering here.
   * All UI is returned from updateView(). All async work lives in the host
   * component (SpeDocumentViewerHost) via React hooks.
   */
  public init(
    _context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    this.notifyOutputChanged = notifyOutputChanged;
  }

  /**
   * Returns the React tree to render. Apply control height as the host
   * component's wrapper style by passing it through props if needed; the
   * host already fills available space (width/height 100%).
   */
  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(SpeDocumentViewerHost, {
      context: context as ComponentFramework.Context<unknown>,
      correlationId: this.correlationId,
    });
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    logger.logInfo('SpeDocumentViewer', 'Destroying control...');
  }
}
