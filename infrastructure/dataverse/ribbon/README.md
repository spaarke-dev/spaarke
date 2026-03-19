# Dataverse Ribbon Customizations

This folder contains all Ribbon XML customizations for Spaarke entity forms and grids.

## Folder Structure

```
ribbon/
├── README.md                          # This file
├── sprk_ThemeMenuRibbon.xml           # Application-level theme menu ribbon
├── AnalysisRibbons/                   # sprk_analysis entity ribbons
│   ├── Entities/sprk_analysis/
│   │   ├── Entity.xml
│   │   └── RibbonDiff.xml
│   └── Other/
│       ├── Customizations.xml
│       └── Solution.xml
├── CommunicationRibbons/              # sprk_communication entity ribbons
│   ├── Entities/sprk_communication/
│   ├── Other/
│   └── [Content_Types].xml
├── DocumentRibbons/                   # sprk_document entity ribbons
│   ├── Entities/sprk_Document/
│   │   ├── Entity.xml
│   │   └── RibbonDiff.xml             # Check in/out, delete, download, send to index
│   ├── Other/
│   ├── customizations.xml
│   ├── solution.xml
│   └── [Content_Types].xml
├── EmailRibbons/                      # email entity ribbons
│   ├── Entities/email/
│   └── Other/
├── EventRibbons/                      # sprk_event entity ribbons
│   ├── README.md                      # Event-specific documentation
│   ├── RibbonDiffXml_sprk_event.xml   # Update Related button
│   └── customizations.xml             # Wizard launcher buttons (UDSS-043)
├── MatterRibbons/                     # sprk_matter entity ribbons
│   ├── Entities/sprk_Matter/
│   │   ├── Entity.xml
│   │   └── RibbonDiff.xml             # Theme menu flyout
│   ├── Other/
│   │   ├── Customizations.xml
│   │   └── Solution.xml
│   └── customizations.xml             # Wizard launcher buttons (UDSS-041)
├── ProjectRibbons/                    # sprk_project entity ribbons
│   └── customizations.xml             # Wizard launcher buttons (UDSS-042)
└── ThemeMenuRibbons/                  # Theme menu solution package
    └── Other/
```

## Wizard Ribbon Buttons (UDSS-041/042/043)

The `customizations.xml` files in MatterRibbons, ProjectRibbons, and EventRibbons add wizard launcher buttons to entity form command bars. All buttons call functions from `sprk_wizard_commands.js`.

### Button Inventory

| Entity | Button | Function | Sequence |
|--------|--------|----------|----------|
| **Matter** | Create Project | `openCreateProjectWizard` | 200 |
| **Matter** | Create Event | `openCreateEventWizard` | 210 |
| **Matter** | Create To Do | `openCreateTodoWizard` | 220 |
| **Matter** | Upload Documents | `openDocumentUploadWizard` | 230 |
| **Matter** | Summarize Files | `openSummarizeFilesWizard` | 240 |
| **Matter** | Find Similar | `openFindSimilarDialog` | 250 |
| **Matter** | Playbook Library | `openPlaybookLibrary` | 260 |
| **Project** | Upload Documents | `openDocumentUploadWizard` | 200 |
| **Project** | Summarize Files | `openSummarizeFilesWizard` | 210 |
| **Project** | Find Similar | `openFindSimilarDialog` | 220 |
| **Project** | Playbook Library | `openPlaybookLibrary` | 230 |
| **Event** | Upload Documents | `openDocumentUploadWizard` | 200 |
| **Event** | Summarize Files | `openSummarizeFilesWizard` | 210 |
| **Event** | Find Similar | `openFindSimilarDialog` | 220 |
| **Event** | Playbook Library | `openPlaybookLibrary` | 230 |

### Prerequisites

| Web Resource | Description |
|-------------|-------------|
| `sprk_wizard_commands.js` | Wizard launcher JavaScript - contains all `Spaarke.Commands.Wizards.*` functions |

### Display Rules

All wizard buttons use these rules:
- **DisplayRule**: `FormStateRule State="Existing"` -- buttons only appear on saved records (not new/create forms)
- **EnableRule**: `FormStateRule State="Existing"` -- buttons are enabled only on existing records

### Naming Convention

All wizard ribbon IDs follow the pattern:
```
sprk.Wizard.{Entity}.{Action}.{Type}
```

Examples:
- `sprk.Wizard.Matter.CreateProject.CustomAction`
- `sprk.Wizard.Matter.CreateProject.Button`
- `sprk.Wizard.Matter.CreateProject.Command`
- `sprk.Wizard.Matter.Form.DisplayRule`
- `sprk.Wizard.Matter.Form.EnableRule`

## Theme Menu Ribbon

The Theme Menu adds a flyout submenu to the command bar allowing users to switch between Auto, Light, and Dark themes.

### Theme Menu Prerequisites

| Web Resource | Source File | Description |
|-------------|-------------|-------------|
| `sprk_ThemeMenu.js` | `src/client/webresources/js/sprk_ThemeMenu.js` | Theme menu JavaScript handler |
| `sprk_ThemeMenu16.svg` | `src/client/assets/icons/sprk_ThemeMenu16.svg` | Menu icon (16x16) |
| `sprk_ThemeMenu32.svg` | `src/client/assets/icons/sprk_ThemeMenu32.svg` | Menu icon (32x32) |
| `sprk_ThemeAuto16.svg` | `src/client/assets/icons/sprk_ThemeAuto16.svg` | Auto option icon |
| `sprk_ThemeLight16.svg` | `src/client/assets/icons/sprk_ThemeLight16.svg` | Light option icon |
| `sprk_ThemeDark16.svg` | `src/client/assets/icons/sprk_ThemeDark16.svg` | Dark option icon |

## Deployment

### Using Ribbon Workbench (Recommended)

1. Open **XrmToolBox** and connect to your Dataverse environment
2. Launch **Ribbon Workbench**
3. Load the target solution (e.g., SpaarkeCore)
4. Select the target entity from the entity dropdown
5. Import the RibbonDiffXml from the appropriate `customizations.xml`
6. **Publish** the customizations

### Using Solution Import

1. Package the ribbon folder contents as an unmanaged solution ZIP
2. Import via **Settings > Solutions > Import**
3. Publish all customizations

### Using ribbon-edit Skill

```bash
/ribbon-edit
```

The `ribbon-edit` skill automates solution export, XML modification, and re-import. See `.claude/skills/ribbon-edit/SKILL.md` for details.

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Buttons not appearing | Verify `sprk_wizard_commands.js` web resource is published |
| Buttons appear on new record forms | Check DisplayRule uses `FormStateRule State="Existing"` |
| JavaScript error on click | Check browser console; verify web resource name matches |
| Button ordering wrong | Adjust Sequence values in CustomAction elements |
| Conflicts with existing buttons | Ensure Sequence values do not overlap with OOTB buttons |

## Related Files

- Wizard commands JS: `src/client/webresources/js/sprk_wizard_commands.js`
- Theme Menu JS: `src/client/webresources/js/sprk_ThemeMenu.js`
- SVG Icons: `src/client/assets/icons/`
- Ribbon Edit Skill: `.claude/skills/ribbon-edit/SKILL.md`
