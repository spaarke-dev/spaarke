/**
 * "Email & Messages" Communications Grid — Form OnLoad Handler
 *
 * Web Resource Name: sprk_/scripts/communications_grid_onload.js
 *
 * messaging-communication-app-r3 task 033 (FR-15). Registered on a record
 * form's OnLoad event (Matter is the pilot; see the companion
 * `communicationsGridTab.FormXml.patch.xml` in this folder). Scopes the
 * "Email & Messages" tab's `sprk_communicationspage` Web Resource control to
 * ONLY the host record's communications by pushing a `data` envelope
 * (`regardingAttribute` + `regardingValue`) into the control via the
 * documented `WebResourceControl.setData()` Client API — the SAME `data=`
 * envelope convention already used by VisualHost drill-through pages
 * (see `DataGridPageShell.tsx`'s `parseUrlParentContext`). The Code Page
 * (`src/solutions/sprk_communicationspage/src/main.tsx`) reads that envelope
 * and forwards it to `<DataGridPageShell additionalHostFilters={…}>`, which
 * the shared DataGrid framework overlays into the grid's FetchXML via
 * `overlayHostFilters` (`fetchXmlOverlay.ts`) — the SANCTIONED `hostFilters`
 * imperative composition layer (ADR-006 Path C: web-resource framework, not
 * a PCF, not a bespoke JS filter).
 *
 * THIS SCRIPT IS ENTITY-AGNOSTIC (despite living in the `sprk_matter/` folder
 * for the Matter pilot). It resolves the current form's entity logical name
 * at runtime via `formContext.data.entity.getEntityName()` and looks up the
 * matching `sprk_regarding{type}` lookup from `REGARDING_FIELD_MAP` below —
 * the SAME 11-entity map as the server's single source of truth,
 * `Sprk.Bff.Api/Services/Communication/Engine/RegardingFieldMap.cs`
 * (ADR-024 regarding family; no second regarding mechanism). The compiled
 * web resource can be registered, UNCHANGED, on any of the 11 regarding-family
 * entity forms — only the FormXml patch (tab + control + library/handler
 * registration) needs to be authored per entity as each form is rolled out.
 *
 * IMPORTANT — keep in sync: `REGARDING_FIELD_MAP` below is a client-side
 * mirror of `RegardingFieldMap.cs`. There is no shared code path between the
 * C# server and this Dataverse form-library web resource, so a future change
 * to the server map (new regarding target, renamed field) MUST be mirrored
 * here by hand. This mirrors the same pattern already used for other
 * client/server option-set + map duplications in this codebase (e.g.
 * `sprk_communicationtype`).
 *
 * Correctness (over-disclosure risk — this is why this file exists):
 *   - The grid control is ONLY scoped when a `sprk_regarding{type}` field
 *     exists for the current entity in the map below. If the current form's
 *     entity type is NOT a regarding-family member, the control is left
 *     untouched (no `data` envelope pushed) — the underlying Code Page then
 *     renders with NO hostFilters, which would show ALL communications
 *     (over-disclosure). Any entity form that mounts this control MUST be a
 *     regarding-family entity; do not register this library/control on a
 *     non-family form.
 *   - Create-mode (record not yet saved, no id) is a hard bail — there is no
 *     record to scope to, so the control is left unmounted with no envelope.
 *   - The `eq` operator + a single scalar `regardingValue` guarantee an exact
 *     single-record match; `overlayHostFilters` treats malformed conditions
 *     (missing attribute/value) as a no-op skip, NEVER as "show everything"
 *     silently succeeding — see `fetchXmlOverlay.ts` `overlayHostFilters`
 *     pre-filter. So a bug in this script's payload construction fails safe
 *     (unfiltered-looking grid is visibly wrong and caught in UAT) rather
 *     than leaking a specific unintended record.
 *
 * Source layout:
 *   - Source: src/dataverse/forms/sprk_matter/communicationsGridOnLoad.ts
 *   - Compiled web resource: src/dataverse/forms/sprk_matter/communicationsGridOnLoad.js
 *
 * Form Events:
 *   Event: OnLoad
 *   Library: sprk_/scripts/communications_grid_onload.js
 *   Function: Spaarke.CommunicationsGrid.onLoad
 *   Pass execution context: Yes
 *
 * Critical constraints:
 *   - Q-U6: Power Apps form OnLoad mechanism — NOT Power Automate, NOT a
 *     bespoke JS filter (ADR-006 Path C: sanctioned DataGrid web-resource
 *     framework only).
 *   - NFR-style: must not block form load. All work is synchronous and cheap
 *     (map lookup + string concat); no network calls are made from this
 *     script — the Code Page itself performs the Dataverse read via the
 *     DataGrid framework's `IDataverseClient`.
 */

// =============================================================================
// TYPES (Client API subset used by this script — avoids a hard dependency on
// a full Xrm type-definitions package; matches the sibling scripts in this
// folder, e.g. insightWidgetOnLoad.ts / insightCardMount.ts).
// =============================================================================

interface XrmEntityReference {
  id: string;
  typename: string;
}

interface XrmFormEntity {
  getEntityName(): string;
  getId(): string;
  getEntityReference(): XrmEntityReference | null;
}

interface XrmWebResourceControl {
  getControlType(): string;
  // Documented Client API for WebResource/IFRAME controls — the `data`
  // string becomes the `data=` query parameter on the resource's URL.
  setData?(data: string): void;
  getData?(): string;
  // Defensive fallback path (IFRAME-style controls exposed via some hosts).
  setSrc?(src: string): void;
  getSrc?(): string;
}

interface XrmFormContext {
  data: {
    entity: XrmFormEntity;
  };
  getControl(name: string): XrmWebResourceControl | null;
}

interface XrmExecutionContext {
  getFormContext(): XrmFormContext;
}

// =============================================================================
// NAMESPACE
// =============================================================================

(function (): void {
  if (typeof window === 'undefined') {
    return;
  }
  interface SpaarkeGlobal {
    Spaarke?: {
      CommunicationsGrid?: CommunicationsGridNamespace;
      [key: string]: unknown;
    };
  }
  interface CommunicationsGridNamespace {
    _version: string;
    _controlName: string;
    onLoad(executionContext: XrmExecutionContext): void;
  }

  const w = window as unknown as SpaarkeGlobal;
  w.Spaarke = w.Spaarke || {};
  w.Spaarke.CommunicationsGrid = w.Spaarke.CommunicationsGrid || ({} as CommunicationsGridNamespace);
  const ns = w.Spaarke.CommunicationsGrid as CommunicationsGridNamespace;

  // ===========================================================================
  // CONFIGURATION
  // ===========================================================================

  /** Handler version (informational; bumped per task). */
  ns._version = '0.1.0';

  /**
   * Name of the "Web Resource" control (classid {9FDF5F91-88B1-47F4-AD53-
   * C11EFC01A01D}) hosting `sprk_communicationspage.html` on the "Email &
   * Messages" tab. MUST match the control name in the FormXml patch
   * (`communicationsGridTab.FormXml.patch.xml`) for every entity form this
   * script is registered on.
   */
  ns._controlName = 'WebResource_CommunicationsGrid';

  /**
   * Entity logical name → `sprk_regarding{type}` lookup field, mirroring
   * `RegardingFieldMap.All` in
   * `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/RegardingFieldMap.cs`
   * (ADR-024 polymorphic regarding family, priority order preserved).
   * KEEP IN SYNC — see file header "IMPORTANT — keep in sync".
   */
  const REGARDING_FIELD_MAP: Readonly<Record<string, string>> = {
    sprk_matter: 'sprk_regardingmatter',
    sprk_project: 'sprk_regardingproject',
    sprk_invoice: 'sprk_regardinginvoice',
    sprk_servicerequest: 'sprk_regardingservicerequest',
    sprk_workassignment: 'sprk_regardingworkassignment',
    sprk_event: 'sprk_regardingevent',
    sprk_budget: 'sprk_regardingbudget',
    sprk_analysis: 'sprk_regardinganalysis',
    sprk_organization: 'sprk_regardingorganization',
    account: 'sprk_regardingaccount',
    contact: 'sprk_regardingperson',
  };

  // ===========================================================================
  // HELPERS
  // ===========================================================================

  /** Strip the `{}` braces Dataverse wraps record ids in. */
  function stripBraces(id: string): string {
    return id.replace(/[{}]/g, '');
  }

  /**
   * Build the `data=` envelope payload string. Form-encoded, matching the
   * convention `DataGridPageShell.tsx`'s `parseUrlParentContext` already
   * expects for the `data` query parameter (see
   * `sprk_communicationspage/src/main.tsx`'s `parseRegardingHostFilter`,
   * which reads these exact two keys back out).
   */
  function buildDataEnvelope(regardingAttribute: string, regardingValue: string): string {
    return (
      'regardingAttribute=' +
      encodeURIComponent(regardingAttribute) +
      '&regardingValue=' +
      encodeURIComponent(regardingValue)
    );
  }

  // ===========================================================================
  // FORM EVENT HANDLER
  // ===========================================================================

  /**
   * OnLoad event handler. Resolves the host record's entity + id, maps the
   * entity to its `sprk_regarding{type}` lookup, and pushes the `data`
   * envelope onto the "Email & Messages" tab's Web Resource control so the
   * hosted `sprk_communicationspage` grid renders scoped to ONLY this record.
   *
   * @param executionContext — execution context passed by the form runtime.
   */
  ns.onLoad = function (executionContext: XrmExecutionContext): void {
    try {
      const formContext = executionContext.getFormContext();
      const entityName = formContext.data.entity.getEntityName();
      const recordIdRaw = formContext.data.entity.getId();

      if (!recordIdRaw) {
        // Create mode — no record yet. Nothing to scope the grid to; leave
        // the control unwired rather than risk an unfiltered/over-disclosing
        // render.
        // eslint-disable-next-line no-console
        console.log('[Communications Grid] v' + ns._version + ' onLoad: no record id (create mode). Skipping.');
        return;
      }

      const regardingField = REGARDING_FIELD_MAP[entityName.toLowerCase()];
      if (!regardingField) {
        // Entity is not a regarding-family member — this script should only
        // ever be registered on regarding-family forms. Fail safe: leave the
        // control unwired (never fall through to an unfiltered render).
        // eslint-disable-next-line no-console
        console.warn(
          '[Communications Grid] v' +
            ns._version +
            " onLoad: entity '" +
            entityName +
            "' is not in the ADR-024 regarding family (RegardingFieldMap). Leaving the grid control unscoped/unwired."
        );
        return;
      }

      const control = formContext.getControl(ns._controlName);
      if (!control) {
        // eslint-disable-next-line no-console
        console.warn(
          '[Communications Grid] v' +
            ns._version +
            " onLoad: control '" +
            ns._controlName +
            "' not found on form (tab may not be deployed yet). Skipping."
        );
        return;
      }

      const recordId = stripBraces(recordIdRaw);
      const dataEnvelope = buildDataEnvelope(regardingField, recordId);

      if (typeof control.setData === 'function') {
        control.setData(dataEnvelope);
      } else if (typeof control.setSrc === 'function' && typeof control.getSrc === 'function') {
        // Defensive fallback for control-type API variance across hosts.
        const baseSrc = control.getSrc() || '';
        const sep = baseSrc.indexOf('?') === -1 ? '?' : '&';
        control.setSrc(baseSrc + sep + 'data=' + encodeURIComponent(dataEnvelope));
      } else {
        // eslint-disable-next-line no-console
        console.warn(
          '[Communications Grid] v' +
            ns._version +
            " onLoad: control '" +
            ns._controlName +
            "' exposes neither setData nor setSrc. Cannot scope the grid — leaving it unwired (fail safe)."
        );
        return;
      }

      // eslint-disable-next-line no-console
      console.log(
        '[Communications Grid] v' +
          ns._version +
          ' onLoad: scoped to ' +
          entityName +
          ' ' +
          recordId +
          ' via ' +
          regardingField +
          '.'
      );
    } catch (error) {
      // Catch-all so the handler never breaks form load (matches sibling
      // scripts' NFR-03-style convention).
      // eslint-disable-next-line no-console
      console.error('[Communications Grid] Error in onLoad (form load unaffected):', error);
    }
  };
})();
