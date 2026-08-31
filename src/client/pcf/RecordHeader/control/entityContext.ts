/**
 * FR-12 entity self-detection — reading the current form identity off the PCF
 * context.
 *
 * Lives in its own module because `pcf-scripts` enforces that the manifest
 * `<code path="index.ts">` entry module define EXACTLY ONE export
 * (`[pcf-1023] Control source code defines more than one export.`). Exporting
 * this helper from `index.ts` alongside the control class fails the build, so
 * it is hoisted here — which also makes the type-cast idiom directly testable.
 *
 * NEITHER surface below exists in `@types/powerapps-component-framework`, so
 * both reads go through a cast (pcf-build-scaffold gotcha 3):
 *
 *  - `context.mode.contextInfo` is the PRIMARY surface — proven in
 *    `VisualHost/control/components/VisualHostRoot.tsx:246-253`.
 *  - `context.page` is a DIFFERENT, older surface used only as the fallback —
 *    see `TrackingFieldTrio/index.ts:337-348`. It is the fallback, NOT evidence
 *    for `contextInfo`; the two are not interchangeable.
 *
 * No entity logical name is (or may ever be) compiled into this control — that
 * is the whole point of FR-12.
 */

interface IEntityContextSurfaces {
  mode?: {
    contextInfo?: {
      entityTypeName?: string;
      entityId?: string;
    };
  };
  page?: {
    entityTypeName?: string;
    entityId?: string;
  };
}

/** Resolved current-form identity. Empty strings mean "not determinable". */
export interface IResolvedEntityContext {
  entityName: string;
  recordId: string;
}

/**
 * Read the current form's entity logical name + record id from the PCF context.
 *
 * `contextInfo` first, `context.page` second. Never throws: a host that
 * supplies neither surface yields empty strings, and the view degrades to its
 * no-record state rather than blanking the form (NFR-10).
 */
export function resolveEntityContext(context: unknown): IResolvedEntityContext {
  const surfaces = context as unknown as IEntityContextSurfaces | null | undefined;
  const contextInfo = surfaces?.mode?.contextInfo;
  const page = surfaces?.page;

  return {
    entityName: contextInfo?.entityTypeName || page?.entityTypeName || '',
    recordId: contextInfo?.entityId || page?.entityId || '',
  };
}
