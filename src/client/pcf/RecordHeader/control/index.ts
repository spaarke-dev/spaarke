import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import { RecordHeaderHost } from './RecordHeaderHost';
import { resolveEntityContext } from './entityContext';

/**
 * RecordHeader PCF Control — `Spaarke.Records.RecordHeader`.
 *
 * The configuration-driven generalization of R1s per-entity `MatterHeader`.
 * ONE control, bound on any entity form; the field layout comes from the
 * `layoutJson` manifest property (FR-01) or, when that is blank/invalid, from
 * entity metadata (FR-04). Adding the header to a new entity is a FORM EDIT,
 * not a code change — which is why NO entity logical name may ever be compiled
 * into this control (FR-12).
 *
 * Follows:
 * - ADR-006: PCF for all custom UI
 * - ADR-012: shared component library (composition of @spaarke/ui-components primitives)
 * - ADR-021: Fluent UI v9 semantic tokens
 * - ADR-022: React 16 APIs with platform-library React + Fluent v9 (control-type="virtual")
 *
 * All Dataverse I/O uses Xrm.WebApi / Xrm.Page via shared hooks (no BFF, no
 * `@spaarke/auth`) — spec NFR-05 / NFR-06.
 */

// NOTE: this module must define EXACTLY ONE export — `pcf-scripts` rejects a
// second one with `[pcf-1023] Control source code defines more than one
// export.` The FR-12 self-detection helper therefore lives in `entityContext.ts`.
export class RecordHeader implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  public init(
    _context: ComponentFramework.Context<IInputs>,
    _notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    // No async init; auth is not used (host-context Xrm only).
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    // FR-12 — entity + record are SELF-DETECTED. The bound field (`boundField`)
    // is manifest-required only so Dataverse shows the PCF in the form
    // designers "Add component" gallery; its value is never read.
    const { entityName, recordId } = resolveEntityContext(context);

    // `title` overrides the layoutJson/metadata title when non-blank.
    const title = context.parameters.title?.raw ?? undefined;

    // `showVersion` is a TwoOptions field: PCF surfaces it as boolean | undefined,
    // so default to true (footer visible) unless the maker explicitly toggles off.
    const showVersion = context.parameters.showVersion?.raw !== false;

    // FR-01 — the RAW manifest string. Parsing belongs to `resolveHeaderConfig`
    // (task 031) so the malformed-config failure path lives in exactly one place.
    const layoutJson = context.parameters.layoutJson?.raw ?? null;

    // The host mounts a <FluentProvider> so Fluent v9 CSS variables reach
    // portal-rendered surfaces (pcf-build-scaffold gotcha 6). `context` is
    // passed so the theme resolver can read `fluentDesignLanguage.isDarkTheme`.
    return React.createElement(RecordHeaderHost, {
      entityName,
      recordId,
      title,
      showVersion,
      layoutJson,
      context,
    });
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    // No cleanup required (no listeners, no timers, no auth handles).
  }
}
