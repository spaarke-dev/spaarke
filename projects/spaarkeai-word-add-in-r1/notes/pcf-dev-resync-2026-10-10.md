# PCF dev re-sync — 2026-10-10

**Why.** The owner asked that every PCF changed on master be deployed, so undeployed changes don't surface later when another project, or a new customer environment, deploys the control.

## How drift was found

- Every PCF was built fresh with `build:prod` and compared byte for byte with the `cc_<ns>.<ctor>/bundle.js` that spaarkedev1 serves. The build is reproducible, so a matching bundle means the control is in sync.
- Where the version was the same as dev's but the code differed, the version was bumped in PR #1613 (merged as `b69b85a95`).
- After that merge, all 14 were rebuilt from master `b69b85a95`. The rebuilt bundles were byte-identical to the `Solution` copies committed in #1613: the shared-library PRs that merged meanwhile (#1605, #1583, #1611) do not reach these bundles.

## Deployed to spaarkedev1

- **Method:** `scripts/Import-SolutionScoped.ps1` (scoped publish, no tenant-wide publish).
- **Verification for every row, from the Web API:**
  - `solutions.version` equals the ZIP's version;
  - the newest `importjobs.progress` is 100 and started after the import began;
  - the published `bundle.js` is byte-identical to the build.

  The Web API check is needed because of #1591: the import command can report success on a failed import.

| Control | Solution | Version | Result |
|---|---|---|---|
| CommunicationActions | CommunicationActionsSolution | 1.3.19 | OK |
| CommunicationAttachments | CommunicationAttachmentsSolution | 1.3.3 | OK |
| CommunicationConversationPanel | CommunicationConversationPanelSolution | 1.18.2 | OK |
| CommunicationMessageActions | CommunicationMessageActionsSolution | 1.0.5 | OK |
| CommunicationTimeline | CommunicationTimelineSolution | 1.0.4 | OK |
| CommunicationTimelineRegarding | CommunicationTimelineRegardingSolution | 1.0.3 | OK |
| DocumentRelationshipViewer | SpaarkeDocumentRelationshipViewer | 1.0.38 | OK |
| MatterHeader | MatterHeaderPcf | 1.0.22.0 | OK |
| RecordHeader | RecordHeaderPcf | 1.1.12.0 | OK |
| RelatedDocumentCount | SpaarkeRelatedDocumentCount | 1.21.6 | OK |
| CommunicationConnections | CommunicationConnectionsSolution | 1.7.1 | OK |
| RegardingResolver | RegardingResolverSolution | 1.6.2 | OK |
| SpeDocumentViewer | SpaarkeSpeDocumentViewer | 1.0.29 | OK |
| ScopeConfigEditor | ScopeConfigEditorSolution | 1.3.0 | OK |

## Not deployed, with reason

| Control | Reason |
|---|---|
| SemanticSearchControl, VisualHost, ThemeEnforcer | Already in sync |
| SpaarkeGridCustomizer | Only CRLF in the license header differs |
| TrackingFieldTrio | Owned by UAC-r2, which deploys it in its own rounds |
| UpdateRelatedButton | On no form, and has no pack script. Dev has a February dev-mode build. |
| EmailProcessingMonitor | Dead folder (only `package-lock.json`) |
