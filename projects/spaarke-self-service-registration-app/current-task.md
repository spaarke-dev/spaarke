# Current Task State — Self-Service Registration App

## Active Task
**Task**: Remaining polish and deployment
**Status**: in-progress
**Next Action**: Code changes for email CCs, acknowledgement email, and environment selection

## Quick Recovery

### What's DONE and WORKING (as of 2026-04-06)
- Full end-to-end provisioning works: form submit → approve via ribbon → Entra user + licenses + Dataverse team + welcome email
- Ribbon buttons working on BOTH form and grid view in dev MDA
- Website form created and pushed to spaarke-website repo
- All documentation created (ops guide, Azure setup guide, pattern file)
- BFF API deployed to dev
- Entity created in both dev and demo Dataverse
- Merged to master

### Remaining Code Changes

**1. Acknowledgement email on submission**
- When user submits the form, send an acknowledgement email to their work email
- "Your request has been received and will be processed shortly"
- CC: demo@demo.spaarke.com
- Change in: RegistrationEndpoints.cs (submit handler) + RegistrationEmailService.cs (new method + template)
- Need new template: AcknowledgementTemplate.html

**2. CC demo@demo.spaarke.com on welcome email**
- Add CC/BCC to the welcome email so demo@demo.spaarke.com has a record
- Change in: RegistrationEmailService.cs (SendWelcomeEmailAsync)
- Check if CommunicationService supports CC/BCC, or send a separate copy

**3. Admin environment selection on approve**
- Add optional `environment` parameter to POST /api/registration/requests/{id}/approve
- If not specified, use DefaultEnvironment from config
- Admin can pass `{ "environment": "Demo 1" }` in the request body
- Also update the ribbon JS to show environment picker (or default to config)
- Changes in: RegistrationEndpoints.cs, ribbon JS, possibly a new DTO

### Remaining Deployment

**4. Deploy ribbon to Demo Dataverse**
- Upload sprk_/js/registrationribbon.js webresource to demo
- Import RegistrationRequestRibbons solution to demo
- Create views, form, sitemap in Demo MDA

**5. Website activation**
- Set BFF_API_URL env var on website hosting
- Set RECAPTCHA_SITE_KEY and RECAPTCHA_SECRET_KEY (may already exist)
- Redeploy website

**6. Switch to Demo environment**
- Change DemoProvisioning__DefaultEnvironment from "Dev" to "Demo 1"
- Test end-to-end against demo

**7. Documentation updates**
- Update ribbon-edit skill with CrmParameter reference table
- Update ribbon guide with lessons learned
- Add CrmParameter reference doc
- Add ribbon-customization pattern pointer

### Key Lessons from Ribbon Work
- NEVER use `SelectedItemReferences` — correct value is `SelectedControlSelectedItemReferences`
- Grid buttons: use `SelectedControl` CrmParameter + `SelectionCountRule` enable rules
- Grid JS functions receive grid context, extract via `gridControl.getGrid().getSelectedRows()`
- Invalid CrmParameter values corrupt entity ribbon — only fix is delete/recreate entity
- Always validate CrmParameter values against Microsoft enum before import
- Use dedicated small solution for ribbon work, never import full SpaarkeCore

### Key IDs
- Dev Dataverse: https://spaarkedev1.crm.dynamics.com
- Demo Dataverse: https://spaarke-demo.crm.dynamics.com
- BFF API: https://spe-api-dev-67e2xz.azurewebsites.net
- Dev BFF App ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
- Demo BFF App ID: da03fe1a-4b1d-4297-a4ce-4b83cae498a9
- Demo Users Group: 745bfdf6-f899-4507-935d-c52de3621536
- Power Apps Trial SKU: dcb1a3ae-b33f-4487-846a-a640262fadf4
- Fabric Free SKU: a403ebcc-fae0-4ca2-8c8c-7a907fd6c235
- Power Automate Free SKU: f30db892-07e9-47e9-837c-80727f46fd3d
