---
name: atk-oauth-register-minimal-project-2026-10-28
description: Minimal atk (M365 Agents Toolkit CLI) project that runs ONLY oauth/register with no secret/no Teams app; driver facts (appId, comma scopes, idempotency), commands, empirically verified up to token acquisition
metadata:
  type: reference
---

## 2026-10-28: Minimal `atk` project for a secretless OAuth auth config (T257)

**Question**: Exact minimal m365agents.yml + commands to create one OAuth auth config (Enterprise token store) via `oauth/register`, public client, no clientSecret.

**Findings**:
- Package `@microsoft/m365agentstoolkit-cli` (npm latest 1.1.18, bin `atk`); bundles yaml schema v1.0.0-v1.13. A provision lifecycle with ONLY `oauth/register` loads and runs: no appPackage, no teamsApp/create, no env file, no projectId needed. Verified by running 1.1.18 locally: it reached the driver and failed only at M365 token acquisition. The CLI auto-appends `projectId: <uuid>` to m365agents.yml and auto-creates `env/.env.<name>` (writes `TEAMSFX_ENV`, `APP_NAME_SUFFIX`).
- `oauth/register` is NOT in the coordinator's M365Actions list, so there is no pre-sign-in tenant check; the driver fetches a Teams Graph token itself. Sign in first with `atk auth login m365 --tenant <tid>`.
- Driver `validateArgs`: `appId` must be a non-empty string (any GUID works; for AnyApp it sends `m365AppId: ""`, never used). `clientSecret` is optional; with `isPKCEEnabled: true` the secret is never prompted, never read from env, sent as `""`. `apiSpecPath` NOT needed if baseUrl + authorizationUrl + tokenUrl are given. `scope` is split on COMMA (`parseScopes`), not space. `targetAudience` code default is AnyTenant (schema comment saying HomeTenant is stale) - set explicitly.
- Idempotency: keyed ONLY on `configurationId` in the env file (not on name). Present + found = skip; present + deleted = warn and skip; absent = POST /v1.0/oAuthConfigurations -> duplicate if the env file is lost (server-side duplicate-name behaviour unknown). Result key = the `writeToEnvironmentFile.configurationId` var name.
- Docs: Learn plugin-authentication-oauth (2026-08-28) says keep appId because the driver validates it unconditionally (confirmed in source).

**Sources**:
- https://github.com/OfficeDev/microsoft-365-agents-toolkit : packages/fx-core/src/component/driver/oauth/{create.ts,utility/utility.ts}, question/other.ts (oauthQuestion), component/coordinator/index.ts, component/utils/{envUtil,settingsUtil,pathUtils}.ts, resource/yaml-schema/v1.11
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-authentication-oauth
- https://learn.microsoft.com/en-us/microsoftteams/platform/toolkit/microsoft-365-agents-toolkit-cli

**Open questions**: live token acquisition + whether the Teams Graph (TGS) POST accepts the comma-split scope array as expected and whether the signing-in account needs any Teams/dev-portal entitlement; whether TGS rejects duplicate descriptions.
