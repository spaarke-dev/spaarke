# Owner decisions — spaarkeai-mcp-bff-api-r1 (2026-11-26)

Each item says what it is, why it needs you, and my recommendation. Tick one answer per item.

## A. The slow decision save (ISS-010, #3184; ~27 s, target 15 s)

### A1. Merge #3193 (part 1: table lookups shared within a save)
- **What:** a save of four actions now looks up each table's structure once instead of repeatedly: 37 lookups become 25, about 1.1 s faster live. This alone does not reach 15 s.
- **State:** ontology-r1, uac-r2 (security) and field-mapping-r1 (the file's owner) all approve. All the required checks pass. The package-size figure (+1 KB) was measured before the last two commits.
- **Recommendation: merge.** I re-measure the package size first and stop if it has moved by more than a few KB. Part C waits on this merge.
- [ ] Merge (I re-measure first)  [ ] Hold

### A2. Part E: move the decision emails' follow-up work to a background job (spec exception)
- **What:** after an Assign Task or budget inquiry email is sent, the save waits for 55–80 server calls and one AI call: filing the email into SharePoint, threading, AI triage, the participant index and the "arrived" notification. Moving these to a queued job saves about 3.5–6.5 s per email. The send itself stays in the save, so a failed send is still reported to the user (your rule from yesterday). If filing finally fails, the user gets a bell linking to the email record, where they can click "Save to SharePoint" (your rule).
- **Why you:** this project's spec says it adds no background work (ADR-052). field-mapping-r1, whose pipeline this is, declined to own the job. The way forward is a recorded project exception in our spec (CLAUDE.md §6.5 Path A), cited in the PR, with field-mapping-r1 reviewing the email-service changes and ontology-r1 reviewing too.
- **Recommendation: approve the exception.** It is the largest single saving, and without it the save cannot reach 15 s. The job follows the standard ADR-004 queue pattern: retried up to 5 times, safe to repeat, and every failure leads to a bell or a log entry.
- [ ] Approve Path A  [ ] Keep it in the save (then 15 s is not reachable)  [ ] Another owner should build it

### A3. FYI, no answer needed: two smaller Part E questions I'll settle with ontology-r1
- Q1: decisions' plain "send email" / "send reminder" actions use the same background path. I'll propose yes (same cost, same safety).
- Q5: one failure bell per email, not one per decision, so each bell links to the record the user fixes. I'll propose per email.

## B. MCP route Host/Origin check (ISS-007, #2835)
- **What:** the MCP transport rules ask servers to check the `Host`/`Origin` header, to block DNS-rebinding attacks from a browser. Our route doesn't check it, and the one conformance test for it is excused. The risk is low: every call needs a valid sign-in token, there are no cookies to steal, and CORS is an allow-list.
- **Recommendation:** add the check. Reject a request whose `Origin` header is present and not on an allow-list (a browser always sends `Origin`). Let requests without `Origin` through, since Copilot and Cowork call server to server. Check `Host` against the BFF's own host names. Before shipping, record the headers Copilot and Cowork actually send in one live run, then remove the excused test. About 2–4 hours plus that live check.
- [ ] Do it as recommended  [ ] Leave it excused for v1

## C. "Done today" on the Copilot widget's category cards (DEF-001, #2832)
- **What:** the spec (FR-13) wants each Decide-lane card to show how many items were closed today, as the Console does. No v1 tool returns that number, so the widget leaves the line out rather than inventing one.
- **Options:** (a) add a closed-today count to `spaarke_show_decisions`; (b) a separate tool; (c) drop it from v1 and amend FR-13.
- **Recommendation: (a), handed to mcp-r2.** r2 now owns the MCP tools and the catalog, so the change belongs in their scope. Here, record FR-13's line as "deferred to r2".
- [ ] (a) via r2  [ ] (c) drop from v1  [ ] Other

## D. Live testing (task 042), all steps need you or a second admin
- **Remaining:** resolve one item in Chat; check a "because" reason reached the Decision Record (I can read it, read-only); check the invoice-review link opens; a second admin re-runs the setup runbook; 50 decision saves for the speed check (NFR-01).
- **Recommendation:** do the Chat resolve and link check whenever convenient. Run the 50-save speed check after #3193 (and ideally Part C) is deployed. Measuring now only measures the old, slow path. Each save writes to dev Dataverse, so the run needs your go.
- [ ] Agree  [ ] Run the speed check now anyway

## E. Deploys (each needs your go for the exact build, through uac-r2)
- **What:** the Copilot widget's finding-first change (#3199, merged as `14f45b7412`) ships only with a server (BFF) deploy. The Console and LegalWorkspace already have it.
- **Recommendation:** one server deploy after #3193 merges, so the widget change and the faster save ship together.
- [ ] One deploy after #3193  [ ] Deploy `14f45b7412` now

## F. Copilot tenant cleanup (S-1, then remove the spike plugin), your actions in the tenant
- **What:** with both Copilot registrations connected ("Spaarke MCP" and the old spike plugin), ask Cowork and Chat to list the Spaarke tools. Record the names, prefixes and source labels in `notes/phase0-results.md`; I send them to the coordination session. Then remove the spike plugin (tenant inventory row C2). You approved this order on 2026-11-23.
- **Recommendation:** do S-1 at your next Copilot session, then remove the spike plugin.
- [ ] Will do

## G. Worktree cleanup (removal only via `scripts/worktree/Remove-WorktreeSafe.ps1`)
- `C:\wtux`: #3199 is merged, nothing is left there. **Recommend: remove.**
- `C:\wt3184`: remove after #3193 merges.
- `C:\wt3184c` (Part C): keep.
- `C:\wt2623` (ISS-003 check, no commits; the issue is closed as not a defect): **Recommend: remove.** Delete its empty local branch `fix/iss-2623-precedent-projection-scope` too.
- [ ] Remove `C:\wtux` and `C:\wt2623` now  [ ] Remove `C:\wt3184` after #3193 merges

## H. Who reviews and merges the Compose fix (ISS-002, #2620, PR #2622)
- **What:** when Compose starts its background profile step, it can clear the original request's sign-in context partway through the request, so the rest of that request loses its identity. The PR runs that step without that side effect and keeps its log entries tied to the right user.
- **State (2026-11-26):** brought up to date with master (`605438d64f`, no conflicts). Master's Compose code still has the defect. All 5 regression tests pass, and taking the fix out makes them fail. The build is clean. It has had no review since 7 Nov, and no Compose session is active to review it.
- **Recommendation:** one independent review here (an Opus reviewer, since it is in the request pipeline), then your merge, since no Compose owner is active. Or name the Compose project that should review it.
- [ ] Review here, then I merge on your go  [ ] Wait for the Compose owners (name the project)
## Already handled without you (for information)
- ISS-001 (#2575, unused April Copilot agent code): mcp-r2's task 001 already removed it on their branch. **Handed off:** r2 accepted it, the label is swapped, and their merge PR will close it.
- ISS-003 (#2623): **closed as not a defect.** Everything the background task touches is a singleton or holds nothing that can be disposed. The evidence is on the issue, and the optional hardening is noted for the Precedents owners.
- ISS-010 D1: **design draft written** (`notes/iss-010-part-d1-design.md`), about 14–33 fewer reads per save (1.3–3 s). Sent to uac-r2 (access questions) and field-mapping-r1 (their files). It's built after #3193 merges.
