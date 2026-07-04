import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import { MatterHeaderHost } from './MatterHeaderHost';

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

    // v1.0.2 manifest inputs. `title` overrides the default "Matter" label; when blank we
    // let the view fall back. `showVersion` is a TwoOptions field: PCF surfaces it as
    // boolean | undefined so we default to true (footer visible) to preserve diagnostic
    // value unless the maker explicitly toggles it off.
    const title = context.parameters.title?.raw ?? undefined;
    const showVersion = context.parameters.showVersion?.raw !== false;

    // v1.0.11 — wrap in `MatterHeaderHost` which mounts a `<FluentProvider>` so
    // Fluent v9 CSS variables reach the portal-rendered `AiSummaryPopover`.
    // v1.0.12 (DEF-09) — pass `context` so the host's theme resolver can read
    // `context.fluentDesignLanguage.isDarkTheme` (Power Apps native dark-mode
    // signal). Adds live-listening for `spaarke-theme-change` events + the
    // `(forced-colors: active)` media query — dynamic light/dark/HC support.
    return React.createElement(MatterHeaderHost, { recordId, title, showVersion, context });
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    // No cleanup required (no listeners, no timers, no auth handles).
  }
}
