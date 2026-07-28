/**
 * TrackingFieldTrio PCF Control
 *
 * Compact three-field editor: Monitor (toggle) + High Priority (toggle) +
 * Access Permission (segmented 3-value picker). Designed to fit inside a 33%
 * form column where the standard Dataverse field controls waste too much
 * horizontal space.
 *
 * v1.0.1 — added showTitle / showVersion PCF properties, alignment fix
 * (explicit 2-row grid), pale color scheme, option-set color binding.
 *
 * v1.0.6 (task 023, FR-14/FR-18) — the rendered component now lives in
 * `@spaarke/ui-components` (entity-agnostic `TrackingFieldTrio`; options
 * injected via props). This `index.ts` is the ONLY place in the tree that
 * knows about `sprk_communication`'s Access Permission choice values —
 * `getAccessPermissionOptions()` supplies the real Dataverse OptionSet
 * metadata (value + label + color) when available, falling back to the
 * hardcoded Standard/Limited/Restricted triple (no color — the shared
 * core's position-based default palette applies) when metadata is
 * unavailable (e.g., harness/test environments), preserving the PRE-LIFT
 * behavior (NFR-04 — zero regression).
 *
 * @remarks
 * - Uses React 16 APIs per ADR-022 (ReactDOM.render, not createRoot)
 * - Uses Fluent UI v9 per ADR-021 (via platform libraries)
 */

import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import * as ReactDOM from 'react-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
// Aliased on import — the PCF control class below MUST be named
// `TrackingFieldTrio` to match `constructor="TrackingFieldTrio"` in
// ControlManifest.Input.xml, so the shared component is imported under a
// distinct local name.
import {
  TrackingFieldTrio as SharedTrackingFieldTrio,
  type ITrackingFieldTrioProps,
  type IAccessPermissionOption,
} from '@spaarke/ui-components/dist/components/TrackingFieldTrio';

// sprk_communication Access Permission choice values — MUST match the
// Dataverse OptionSet values. Entity-specific: lives ONLY here (the PCF
// caller), never in the shared `TrackingFieldTrio` core (FR-14).
const ACCESS_PERMISSION_STANDARD = 100000000;
const ACCESS_PERMISSION_LIMITED = 100000001;
const ACCESS_PERMISSION_RESTRICTED = 100000002;

// Fallback segments (no per-option color) used when the bound OptionSet's
// field metadata isn't available (e.g., harness/test environments). The
// shared core's position-based default palette (green/yellow/red, by index)
// then supplies the same colors the pre-lift hardcoded fallback did.
const FALLBACK_ACCESS_PERMISSION_OPTIONS: IAccessPermissionOption[] = [
  { value: ACCESS_PERMISSION_STANDARD, label: 'Standard' },
  { value: ACCESS_PERMISSION_LIMITED, label: 'Limited' },
  { value: ACCESS_PERMISSION_RESTRICTED, label: 'Restricted' },
];

export class TrackingFieldTrio implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private notifyOutputChanged: () => void;
  private context: ComponentFramework.Context<IInputs>;

  // Local state that mirrors the bound fields. We keep them here so the
  // control can render immediately when the user clicks a segment/toggle,
  // then flush the change via notifyOutputChanged() → getOutputs().
  private monitorValue = false;
  private highPriorityValue = false;
  private accessPermissionValue: number | null = null;

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.container = container;
    this.notifyOutputChanged = notifyOutputChanged;
    this.context = context;

    this.monitorValue = context.parameters.monitor?.raw ?? false;
    this.highPriorityValue = context.parameters.highPriority?.raw ?? false;
    this.accessPermissionValue = context.parameters.accessPermission?.raw ?? null;

    this.renderControl();
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Framework-driven update (e.g., form refresh, another script wrote to
    // the field). Sync local state to the framework's raw values.
    this.monitorValue = context.parameters.monitor?.raw ?? false;
    this.highPriorityValue = context.parameters.highPriority?.raw ?? false;
    this.accessPermissionValue = context.parameters.accessPermission?.raw ?? null;

    this.renderControl();
  }

  /**
   * Extract per-option value/label/color from the bound OptionSet's field
   * metadata so the segmented picker can honor the choice column's real
   * Dataverse values, labels, and colors. Falls back to the hardcoded
   * Standard/Limited/Restricted triple (no color) when metadata isn't
   * available, so the control always renders 3 segments (NFR-04).
   */
  private getAccessPermissionOptions(): IAccessPermissionOption[] {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const attrs = (this.context.parameters.accessPermission as any)?.attributes;
    const options = attrs?.Options as { Value: number; Label: string; Color?: string }[] | undefined;
    if (!options || options.length === 0) return FALLBACK_ACCESS_PERMISSION_OPTIONS;
    return options.map(o => ({
      value: o.Value,
      label: o.Label,
      color: o.Color,
    }));
  }

  /**
   * Resolve a bound field's Dataverse display name from the PCF context.
   * Falls back to the provided default when metadata isn't available (e.g.,
   * harness/test environments).
   */
  private getFieldLabel(param: ComponentFramework.PropertyTypes.Property | undefined, fallback: string): string {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const displayName = (param as any)?.attributes?.DisplayName as string | undefined;
    return displayName || fallback;
  }

  private renderControl(): void {
    // `!== false` treats unset / null / undefined as "not explicitly false"
    // so the manifest default (showTitle=true) wins. Same pattern as
    // VisualHost's showToolbar/showVersion reads.
    const showTitle = this.context.parameters.showTitle?.raw !== false;
    const showVersion = this.context.parameters.showVersion?.raw === true;

    const props: ITrackingFieldTrioProps = {
      monitor: this.monitorValue,
      highPriority: this.highPriorityValue,
      accessPermission: this.accessPermissionValue,
      showTitle,
      showVersion,
      versionText: 'v1.0.6 • Built 2026-07-28',
      accessPermissionOptions: this.getAccessPermissionOptions(),
      // Labels pulled from each bound field's Dataverse metadata so they
      // reflect the actual field display name (localizable, and stays in
      // sync if the field is renamed).
      monitorLabel: this.getFieldLabel(this.context.parameters.monitor, 'Monitor'),
      highPriorityLabel: this.getFieldLabel(this.context.parameters.highPriority, 'High Priority'),
      accessPermissionLabel: this.getFieldLabel(this.context.parameters.accessPermission, 'Access Permission'),
      onMonitorChange: v => {
        this.monitorValue = v;
        this.notifyOutputChanged();
      },
      onHighPriorityChange: v => {
        this.highPriorityValue = v;
        this.notifyOutputChanged();
      },
      onAccessPermissionChange: v => {
        this.accessPermissionValue = v;
        this.notifyOutputChanged();
      },
    };

    // React 16 API per ADR-022 - use ReactDOM.render, NOT createRoot
    ReactDOM.render(
      React.createElement(
        FluentProvider,
        { theme: webLightTheme, style: { width: '100%' } },
        React.createElement(SharedTrackingFieldTrio, props)
      ),
      this.container
    );
  }

  public getOutputs(): IOutputs {
    return {
      monitor: this.monitorValue,
      highPriority: this.highPriorityValue,
      accessPermission: this.accessPermissionValue ?? undefined,
    };
  }

  public destroy(): void {
    // React 16 API per ADR-022 - use unmountComponentAtNode, NOT root.unmount()
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
