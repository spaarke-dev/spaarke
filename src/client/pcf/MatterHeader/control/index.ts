import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import { MatterHeaderView } from './MatterHeaderView';

/**
 * MatterHeader PCF Control
 *
 * Compact 5-field summary card + 3-action toolbar for Matter records.
 * Part of the record-header-and-notepad-r1 shared primitive set.
 *
 * Follows:
 * - ADR-006: PCF for all custom UI
 * - ADR-012: Shared component library (composition of @spaarke/ui-components primitives)
 * - ADR-021: Fluent UI v9 semantic tokens (auto-theming via platform-library modern theming)
 * - ADR-022: React 16 APIs with platform-library React + Fluent v9 (control-type="virtual")
 *
 * All Dataverse I/O uses Xrm.WebApi via shared hooks (no BFF, no @spaarke/auth).
 */
export class MatterHeader implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  public init(
    _context: ComponentFramework.Context<IInputs>,
    _notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    // No async init; auth is not used (host-context Xrm.WebApi only).
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    // Read recordId from context.mode.contextInfo.entityId (the current form's record).
    // The bound field (boundField) is manifest-required so Dataverse shows the PCF in the
    // form designers "Add component" gallery, but the field value itself is not used.
    // `context.mode.contextInfo` exists at runtime but is not in the current
    // @types/powerapps-component-framework. Type-cast pattern mirrors ScopeConfigEditor +
    // SearchIndexResolver (task-024 build repair per task-023 gap).
    const contextInfo = (context.mode as unknown as { contextInfo?: { entityId?: string } }).contextInfo;
    const recordId = contextInfo?.entityId || '';
    // Platform-library Fluent v9 auto-applies host theme (control-type="virtual").
    // No manual FluentProvider wrap needed per fluent-v9-modern-theming pattern (approach 1).
    return React.createElement(MatterHeaderView, { recordId });
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    // No cleanup required (no listeners, no timers, no auth handles).
  }
}
