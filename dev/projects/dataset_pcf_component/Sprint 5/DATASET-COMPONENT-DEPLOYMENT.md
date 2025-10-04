# Dataset Component Deployment Guide

## Build Configuration
### Package.json Setup
```json
{
  "name": "spaarke-universal-dataset",
  "version": "1.0.0",
  "scripts": {
    "build": "pcf-scripts build",
    "build:prod": "pcf-scripts build --production",
    "start": "pcf-scripts start watch",
    "test": "jest",
    "test:coverage": "jest --coverage",
    "lint": "eslint src/**/*.{ts,tsx}",
    "type-check": "tsc --noEmit"
  },
  "devDependencies": {
    "pcf-scripts": "^1.31.0",
    "pcf-start": "^1.31.0",
    "@types/react": "^18.2.0",
    "@types/react-dom": "^18.2.7",
    "typescript": "^5.4.0",
    "jest": "^29.7.0",
    "@testing-library/react": "^14.2.1"
  },
  "dependencies": {
    "@fluentui/react-components": "^9.46.0",
    "@fluentui/react-icons": "^2.0.0",
    "@tanstack/react-virtual": "^3.0.0",
    "react": "^18.2.0",
    "react-dom": "^18.2.0"
  }
}
```

## CI/CD Pipeline
### Azure DevOps Pipeline
```yaml
# azure-pipelines.yml
trigger:
  branches:
    include: [ main, develop ]
  paths:
    include: [ packages/dataset-component/** ]

pool:
  vmImage: 'windows-latest'

variables:
  - group: 'Spaarke-PCF-Variables'

stages:
- stage: Build
  jobs:
  - job: BuildPCF
    steps:
    - task: NodeTool@0
      inputs: { versionSpec: '18.x' }

    - task: PowerPlatformToolInstaller@2
      inputs: { DefaultVersion: true }

    - script: |
        npm ci
        npm run lint
        npm run type-check
      displayName: 'Lint and Type Check'

    - script: npm run test:coverage
      displayName: 'Run Tests'

    - task: PublishCodeCoverageResults@1
      inputs:
        codeCoverageTool: 'Cobertura'
        summaryFileLocation: 'coverage/cobertura-coverage.xml'

    - script: npm run build:prod
      displayName: 'Build PCF Component'

    - task: PowerPlatformPackSolution@2
      inputs:
        SolutionSourceFolder: 'packages/dataset-component'
        SolutionOutputFile: '$(Build.ArtifactStagingDirectory)/UniversalDataset.zip'
        SolutionType: 'Both'

    - publish: $(Build.ArtifactStagingDirectory)
      artifact: PCFComponent

- stage: DeployDev
  dependsOn: Build
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/develop'))
  jobs:
  - deployment: DeployToDev
    environment: 'Development'
    strategy:
      runOnce:
        deploy:
          steps:
          - task: PowerPlatformImport@2
            inputs:
              authenticationType: 'PowerPlatformSPN'
              PowerPlatformSPN: 'SpaarkeDevSPN'
              SolutionInputFile: '$(Pipeline.Workspace)/PCFComponent/UniversalDataset.zip'
              AsyncOperation: true
              MaxAsyncWaitTime: 60

- stage: DeployProd
  dependsOn: Build
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
  jobs:
  - deployment: DeployToProd
    environment: 'Production'
    strategy:
      runOnce:
        deploy:
          steps:
          - task: PowerPlatformImport@2
            inputs:
              authenticationType: 'PowerPlatformSPN'
              PowerPlatformSPN: 'SpaarkeProdSPN'
              SolutionInputFile: '$(Pipeline.Workspace)/PCFComponent/UniversalDataset.zip'
              AsyncOperation: true
              MaxAsyncWaitTime: 60
```

## Solution Structure
### Solution Components
```xml
<!-- solution.xml -->
<ImportExportXml version="9.2.0.0">
  <SolutionManifest>
    <UniqueName>SpaarkeUniversalDataset</UniqueName>
    <LocalizedNames>
      <LocalizedName languagecode="1033">Spaarke Universal Dataset</LocalizedName>
    </LocalizedNames>
    <Version>1.0.0.0</Version>
    <Publisher>
      <UniqueName>Spaarke</UniqueName>
      <LocalizedNames>
        <LocalizedName languagecode="1033">Spaarke</LocalizedName>
      </LocalizedNames>
    </Publisher>
  </SolutionManifest>
  <Components>
    <CustomControl>
      <Name>Spaarke.UniversalDataset</Name>
      <FileName>/Controls/UniversalDataset/bundle.js</FileName>
    </CustomControl>
    <WebResource>
      <Name>sprk_UniversalDataset.css</Name>
      <FileName>/Controls/UniversalDataset/css/UniversalDataset.css</FileName>
    </WebResource>
  </Components>
</ImportExportXml>
```

## Deployment Steps
### Local Development
```bash
# 1. Clone repository
git clone https://github.com/spaarke/universal-dataset.git
cd universal-dataset

# 2. Install dependencies
npm install

# 3. Start local test harness
npm start watch   # http://localhost:8181

# 4. Run tests
npm test

# 5. Build for deployment
npm run build:prod
```

### Manual Deployment
```powershell
# deploy.ps1
param(
  [Parameter(Mandatory=$true)][string]$Environment,
  [Parameter(Mandatory=$true)][string]$SolutionPath
)

pac auth create --url "https://$Environment.crm.dynamics.com"
pac solution import --path $SolutionPath --force-overwrite --publish-changes --async
pac solution publish
Write-Host "Deployment complete to $Environment"
```

## Configuration Management
### Environment Variables
```typescript
// Environment variable definitions
const environmentVariables = {
  "sprk_dataset_api_endpoint": {
    "schemaname": "sprk_dataset_api_endpoint",
    "displayname": "Dataset API Endpoint",
    "type": "String",
    "defaultvalue": "https://api.spaarke.com/v1"
  },
  "sprk_dataset_enable_virtual_scroll": {
    "schemaname": "sprk_dataset_enable_virtual_scroll",
    "displayname": "Enable Virtual Scrolling",
    "type": "Boolean",
    "defaultvalue": "true"
  },
  "sprk_dataset_default_page_size": {
    "schemaname": "sprk_dataset_default_page_size",
    "displayname": "Default Page Size",
    "type": "Number",
    "defaultvalue": "25"
  }
};
```

### Configuration Entity
```typescript
// Dataverse configuration entity (example)
interface IDatasetConfiguration {
  sprk_configurationid: string;
  sprk_name: string;
  sprk_entity_name: string;
  sprk_view_mode: "Grid" | "Card" | "List";
  sprk_column_config: string; // JSON
  sprk_command_config: string; // JSON
  sprk_filter_config: string; // JSON
}
```

## Form Integration
### Adding to Model-Driven Form
```xml
<form>
  <tabs>
    <tab name="documents_tab">
      <sections>
        <section name="documents_section">
          <rows>
            <row>
              <cell>
                <control id="documents_grid"
                         classid="{UNIVERSAL-DATASET-GUID}"
                         datafieldname="sprk_matter_document_relationship">
                  <parameters>
                    <viewMode>Grid</viewMode>
                    <density>Standard</density>
                    <enabledCommands>open,create,upload,delete</enabledCommands>
                    <showToolbar>true</showToolbar>
                    <pageSize>50</pageSize>
                  </parameters>
                </control>
              </cell>
            </row>
          </rows>
        </section>
      </sections>
    </tab>
  </tabs>
</form>
```

## Monitoring and Telemetry
### Application Insights Integration
```typescript
// telemetry/AppInsights.ts
export class TelemetryService {
  private appInsights: any;
  constructor(instrumentationKey: string) {
    this.appInsights = new (window as any).ApplicationInsights({
      config: { instrumentationKey, enableAutoRouteTracking: true }
    });
    this.appInsights.loadAppInsights();
  }
  trackComponentLoad(entityName: string, viewMode: string): void {
    this.appInsights.trackEvent({
      name: "DatasetComponent.Load",
      properties: { entityName, viewMode, timestamp: new Date().toISOString() }
    });
  }
  trackPerformance(metric: string, duration: number): void {
    this.appInsights.trackMetric({ name: `DatasetComponent.${metric}`, average: duration });
  }
  trackError(error: Error, context?: any): void {
    this.appInsights.trackException({ exception: error, properties: context });
  }
}
```

## Rollback Procedures
### Version Management
```bash
pac solution list --environment-url https://org.crm.dynamics.com
pac solution export --name SpaarkeUniversalDataset --path ./backups/solution_backup_$(date +%Y%m%d).zip --managed false
# Rollback
pac solution import --path ./backups/solution_backup_20240101.zip --force-overwrite
```

## Post-Deployment Validation
### Automated Tests
```typescript
// post-deploy-tests.ts
describe("Post Deployment Validation", () => {
  test("Component loads on forms", async () => {
    const forms = ["sprk_document_main", "sprk_matter_main", "account_main"];
    for (const formId of forms) {
      const form = await openForm(formId);
      const component = form.querySelector("[data-control-name='UniversalDataset']");
      expect(component).toBeTruthy();
    }
  });
});
```

## Troubleshooting Guide
### Common Issues and Solutions
| Issue | Symptoms | Solution |
|-------|----------|----------|
| Component not loading | Blank space | Check console, verify solution import |
| Slow performance | Lag on scroll | Enable virtualization, reduce page size |
| Missing commands | Buttons absent | Verify `enabledCommands`, permissions |
| Theme not applied | Wrong styling | Wrap with `FluentProvider` |
| Data not refreshing | Stale rows | Check `dataset.refresh()` and cache |

## AI Coding Prompt
Set up build, deploy, and monitoring:
- `package.json` scripts (build, prod build, watch, lint, type-check, test, coverage).
- Azure DevOps pipeline: lint/type/test/coverage, build PCF, pack solution, deploy to Dev/Prod.
- Solution packaging with control + web resources; include a PAC CLI `deploy.ps1`.
- Environment variables and optional configuration entity.
- App Insights telemetry and post-deploy validation script.
Deliverables: pipeline YAML, solution stubs, script, telemetry service, and validation tests.
