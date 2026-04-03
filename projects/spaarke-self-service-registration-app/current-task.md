# Current Task State — Self-Service Registration App

## Active Task
**Task**: 042 - End-to-End Testing and Verification
**Status**: not-started
**Next Action**: Test the approve flow — call POST /api/registration/requests/{id}/approve with admin token

## Quick Recovery

### What's Done (12 of 15 tasks)
- All code written and compiled (tasks 001-031, 040)
- BFF API deployed to dev with registration endpoints working
- Submit endpoint tested: POST /api/registration/demo-request returns 202 with tracking ID
- Test record created in Dataverse: REG-20260403-XMG9 (Test User - Contoso Ltd)
- Dataverse table `sprk_registrationrequest` created in dev with 22 columns
- License SKU IDs discovered (Power Apps, Fabric, Power Automate)
- Entra security group "Spaarke Demo Users" created (745bfdf6-f899-4507-935d-c52de3621536)
- Conditional Access policy created (MFA exclusion for demo group)
- Graph Application permissions added to dev BFF app reg (User.ReadWrite.All, GroupMember.ReadWrite.All, Directory.ReadWrite.All) + admin consent granted
- BFF appsettings configured on App Service (Dev + Demo environments, SKU IDs, group ID)
- Demo BFF app registration manifest updated (scopes, roles, permissions)
- Master merged into branch

### What's NOT Done
1. **Test approve flow** — need admin bearer token to call POST /api/registration/requests/{id}/approve
2. **DemoExpirationService** — temporarily disabled in DI (was crashing app on startup). Need to debug the GraphUserService dependency chain.
3. **Ribbon buttons** — JS webresource created but not deployed to Dataverse
4. **Website form** — created in spaarke-website repo but not deployed
5. **Task 050** — project wrap-up

### Known Issues
- DemoExpirationService commented out in RegistrationModule.cs — GraphUserService singleton resolution fails when hosted service starts. Likely a constructor dependency chain issue (GraphClientFactory → GraphTokenCache timing).
- Deploy script `Deploy-BffApi.ps1` uses `deploy/api-publish/` path which can get stale. Must clean bin/obj before publish.
- NEVER use `az webapp deploy --clean true` — wipes separately deployed assets (SPAs, static files).

### Key IDs and Config
- Dev Dataverse: https://spaarkedev1.crm.dynamics.com
- Demo Dataverse: https://spaarke-demo.crm.dynamics.com
- BFF API (dev): https://spe-api-dev-67e2xz.azurewebsites.net
- Dev BFF App ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
- Demo BFF App ID: da03fe1a-4b1d-4297-a4ce-4b83cae498a9
- Demo Users Group ID: 745bfdf6-f899-4507-935d-c52de3621536
- Demo BU ID (dev): 9271b764-952f-f111-88b5-7c1e520aa4df
- Demo Team ID (dev): 9471b764-952f-f111-88b5-7c1e520aa4df (Owner team, type 0)
- Power Apps Trial SKU: dcb1a3ae-b33f-4487-846a-a640262fadf4
- Fabric Free SKU: a403ebcc-fae0-4ca2-8c8c-7a907fd6c235
- Power Automate Free SKU: f30db892-07e9-47e9-837c-80727f46fd3d
- Test record tracking ID: REG-20260403-XMG9

## Files Modified This Session
- src/server/api/Sprk.Bff.Api/Models/Registration/ (6 files)
- src/server/api/Sprk.Bff.Api/Configuration/DemoProvisioningOptions.cs
- src/server/api/Sprk.Bff.Api/Infrastructure/DI/RegistrationModule.cs
- src/server/api/Sprk.Bff.Api/Services/Registration/ (10 files)
- src/server/api/Sprk.Bff.Api/Endpoints/RegistrationEndpoints.cs
- src/server/api/Sprk.Bff.Api/Endpoints/Filters/RegistrationAuthorizationFilter.cs
- src/server/api/Sprk.Bff.Api/Program.cs (added AddRegistrationModule)
- src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs (added MapRegistrationEndpoints)
- src/server/api/Sprk.Bff.Api/appsettings.template.json (added DemoProvisioning section)
- scripts/Setup-EntraInfrastructure.ps1
- scripts/Get-LicenseSkuIds.ps1
- scripts/Create-RegistrationRequestSchema.ps1
- src/client/webresources/js/sprk_registrationribbon.js
- src/solutions/DemoRegistration/ (schema-definition.md, ribbon-definition.md)
- spaarke-website repo: app/demo/page.tsx, components/DemoRequestForm.tsx, app/api/registration/demo-request/route.ts
