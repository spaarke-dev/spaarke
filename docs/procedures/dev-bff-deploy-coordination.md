# Dev BFF deploy coordination

> **Owner (orchestrator):** `unified-access-control-r2`, from 2026-11-03 (owner decision). Before that, `spaarke-SPA-external-access-platform-r3` ran every coordinated dev deploy (its record: `projects/spaarke-SPA-external-access-platform-r3/notes/contact-identity-dev-rollout-record.md` §9–17).
> **Applies to:** `spaarke-bff-dev` (resource group `rg-spaarke-dev`), the single dev BFF that every active project shares. Production uses `production-release.md`; emergencies use `bff-deploy-emergency.md`.

## Why one orchestrator

Every active project lands code on the dev BFF, and a deploy ships all of master at once. So one session runs each deploy end to end: it collects the checks, deploys, reads back, notifies, and keeps the record. Other projects ask the orchestrator for a deploy; they do not deploy dev themselves.

## Gates (all required, in this order)

1. **Owner go, direct and fresh.** The owner names the exact SHA ("go BFF deploy <sha>") in the orchestrator's own session. A go relayed by another session is not a go. A go for one SHA does not cover another.
2. **Peer OKs for that SHA.** Ask each active BFF project. In 2026-11: ontology, word-add-in, field-mapping and ebilling; provisioning gave a standing OK. Use `projects/INDEX.md` (BFF = Y) for the current list. A project can attach conditions, such as a re-seed it runs after the deploy.
3. **Router green** on the commit (`gh run list --commit <sha>`). A cancelled Arch Tests job fails Router with every step green (#2527); re-run the failed jobs (`gh run rerun <id> --failed`) before treating it as a code defect.
4. **Content checks** on the SHA, read-only:
   - `git merge-base --is-ancestor 929dc42d31 <sha>` exits 0 (#2450 present);
   - no non-comment `sprk_invoicestatus` under `src/server` (`git grep -n sprk_invoicestatus <sha> -- src/server`, then read each hit);
   - any check a peer attached to its OK.
5. **ebilling schema verify** exits 0, run from the deploy worktree:
   `& <worktree>\scripts\Set-ChildRecordCreatorPersonSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5 -Verify`
6. **App settings**, names and booleans only, never values:
   - the count matches the last record (225 on 2026-11-03);
   - `SecureRoot__Inheritance__RootOwnerWritesEnabled` and `SecureChild__Reconciliation__OrdinaryMovesWritesEnabled` are `true`;
   - `Ebilling__Enabled` is `true` and `Ebilling__StoredFileReconcileEnabled` is `false`;
   - the three `Finance__FileStore__*` names are present.

   Example: `az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev --query "length(@)"`, and the same with a `[?name=='…'].{n:name,v:value}` query for the boolean switches only.

## Steps

1. **Worktree:** `git worktree add --detach C:\wtbffe-<sha> <sha>` (a short path; a deep path can produce a partial publish).
2. **Gates 4–6** from that worktree.
3. **Snapshot:** `pwsh -File scripts/Save-BffDevRollbackSnapshot.ps1` from the orchestrator's own worktree. Record the live ProductVersion and the snapshot folder; that worktree keeps the snapshot.
4. **Deploy:** `scripts/Deploy-BffApi.ps1 -Environment dev`, run from `C:\wtbffe-<sha>`.
5. **Read-back:** `pwsh -File scripts/Test-BffDevDeployReadback.ps1`. Expect the ProductVersion to carry the approved SHA, /healthz 200, /healthz/dataverse 200 and anonymous /api/me 401. Then re-check the settings (gate 6): the count must be unchanged.
6. **After-notices** to every peer: SHA, receive time, readback, rollback target. Projects with post-deploy work (a field-mapping re-seed, an ebilling import) run it in their own session with the owner's OK there; the orchestrator never runs it for them.
7. **Gate:** uac-r2 watches 30 minutes from the receive time (5xx, OWNERSHIP-REFUSED, SECURE-CENSUS, the external routes split out). Report PASS or FAIL to the peers.
8. **Record** a section in the orchestrator's rollout record: content, CI, go, OKs, checks, snapshot, deploy, readback, notices, gate, rollback.
9. **Clean up:** `& scripts\worktree\Remove-WorktreeSafe.ps1 -Path C:\wtbffe-<sha>` (never `git worktree remove`; FAILURE-MODES G-20).

## Rollback (a live change: owner approval first)

- **Fast:** restore the snapshot: `scripts/Deploy-BffApi.ps1 -Environment dev -SkipBuild -PublishPath deploy/rollback/wwwroot-<timestamp>`, from the worktree that holds it.
- **Always available:** redeploy the previous SHA from a fresh detached worktree, following the steps above.
- Then repeat the read-back and send notices.

## Rules that do not change

- Live reads are read-only, and the commands are shown to the owner before they run. Every live change needs owner approval and a stated rollback.
- Mask by default: never print tokens, secret values, emails, UPNs or object ids in records, commits or chat.
- One deploy at a time. Wait until the previous deploy's gate has passed before starting the next.
