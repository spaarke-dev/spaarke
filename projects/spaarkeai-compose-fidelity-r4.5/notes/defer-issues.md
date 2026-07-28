# Deferred issues — spaarkeai-compose-fidelity-r4.5

Tracked follow-ups surfaced during execution. (Two-write rule: file as GitHub Issues at PR/wrap-up time.)

| ID | Title | Surfaced | Disposition | GitHub Issue |
|----|-------|----------|-------------|--------------|
| DEF-01 | `ComposeEditor.advisoryComments.test.tsx` fails ("placed 1 vs 2") — advisory-comment **target-resolution**. Confirmed **pre-existing on master** (failing at task 012 before task 013 touched the file; no advisory-placement source changed on this branch). | WS-1 gate (013) | **Assign to WS-3 task 031** — advisory anchoring depends on the paragraph/numbering model 031 builds; the test's own sessionId is `session-nda-review-031`. Re-run after 031; if still red, fix there. | {URL} |
| DEF-02 | `/api/compose/project` + `/upload` run **synchronous** projection (CPU) on `byte[] Content` with only Kestrel's implicit ~28.6 MB body cap — no Compose-specific size guard. | WS-1 gate (code-review) | **Hardening follow-up** — add an explicit request-size limit aligned with the 25 MB chat-attachment policy (`docs/standards/CHAT-ATTACHMENT-POLICY.md`). Same profile as existing Load path (not a regression); low severity. Consider at WS-1 deploy PR or a small hardening task. | {URL} |
