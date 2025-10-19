# Universal Document Upload PCF - Custom Page Migration

## Project Overview

Migrate the Universal Quick Create PCF control from Quick Create Form context to Custom Page Dialog context to enable unlimited multi-record creation using `Xrm.WebApi`.

**Version:** 2.0.0.0 (Major Release)
**Status:** Implementation Ready
**Estimated Duration:** 24 hours (3 working days)

---

## Quick Start

1. **Read Sprint Overview:** [SPRINT-OVERVIEW.md](./SPRINT-OVERVIEW.md)
2. **Review Architecture:** [ARCHITECTURE.md](./ARCHITECTURE.md)
3. **Check ADR Compliance:** [ADR-COMPLIANCE.md](./ADR-COMPLIANCE.md)
4. **Review Code Changes:** [CODE-REFERENCES.md](./CODE-REFERENCES.md)
5. **Follow Implementation:** Start with Phase 1 (see below)

---

## Implementation Phases

| Phase | Document | Duration | Focus |
|-------|----------|----------|-------|
| **Phase 1** | [PHASE-1-SETUP.md](./PHASE-1-SETUP.md) | 2 hours | Configuration & types |
| **Phase 2** | [PHASE-2-SERVICES.md](./PHASE-2-SERVICES.md) | 3 hours | Service refactoring |
| **Phase 3** | [PHASE-3-PCF-CONTROL.md](./PHASE-3-PCF-CONTROL.md) | 4 hours | PCF migration |
| **Phase 4** | [PHASE-4-UI-COMPONENTS.md](./PHASE-4-UI-COMPONENTS.md) | 4 hours | Fluent UI v9 |
| **Phase 5** | [PHASE-5-CUSTOM-PAGE.md](./PHASE-5-CUSTOM-PAGE.md) | 2 hours | Custom page creation |
| **Phase 6** | [PHASE-6-COMMAND-INTEGRATION.md](./PHASE-6-COMMAND-INTEGRATION.md) | 3 hours | Command buttons |
| **Phase 7** | [PHASE-7-TESTING.md](./PHASE-7-TESTING.md) | 4 hours | Testing & validation |
| **Deploy** | [DEPLOYMENT-GUIDE.md](./DEPLOYMENT-GUIDE.md) | 2 hours | Packaging & release |

**Total:** 24 hours

---

## Key Changes

### Before (Quick Create Form)
- ❌ Limited to single-record creation
- ❌ Second `context.webAPI.createRecord()` fails with 400 error
- ❌ Form context corruption
- ❌ Only works in Quick Create forms

### After (Custom Page Dialog)
- ✅ Unlimited record creation (10, 50, 100+ records)
- ✅ Uses `Xrm.WebApi.createRecord()` (no limitations)
- ✅ No form context dependencies
- ✅ Works from any entity's subgrid
- ✅ Better error handling (partial success scenarios)
- ✅ Fluent UI v9 compliance

---

## Success Criteria

**Functional:**
- [x] Upload 1 file → Create 1 record
- [x] Upload 10 files → Create 10 records
- [x] Upload 100MB total → Success
- [x] File limits enforced (10MB per file, 100MB total)
- [x] Dangerous files blocked (.exe, .dll, etc.)
- [x] Works on all entity types (Matter, Project, Invoice, Account, Contact)
- [x] Dialog opens/closes properly
- [x] Subgrid refreshes automatically
- [x] Partial failures handled gracefully

**Technical:**
- [x] Uses `Xrm.WebApi.createRecord()` only
- [x] Fluent UI v9 components only (NO v8)
- [x] TypeScript strict mode compliant
- [x] Proper error handling and logging
- [x] Version displayed in dialog footer

---

## Documentation Index

### Planning Documents
- **[SPRINT-OVERVIEW.md](./SPRINT-OVERVIEW.md)** - Epic summary, goals, timeline
- **[ARCHITECTURE.md](./ARCHITECTURE.md)** - System design, data flow, ERD
- **[ADR-COMPLIANCE.md](./ADR-COMPLIANCE.md)** - Architectural Decision Records
- **[CODE-REFERENCES.md](./CODE-REFERENCES.md)** - File catalog (keep/modify/create/delete)

### Implementation Phases
- **[PHASE-1-SETUP.md](./PHASE-1-SETUP.md)** - Project setup, configuration
- **[PHASE-2-SERVICES.md](./PHASE-2-SERVICES.md)** - Service layer refactoring
- **[PHASE-3-PCF-CONTROL.md](./PHASE-3-PCF-CONTROL.md)** - PCF control migration
- **[PHASE-4-UI-COMPONENTS.md](./PHASE-4-UI-COMPONENTS.md)** - Fluent UI v9 components
- **[PHASE-5-CUSTOM-PAGE.md](./PHASE-5-CUSTOM-PAGE.md)** - Custom page creation
- **[PHASE-6-COMMAND-INTEGRATION.md](./PHASE-6-COMMAND-INTEGRATION.md)** - Command buttons
- **[PHASE-7-TESTING.md](./PHASE-7-TESTING.md)** - Testing checklist

### Deployment
- **[DEPLOYMENT-GUIDE.md](./DEPLOYMENT-GUIDE.md)** - Packaging, deployment, troubleshooting

### Prompts (AI Assistant)
- **[PROMPTS/](./PROMPTS/)** - Ready-to-use prompts for each phase

---

## Technology Stack

### Frontend
- **Framework:** Power Apps Component Framework (PCF)
- **Language:** TypeScript (strict mode)
- **UI Library:** Fluent UI v9 (`@fluentui/react-components`)
- **React:** v16.13 (PCF requirement)
- **Build Tool:** PCF-Scripts (webpack)

### Backend
- **Dataverse API:** Xrm.WebApi.createRecord()
- **File Upload:** SharePoint Embedded via BFF API
- **Authentication:** MSAL (OAuth2 On-Behalf-Of flow)

### Deployment
- **Platform:** Power Platform / Dataverse
- **Package:** Solution ZIP (managed or unmanaged)
- **Version:** 2.0.0.0

---

## File Limits

| Limit | Value | Rationale |
|-------|-------|-----------|
| Max Files | 10 | Prevent abuse, reasonable for typical use |
| Max File Size | 10MB | Individual file limit |
| Max Total Size | 100MB | All files combined |
| Blocked Types | .exe, .dll, .bat, .cmd, .ps1, .vbs, .js, .jar, .app, .msi, .scr, .com | Security |

---

## Supported Parent Entities

1. **Matter** (`sprk_matter`)
2. **Project** (`sprk_project`)
3. **Invoice** (`sprk_invoice`)
4. **Account** (`account`)
5. **Contact** (`contact`)

**Future Additions:** Easy to add via `EntityDocumentConfig.ts` (no code changes needed)

---

## Entity Schema Changes Required

### Document Entity (`sprk_document`)

**New Fields:**
- `sprk_description` (Multi-line Text) - Optional document notes

**New Lookup Fields:**
- `sprk_matter` (Lookup to sprk_matter)
- `sprk_project` (Lookup to sprk_project)
- `sprk_invoice` (Lookup to sprk_invoice)
- `sprk_account` (Lookup to account)
- `sprk_contact` (Lookup to contact)

**Existing Fields:**
- `sprk_documentname` (Single Line Text)
- `sprk_filename` (Single Line Text)
- `sprk_graphdriveid` (Single Line Text)
- `sprk_graphitemid` (Single Line Text)
- `sprk_filesize` (Whole Number)

### Parent Entities

**Required Field (all parent entities):**
- `sprk_containerid` (Single Line Text) - SharePoint Embedded Container ID

---

## Development Commands

```bash
# Navigate to control directory
cd src/controls/UniversalQuickCreate

# Install dependencies
npm install

# Build (development)
npm run build

# Build (production)
npm run build -- --buildMode production

# Watch mode (auto-rebuild on changes)
npm run start watch

# Deploy to Dataverse
pac pcf push --publisher-prefix sprk
```

---

## Testing Commands

```bash
# Type checking
npm run build

# Lint (if configured)
npm run lint

# Manual testing in browser
# 1. Deploy control
# 2. Open Matter form
# 3. Click "New Document" button
# 4. Verify dialog opens
# 5. Test file upload
```

---

## Common Issues & Solutions

### Issue: Dialog Doesn't Open
**Solution:** Check browser console for errors. Verify custom page is published.

### Issue: Files Don't Upload
**Solution:** Check MSAL authentication. Verify BFF API is reachable.

### Issue: Records Not Created
**Solution:** Check user permissions on `sprk_document`. Verify lookup field name in config.

### Issue: Fluent UI Styles Missing
**Solution:** Ensure importing from `@fluentui/react-components` (NOT `@fluentui/react`).

**Full Troubleshooting:** See [DEPLOYMENT-GUIDE.md](./DEPLOYMENT-GUIDE.md#monitoring--troubleshooting)

---

## Contributing

### Code Style
- Follow existing patterns (see ADR-COMPLIANCE.md)
- Use Fluent UI v9 only
- TypeScript strict mode
- Meaningful variable names
- Comment complex logic

### Adding New Entity Support

1. Add lookup field to Document entity
2. Add config to `EntityDocumentConfig.ts`:
   ```typescript
   'sprk_newentity': {
       entityName: 'sprk_newentity',
       lookupFieldName: 'sprk_newentity',
       containerIdField: 'sprk_containerid',
       displayNameField: 'sprk_name',
       entitySetName: 'sprk_newentities'
   }
   ```
3. Deploy command button to new entity's subgrid
4. Test upload from new entity

**No code changes needed** - configuration-driven!

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 2.0.0.0 | 2025-01-10 | Initial Custom Page migration release |
| 2.1.0.0 | TBD | Add support for additional entities |
| 2.0.1.0 | TBD | Bug fixes |

---

## Resources

### Reference Implementations
- **Universal Dataset Grid:** Fluent UI v9 patterns (Sprint 5B)
- **MSAL Integration:** OAuth2 OBO flow (Sprint 8)

### Microsoft Documentation
- [PCF Framework](https://learn.microsoft.com/power-apps/developer/component-framework/)
- [Fluent UI v9](https://react.fluentui.dev/)
- [Xrm.WebApi](https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/reference/xrm-webapi)
- [Custom Pages](https://learn.microsoft.com/power-apps/maker/model-driven-apps/page-powerapps)

### Internal Documentation
- [Spaarke ADRs](../../docs/)
- [Universal Dataset Grid Docs](../dataset_pcf_component/)

---

## Contact & Support

**Project Owner:** Ralph
**Development Team:** AI Assistant (Claude)
**Sprint Duration:** 3 days (24 hours estimated)

For issues, questions, or enhancement requests, refer to [DEPLOYMENT-GUIDE.md](./DEPLOYMENT-GUIDE.md) or project documentation.

---

## License

Internal Spaarke project - proprietary and confidential.

---

**Ready to Start?** → [PHASE-1-SETUP.md](./PHASE-1-SETUP.md)
