# Per-customer Copilot agent gate (Step 6f)

Moved from SKILL.md Step 6f (2026-11-03). SKILL.md says when to read this file (the run is Completed). Each numbered
step is unchanged. The registry PATCH in step 4 uses the same path as Step 6a: the Dataverse MCP `update_record`, or the
F1 Web API fallback (`Invoke-RegistryWebApi` in `scripts/provision-environment/ProvisionEnvironment.psm1`).

Each customer gets its own Microsoft Copilot agent. The customer's IT installs it in THEIR tenant; never install it in
Spaarke's catalog. The full procedure, with the one-time platform setup, is
[`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../../../../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) §7.12; do not copy
it here. This gate is the exception to the no-human-interaction end state that the owner accepted on 2026-10-09
(design note t257 Q3). Microsoft has no API for creating an auth config. Re-check Graph and `wiqd` at each refresh.

Interactive mode only. In batch mode, write `runs/{runId}-copilot-gate.md` with the values below, mark it PENDING in the
handoff, and continue.

1. **Pre-check (read-only).** If either check fails, STOP the gate, not the run, and report it:
   - `az ad app show --id <run.interStepState.bffAppRegId> --query "api.preAuthorizedApplications[].appId" -o tsv`
     lists the Spaarke Copilot Agent client id (`copilotAgentClientAppId` in the control-plane bicepparam). If it does
     not, the control plane was deployed without the client; fix that first (guide §7.12 step 3).
   - The registry row has `sprk_bffappid` (H13 promoted it, or Step 6a did).
2. **Show the operator the auth-config values** and ask them to create it in the Teams developer portal (Tools → OAuth
   client registration → New):
   - name `spaarke-copilot-{customerId}`. Reuse it if it exists; never create a duplicate;
   - base URL `<run.interStepState.bffApiUrl>`;
   - restrict usage by org: Any Microsoft 365 organization;
   - restrict usage by app: Any Teams app;
   - client id = the Spaarke Copilot Agent client, **no client secret**;
   - authorize `https://login.microsoftonline.com/<Spaarke tenant id>/oauth2/v2.0/authorize`;
   - token and refresh `https://login.microsoftonline.com/<Spaarke tenant id>/oauth2/v2.0/token`;
   - scope `api://<bffAppRegId>/user_impersonation offline_access`;
   - PKCE on.
3. **Ask for the OAuth client registration ID.** Refuse a value that does not match `^[A-Za-z0-9+/=_.-]{8,512}$`.
4. **Write it to the registry** with the operator's identity. This is a live write; confirm before sending it.
   `PATCH sprk_dataverseenvironments(<environmentId>)` `{ "sprk_copilotauthconfigid": "<id>" }`, through the Dataverse MCP
   or the F1 Web API fallback, as in Step 6a. If `sprk_bffappid` is empty, set it in the same PATCH from
   `run.interStepState.bffAppRegId`.
5. **Render the package** (local file only):
   `scripts/copilot-agent/Render-CopilotAgentPackage.ps1 -TemplatePath <template zip> -TemplateManifestPath <copilot-agent-template-latest.json> -OutputFolder runs/<runId>-copilot -CustomerId <customerId> -AdminEnvironmentUrl <registry env URL>`.
   - Download the template and manifest read-only from `provisioning-artifacts`.
   - Record the package path, version, manifest id and SHA-256 in `runs/{runId}.md` § Copilot agent.
6. **Hand-off text for the operator:** "Send `spaarke-copilot-<customerId>-<version>.zip` and
   docs/guides/COPILOT-AGENT-CUSTOMER-IT-ONBOARDING.md to the customer's IT. They upload it in their Microsoft 365 admin
   center (Agents → Upload custom agent) and assign it to the staff invited as guests. When one user has signed in,
   check the BFF log: `tid` = Spaarke, `acct` = 1, `azp` = the Spaarke Copilot Agent client, `aud` = this BFF."
