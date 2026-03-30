/**
 * Drill-Through Workspace PCF Control
 *
 * Expanded workspace for visualization drill-through:
 * - Two-panel layout: Chart (1/3) | Dataset Grid (2/3)
 * - Click chart elements to filter dataset grid
 * - Supports all 7 visual types from Visual Host
 * - Opens as dialog from Visual Host expand button
 *
 * ADR Compliance:
 * - ADR-006: PCF over webresources
 * - ADR-011: Dataset PCF over Subgrids
 * - ADR-021: Fluent UI v9 Design System
 *
 * @version 1.0.0
 */

import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import * as ReactDOM from 'react-dom';
import { FluentProvider, webLightTheme, Theme } from '@fluentui/react-components';
import {
  resolveThemeWithUserPreference,
  setupThemeListener,
} from '@spaarke/ui-components';
import { DrillThroughWorkspaceApp } from './components/DrillThroughWorkspaceApp';
import { logger } from './utils/logger';

// ─────────────────────────────────────────────────────────────────────────────
// PCF Control Class
// ─────────────────────────────────────────────────────────────────────────────

export class DrillThroughWorkspace implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private _container: HTMLDivElement;
  private _context: ComponentFramework.Context<IInputs>;
  private _notifyOutputChanged: () => void;
  private _isRendered = false;

  // Output values
  private _shouldClose = false;
  private _selectedRecordIds = '';

  // Theme state
  private _currentTheme: Theme = webLightTheme;
  private _cleanupThemeListener?: () => void;

  constructor() {
    logger.info('DrillThroughWorkspace', 'Constructor called');
  }

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    logger.info('DrillThroughWorkspace', 'init() called');

    this._context = context;
    this._container = container;
    this._notifyOutputChanged = notifyOutputChanged;

    // Enable container resize tracking for responsive sizing
    context.mode.trackContainerResize(true);

    // Set container base styles
    this._container.style.display = 'flex';
    this._container.style.flexDirection = 'column';
    this._container.style.boxSizing = 'border-box';
    this._container.style.overflow = 'hidden';

    // Set up theme using shared library (no OS prefers-color-scheme — ADR-021)
    this._currentTheme = resolveThemeWithUserPreference(context);
    this.setupThemeListeners();

    // Set up escape key handler for closing
    this.setupKeyboardHandler();

    // Initial render
    this.renderComponent();
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    logger.info('DrillThroughWorkspace', 'updateView() called');
    this._context = context;
    this.renderComponent();
  }

  public getOutputs(): IOutputs {
    return {
      shouldClose: this._shouldClose,
      selectedRecordIds: this._selectedRecordIds,
    };
  }

  public destroy(): void {
    logger.info('DrillThroughWorkspace', 'destroy() called');
    this._cleanupThemeListener?.();
    this.cleanupKeyboardHandler();
    if (this._isRendered) {
      ReactDOM.unmountComponentAtNode(this._container);
      this._isRendered = false;
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Private Methods
  // ─────────────────────────────────────────────────────────────────────────

  private renderComponent(): void {
    // Get allocated dimensions from Custom Page (responsive sizing)
    const allocatedWidth = this._context.mode.allocatedWidth;
    const allocatedHeight = this._context.mode.allocatedHeight;

    // Apply dimensions to container
    if (allocatedWidth > 0) {
      this._container.style.width = `${allocatedWidth}px`;
    } else {
      this._container.style.width = '100%';
    }

    if (allocatedHeight > 0) {
      this._container.style.height = `${allocatedHeight}px`;
    } else {
      this._container.style.height = '100%';
    }

    logger.info('DrillThroughWorkspace', `Container size: ${allocatedWidth}x${allocatedHeight}`);

    // Get input parameters
    const chartDefinitionId = this._context.parameters.chartDefinitionId?.raw || '';

    // Get dataset from platform (Dataset PCF pattern per ADR-011)
    const dataset = this._context.parameters.dataset;

    logger.info(
      'DrillThroughWorkspace',
      `Rendering with chartDefinitionId: ${chartDefinitionId}, dataset records: ${
        dataset?.sortedRecordIds?.length ?? 0
      }`
    );

    // Render React component (React 16 pattern per ADR-022)
    ReactDOM.render(
      React.createElement(
        FluentProvider,
        { theme: this._currentTheme },
        React.createElement(DrillThroughWorkspaceApp, {
          chartDefinitionId,
          dataset,
          webApi: this._context.webAPI,
          onRecordSelect: this.handleRecordSelect.bind(this),
          onClose: this.handleClose.bind(this),
        })
      ),
      this._container
    );
    this._isRendered = true;
  }

  private handleRecordSelect(recordIds: string[]): void {
    this._selectedRecordIds = recordIds.join(',');
    this._notifyOutputChanged();
  }

  private handleClose(): void {
    this._shouldClose = true;
    this._notifyOutputChanged();
    this.closeDialog();
  }

  private closeDialog(): void {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window as any).Xrm;

      // Try navigateBack for custom pages opened as dialogs
      if (xrm?.Navigation?.navigateBack) {
        xrm.Navigation.navigateBack();
        return;
      }

      // Try closing the form/dialog
      if (xrm?.Page?.ui?.close) {
        xrm.Page.ui.close();
        return;
      }

      // Fallback to window methods
      this.tryWindowClose();
    } catch (err) {
      logger.error('DrillThroughWorkspace', 'closeDialog error, trying fallback', err);
      this.tryWindowClose();
    }
  }

  private tryWindowClose(): void {
    try {
      // Try history.back() first
      if (window.history.length > 1) {
        window.history.back();
        return;
      }

      // For dialogs in iframe, message the parent
      if (window.parent !== window) {
        window.parent.postMessage({ type: 'DRILLTHROUGH_WORKSPACE_CLOSE' }, '*');
      }

      // Try window.close as last resort
      window.close();
    } catch {
      logger.error('DrillThroughWorkspace', 'Failed to close dialog');
    }
  }

  private _keyboardHandler = (e: KeyboardEvent) => {
    if (e.key === 'Escape') {
      this.handleClose();
    }
  };

  private setupKeyboardHandler(): void {
    window.addEventListener('keydown', this._keyboardHandler);
  }

  private cleanupKeyboardHandler(): void {
    window.removeEventListener('keydown', this._keyboardHandler);
  }

  private setupThemeListeners(): void {
    // Use shared library listener — listens for localStorage + custom events.
    // OS prefers-color-scheme is intentionally NOT listened to (ADR-021).
    this._cleanupThemeListener = setupThemeListener((isDark: boolean) => {
      this._currentTheme = resolveThemeWithUserPreference(this._context);
      this.renderComponent();
    }, this._context);
  }
}
