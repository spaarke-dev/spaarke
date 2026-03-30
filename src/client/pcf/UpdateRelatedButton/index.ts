/**
 * UpdateRelatedButton PCF Control
 *
 * Triggers update of related records based on configured field mappings.
 * Uses the BFF API to execute field mapping rules.
 *
 * @remarks
 * - Uses React 16 APIs per ADR-022 (ReactDOM.render, not createRoot)
 * - Uses Fluent UI v9 per ADR-021 (via platform libraries)
 */

import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import * as ReactDOM from 'react-dom';
import { FluentProvider } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components';
import { UpdateRelatedButtonApp, IUpdateRelatedButtonAppProps } from './UpdateRelatedButtonApp';

export class UpdateRelatedButton implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private notifyOutputChanged: () => void;
  private context: ComponentFramework.Context<IInputs>;

  constructor() {
    // Empty constructor
  }

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.container = container;
    this.notifyOutputChanged = notifyOutputChanged;
    this.context = context;

    this.renderControl();
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;
    this.renderControl();
  }

  private renderControl(): void {
    const theme = resolveThemeWithUserPreference(this.context);

    const props: IUpdateRelatedButtonAppProps = {
      buttonLabel: this.context.parameters.buttonLabel?.raw || 'Update Related Records',
      sourceEntityId: this.context.parameters.sourceEntityId?.raw || '',
      sourceEntityType: this.context.parameters.sourceEntityType?.raw || '',
      mappingProfileId: this.context.parameters.mappingProfileId?.raw || undefined,
      apiBaseUrl: this.context.parameters.apiBaseUrl?.raw || '',
      webApi: this.context.webAPI,
      onUpdateComplete: this.handleUpdateComplete.bind(this),
    };

    // React 16 API per ADR-022 - use ReactDOM.render, NOT createRoot
    ReactDOM.render(
      React.createElement(
        FluentProvider,
        { theme, style: { height: '100%', width: '100%' } },
        React.createElement(UpdateRelatedButtonApp, props)
      ),
      this.container
    );
  }

  private handleUpdateComplete(success: boolean, message: string): void {
    // Could trigger notification or refresh
    if (success) {
      console.log('UpdateRelatedButton: Update completed successfully', message);
    } else {
      console.error('UpdateRelatedButton: Update failed', message);
    }
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    // React 16 API per ADR-022 - use unmountComponentAtNode, NOT root.unmount()
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
