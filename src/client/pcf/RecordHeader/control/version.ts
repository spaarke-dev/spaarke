/**
 * RecordHeader PCF - single source of truth for the control version.
 *
 * Version MUST be kept in sync across 5 locations (ADR-020;
 * `src/client/pcf/CLAUDE.md` says 4 — that count is stale, `pcf-deploy` adds
 * pack.ps1, which is what actually names the emitted ZIP):
 *   1. control/ControlManifest.Input.xml                (version="X.Y.Z")
 *   2. control/version.ts                               (CONTROL_VERSION — this file)
 *   3. Solution/solution.xml                            (<Version>X.Y.Z.0</Version>)
 *   4. Solution/Controls/sprk_Spaarke.Records.RecordHeader/ControlManifest.xml
 *      (build artifact — do NOT hand-edit; it is copied from out/ after build)
 *   5. Solution/pack.ps1                                ($version = "X.Y.Z.0")
 *
 * See docs/guides/PCF-DEPLOYMENT-GUIDE.md and .claude/skills/pcf-deploy/SKILL.md
 * for the update workflow.
 *
 * 1.1.0 (2026-08-25) — initial release of the configuration-driven control
 * (record-header-and-notepad-r2 task 033). Starts at 1.1.0 rather than 1.0.0
 * because this is a NEW control identity carrying R1s already-shipped
 * MatterHeader feature set (spec assumption, confirmed here) plus the R2
 * renderer/config work. `MatterHeaderPcf` v1.0.21 stays live and is retired
 * separately at task 081.
 *
 * 1.1.1 (2026-08-26) — first-UAT defect fixes.
 *   DEF-1 metadata never reached the resolver, so every field derived the
 *   `text` renderer, every label humanized its logical name, and a lookup got
 *   `$select`ed by its bare name (HTTP 400 -> every cell an em-dash). Two
 *   independent causes, both fixed in `@spaarke/ui-components`:
 *     (a) the attribute label/type rescue call used
 *         `Xrm.WebApi.retrieveMultipleRecords('EntityDefinition', ...)`, which
 *         cannot work - `Xrm.WebApi` does not serve metadata entities - so it
 *         threw on every call and its catch swallowed the throw;
 *     (b) `projectAttribute` parsed only Web-API shapes, but the CLIENT API
 *         returns a numeric `AttributeType` and a plain-string `DisplayName`.
 *   Plus two defences: the metadata fetch now NAMES the attributes it needs,
 *   and a failed `$select` read degrades to an unprojected read instead of
 *   blanking the header (the RS-1 failure mode, third occurrence).
 *   DEF-2 `layoutJson` moved to `of-type="Multiple"` - the classic form
 *   designer caps SingleLine.Text at 100 characters, below any real layout.
 *
 * 1.1.3 (2026-08-26) - NFR-02 bundle-ceiling fix. `DateField` now edits through
 *   the Fluent `Input` in native date mode (`type="date"` /
 *   `type="datetime-local"`), the pattern already shipping in
 *   `Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/EnterInfoStep.tsx`,
 *   replacing `@fluentui/react-datepicker-compat`. `Input` lives inside the
 *   `@fluentui/react-components` umbrella that `pcf-scripts` externalizes onto
 *   the platform library, so it costs ZERO bundle bytes; the picker imported
 *   its deps by their granular package names, none of which match that
 *   externals regex, so webpack bundled a second private copy of Fluent
 *   internals the host already serves. bundle.js: 378,457 -> 99,068 bytes
 *   (-73.8%), now 40% of the 250,000-byte ceiling. Zero `datepicker-compat` /
 *   `calendar-compat` references remain in the emitted bundle.
 *   Two earlier attempts on this defect FAILED and must not be retried:
 *   lazy-loading `DateField` (pcf-scripts emits one chunk, so `import()`
 *   inlines back and measured LARGER) and a custom webpack `externals` block
 *   for the granular `@fluentui/*` packages (built clean, passed static symbol
 *   verification, then crashed on mount with "Minified React error #31" - see
 *   notes/decisions/033-nfr02-externals-runtime-failure.md).
 *   The FR-10 renderer contract is unchanged; `DateField` is now a plain
 *   staged-draft renderer (type stages, Enter/blur commits) rather than the
 *   commit-on-calendar-selection special case, and all wall-clock <-> `Date`
 *   conversion goes through LOCAL calendar fields so no value shifts a day.
 *
 * 1.1.4 (2026-08-26) - sparkle wired to `sprk_recordsummary` (task 034, FR-17 /
 *   FR-22 / FR-23). Visibility keys on the attribute EXISTING in entity
 *   metadata, never on it being populated: an existing-but-empty column still
 *   shows the sparkle and the popover reads "No summary yet." (a separate
 *   project populates these columns, so empty IS the expected state at ship
 *   time). When the attribute is absent the `aiSummary` prop is OMITTED
 *   entirely - `HeaderToolbar` then renders no sparkle at all, rather than a
 *   dead one whose popover is permanently empty.
 *   Two things had to be true for that gate to work:
 *     (a) the metadata request now names BOTH summary candidates (the
 *         configured `summaryField` AND the `RECORDSUMMARY_FIELD` default).
 *         `sprk_recordsummary` is on none of the six rollout entities' FORMS,
 *         so without this the payload would never contain it, the gate would
 *         fail on every entity, and the sparkle would be invisible everywhere
 *         with no error to explain why;
 *     (b) the column joins the `$select` ONLY after that check passes - a
 *         `$select` naming a column the entity lacks fails the WHOLE retrieve
 *         with HTTP 400 and blanks every cell (RS-1, third occurrence).
 *   The field name is IMPORTED from the shared library, never re-declared - the
 *   v1.0.20 sparkle regression was two copies of that literal drifting apart -
 *   and a source-grep test now enforces it.
 *   Shared-lib additions (additive; zero change for the nine existing callers):
 *   `AiSummaryPopover` gained an optional `emptyText` plus stable
 *   `data-testid`s, and `IHeaderToolbarProps.aiSummary` forwards `emptyText`.
 *   The refresh icon stays UNWIRED (DEF-01) - and is now absent rather than
 *   inert, since the shared popover offers only copy-to-clipboard.
 */
export const CONTROL_VERSION = '1.1.4';
