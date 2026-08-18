# MDA "custom help pane" fit for Spaarke help guides + P&P library (2026-08-14)

**Question**: Is the model-driven app "custom help pane" feature a viable home for Spaarke's (1) context help guides and (2) Policy & Procedures library, given they have their own cross-surface Navigation pane + custom React SPAs?

**Findings**:
- Two DISTINCT MDA features, mutually exclusive (enabling one disables the other via env setting):
  1. **Custom help panes + guided tasks** (newer; replaces legacy "learning path"). Rich-text/coach-mark/balloon help authored IN-APP (Help command bar > ellipsis > Edit). Stored in Dataverse **Help Page (`msdyn_helppage`)** table, part of the **Contextual Help managed solution**. Also a PPHML XML export/import format ("Power Platform Help Markup Language"). Context-sensitive per **Table / Form / Dashboard / Language / Application** (NOT per-role, NOT per-device). Videos/images are internet LINKS (Stream/YouTube/Facebook/Vimeo), not stored. Coach marks/balloons only target DEFAULT Unified Interface components, not custom components. GA (how-to doc, ms.date 2025-07-23). Requires env setting "Enable Custom Help Panes" ON. **Does NOT work on mobile (Android/iOS).**
  2. **Customizable help** (older "Set custom Help URL"). Overrides the Help link target with YOUR external URL, global or per-table, with appended params (userlcid, table, formid, hierarchy/form entry point). This is the ONE that can point at an external/custom store. ms.date 2026-03-02.
- **Rendering is MDA-shell-only for BOTH.** Custom help panes render inside the Unified Interface Help pane; customizable help just redirects the MDA Help link. NEITHER renders in a standalone React code page, Legal Front Door SPA, or external-access SPA. This is the decisive fit failure.
- **External/CIAM/unlicensed users CANNOT see it** — MDAs require a Power Apps (per-app/per-user) or D365 license to run at all; external SPA users never enter the MDA shell.
- **Licensing (positive vs knowledgearticle)**: NO restricted-table dependency. Help Page is in Contextual Help managed solution, not the licensed `knowledgearticle`. Consuming needs read priv on Help Page table (Help Page Consumer role; Basic User lacks it by default). Authoring needs full privs on Help Page (Sys Admin/Customizer have it). No EXTRA paid license beyond the base MDA license users already need.
- **Programmatic push**: unsupported. Docs explicitly say creating/customizing the Contextual Help tables "outside of the custom help panes and guided tasks feature isn't supported." Supported automation = solution export/import + per-page XML files only.

**Fit verdict**:
- Help GUIDES (context help): PARTIAL fit for the MDA surface ONLY. Good in-context per-form help inside MDAs with zero build, but covers 0% of the React code pages / Legal Front Door SPA / external SPA — Spaarke's own Navigation pane already spans all surfaces. Only worth it if there's meaningful MDA-form usage the custom pane doesn't reach.
- P&P LIBRARY: POOR fit. Confined to MDA shell, invisible to external/CIAM users (the exact audience for P&P), no programmatic push, coach-mark model is form-centric not document-library. Spaarke's SPE + `sprk_policy` + RAG decision stands; custom help pane adds nothing for P&P.

**Sources**:
- https://learn.microsoft.com/en-us/power-apps/maker/data-platform/create-custom-help-pages (ms.date 2025-07-23) — most authoritative; storage table, PPHML, context, mobile limit, privileges
- https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/use-customizable-help (ms.date 2026-03-02) — customizable-help URL override, mutual exclusivity

**Open questions**: Is there any measurable Spaarke user population working primarily inside MDA forms (vs code pages/SPAs) that would benefit from in-context MDA help? If not, the MDA-shell confinement makes both features non-starters. Could msdyn_helppage rows be created via Dataverse Web API despite the "unsupported" statement (not tested — and unsupported = risky for ALM).
