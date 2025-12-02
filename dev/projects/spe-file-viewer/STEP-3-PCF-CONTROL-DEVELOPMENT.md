# Step 3: PCF Control Development

**Phase**: 3 of 5
**Duration**: ~3 hours
**Prerequisites**: Step 2 completed (Custom API registered)

---

## Overview

Create a PCF (PowerApps Component Framework) control using React and Fluent UI v9 to display SharePoint Embedded files in Document forms. The control calls the Custom API to get preview URLs and handles auto-refresh before expiration.

**Tech Stack**:
- PCF Framework (PowerApps Component Framework)
- React 17.x
- TypeScript 4.x
- Fluent UI v9 (React components)
- Xrm.WebApi (Custom API calls)

**Control Behavior**:
1. Receives Document record ID from form context
2. Calls Custom API `sprk_GetFilePreviewUrl` on mount
3. Displays file in iframe with preview URL
4. Shows loading spinner during fetch
5. Auto-refreshes preview URL 1 minute before expiration
6. Shows errors with Fluent UI MessageBar

---

## Task 3.1: Create PCF Project

**Goal**: Initialize PCF project with React template

**Repository Location**: `src/controls/SpeFileViewer/`
**Pattern**: Follows `UniversalQuickCreate` structure (see [REPOSITORY-STRUCTURE.md](REPOSITORY-STRUCTURE.md))

### Create Project Directory

```bash
# Navigate to controls directory
cd c:/code_files/spaarke/src/controls

# Create PCF project using PAC CLI
pac pcf init \
    --namespace Spaarke \
    --name SpeFileViewer \
    --template field \
    --framework react \
    --run-npm-install

# Navigate into project
cd SpeFileViewer
```

**Expected Output**:
```
Initializing PCF control...
✓ Created manifest file
✓ Created index.ts
✓ Created ControlManifest.Input.xml
✓ Running npm install...
✓ PCF control initialized successfully!
```

**Initial Project Structure**:
```
src/controls/SpeFileViewer/
├── ControlManifest.Input.xml
├── index.ts
├── package.json
├── tsconfig.json
├── .eslintrc.json
└── SpeFileViewer/
    └── (React components - to be organized)
```

**Final Target Structure** (after Task 3.4):
```
src/controls/SpeFileViewer/
├── SpeFileViewer/                  # PCF control code
│   ├── components/                 # React components
│   │   ├── FileViewer.tsx
│   │   ├── LoadingSpinner.tsx
│   │   └── ErrorMessage.tsx
│   ├── services/                   # Services
│   │   └── CustomApiService.ts
│   ├── types/                      # TypeScript types
│   │   └── types.ts
│   ├── generated/                  # PCF generated
│   ├── ControlManifest.Input.xml
│   └── index.ts
├── SpeFileViewerSolution/          # Dataverse solution (Task 3.5)
├── docs/                           # Documentation
├── package.json
├── tsconfig.json
└── SpeFileViewer.pcfproj
```

---

## Task 3.2: Install Dependencies

**Goal**: Install Fluent UI v9 and additional packages

### Install Fluent UI v9

```bash
npm install @fluentui/react-components @fluentui/react-icons
```

### Install Date Utilities

```bash
npm install date-fns
```

### Verify package.json

```json
{
  "dependencies": {
    "@fluentui/react": "^8.x",
    "@fluentui/react-components": "^9.54.0",
    "@fluentui/react-icons": "^2.0.258",
    "date-fns": "^3.0.0",
    "react": "^17.0.2",
    "react-dom": "^17.0.2"
  }
}
```

---

## Task 3.3: Update Manifest

**Goal**: Configure control properties and metadata

### File: ControlManifest.Input.xml

Replace entire contents:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke" constructor="SpeFileViewer" version="1.0.0" display-name-key="SpeFileViewer_Display_Key" description-key="SpeFileViewer_Desc_Key" control-type="standard">

    <!-- Property: Document ID (bound to sprk_document record) -->
    <property name="documentId" display-name-key="Document_ID_Key" description-key="Document_ID_Desc" of-type="SingleLine.Text" usage="bound" required="true" />

    <!-- Property: Height -->
    <property name="height" display-name-key="Height_Key" description-key="Height_Desc" of-type="Whole.None" usage="input" required="false" default-value="600" />

    <!-- Property: Show File Name -->
    <property name="showFileName" display-name-key="Show_FileName_Key" description-key="Show_FileName_Desc" of-type="TwoOptions" usage="input" required="false" default-value="true">
      <value name="true" display-name-key="Yes" default="true">true</value>
      <value name="false" display-name-key="No">false</value>
    </property>

    <!-- Resources -->
    <resources>
      <code path="index.ts" order="1"/>
      <platform-library name="React" version="17.0.2" />
      <platform-library name="Fluent" version="9.0.0" />
    </resources>

    <!-- Feature usage -->
    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
    </feature-usage>
  </control>
</manifest>
```

**Key Properties**:
- `documentId`: Bound to Document record ID field
- `height`: Configurable iframe height (default 600px)
- `showFileName`: Toggle file name display

---

## Task 3.4: Implement Control Logic

**Goal**: Create React components and Custom API service

### Organize Directory Structure

First, create the proper directory structure (following UniversalQuickCreate pattern):

```bash
# Navigate to the PCF control directory
cd c:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer

# Create subdirectories
mkdir components services types

# Verify structure
ls -la
```

### File Structure

Create these files in the `SpeFileViewer/` directory:

```
src/controls/SpeFileViewer/SpeFileViewer/
├── components/
│   ├── FileViewer.tsx       (Main component)
│   ├── LoadingSpinner.tsx   (Loading state)
│   └── ErrorMessage.tsx     (Error display)
├── services/
│   └── CustomApiService.ts  (Custom API calls)
├── types/
│   └── types.ts             (TypeScript interfaces)
├── App.tsx                  (Root component)
├── generated/               (PCF generated - already exists)
├── ControlManifest.Input.xml
└── index.ts
```

### File 1: types/types.ts

```typescript
/**
 * Custom API response from sprk_GetFilePreviewUrl
 */
export interface IFilePreviewResponse {
    PreviewUrl: string;
    FileName: string;
    FileSize: number;
    ContentType: string;
    ExpiresAt: string;  // ISO 8601 date string
    CorrelationId: string;
}

/**
 * Component state
 */
export interface IFileViewerState {
    loading: boolean;
    error: string | null;
    previewUrl: string | null;
    fileName: string | null;
    expiresAt: Date | null;
    correlationId: string | null;
}

/**
 * PCF context
 */
export interface IFileViewerProps {
    context: ComponentFramework.Context<IInputs>;
    documentId: string;
    height: number;
    showFileName: boolean;
}
```

### File 2: services/CustomApiService.ts

```typescript
import { IFilePreviewResponse } from '../types/types';

/**
 * Service for calling Dataverse Custom APIs
 */
export class CustomApiService {
    private context: ComponentFramework.Context<any>;

    constructor(context: ComponentFramework.Context<any>) {
        this.context = context;
    }

    /**
     * Call sprk_GetFilePreviewUrl Custom API
     * @param documentId Document entity ID
     * @returns Promise<IFilePreviewResponse>
     */
    public async getFilePreviewUrl(documentId: string): Promise<IFilePreviewResponse> {
        console.log('[CustomApiService] Getting preview URL for document:', documentId);

        try {
            const result = await this.context.webAPI.online.execute({
                getMetadata: () => ({
                    boundParameter: "entity",
                    parameterTypes: {
                        "entity": {
                            "typeName": "mscrm.sprk_document",
                            "structuralProperty": 5  // Entity
                        }
                    },
                    operationType: 1,  // Function
                    operationName: "sprk_GetFilePreviewUrl"
                }),
                entity: {
                    entityType: "sprk_document",
                    id: documentId
                }
            });

            console.log('[CustomApiService] Custom API response:', result);

            // Type assertion for response
            const response = result as unknown as IFilePreviewResponse;

            if (!response.PreviewUrl) {
                throw new Error('Custom API did not return a preview URL');
            }

            return response;
        } catch (error: any) {
            console.error('[CustomApiService] Custom API error:', error);
            throw new Error(`Failed to get preview URL: ${error.message}`);
        }
    }
}
```

### File 3: components/LoadingSpinner.tsx

```typescript
import * as React from 'react';
import { Spinner, SpinnerSize, Text, makeStyles } from '@fluentui/react-components';

const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        gap: '16px'
    }
});

export const LoadingSpinner: React.FC = () => {
    const styles = useStyles();

    return (
        <div className={styles.container}>
            <Spinner size="large" label="Loading file preview..." />
            <Text>Please wait while we load your file...</Text>
        </div>
    );
};
```

### File 4: components/ErrorMessage.tsx

```typescript
import * as React from 'react';
import { MessageBar, MessageBarBody, MessageBarTitle, makeStyles } from '@fluentui/react-components';
import { ErrorCircle24Regular } from '@fluentui/react-icons';

const useStyles = makeStyles({
    container: {
        padding: '16px'
    }
});

interface IErrorMessageProps {
    error: string;
    onRetry?: () => void;
}

export const ErrorMessage: React.FC<IErrorMessageProps> = ({ error, onRetry }) => {
    const styles = useStyles();

    return (
        <div className={styles.container}>
            <MessageBar intent="error" icon={<ErrorCircle24Regular />}>
                <MessageBarBody>
                    <MessageBarTitle>Unable to Load File Preview</MessageBarTitle>
                    {error}
                </MessageBarBody>
            </MessageBar>
            {onRetry && (
                <button onClick={onRetry} style={{ marginTop: '8px' }}>
                    Retry
                </button>
            )}
        </div>
    );
};
```

### File 5: components/FileViewer.tsx

```typescript
import * as React from 'react';
import { Text, makeStyles, tokens } from '@fluentui/react-components';
import { differenceInMinutes } from 'date-fns';
import { CustomApiService } from '../services/CustomApiService';
import { LoadingSpinner } from './LoadingSpinner';
import { ErrorMessage } from './ErrorMessage';
import { IFileViewerProps, IFileViewerState } from '../types/types';

const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        width: '100%'
    },
    header: {
        padding: '8px 16px',
        backgroundColor: tokens.colorNeutralBackground3,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`
    },
    iframe: {
        flex: 1,
        border: 'none',
        width: '100%'
    }
});

export const FileViewer: React.FC<IFileViewerProps> = ({
    context,
    documentId,
    height,
    showFileName
}) => {
    const styles = useStyles();
    const [state, setState] = React.useState<IFileViewerState>({
        loading: true,
        error: null,
        previewUrl: null,
        fileName: null,
        expiresAt: null,
        correlationId: null
    });

    const customApiService = React.useMemo(
        () => new CustomApiService(context),
        [context]
    );

    const refreshTimerRef = React.useRef<number | null>(null);

    /**
     * Load file preview from Custom API
     */
    const loadFilePreview = React.useCallback(async (): Promise<void> => {
        console.log('[FileViewer] Loading preview for document:', documentId);

        setState(prev => ({ ...prev, loading: true, error: null }));

        try {
            const result = await customApiService.getFilePreviewUrl(documentId);

            console.log('[FileViewer] Preview URL retrieved:', result);

            setState({
                loading: false,
                error: null,
                previewUrl: result.PreviewUrl,
                fileName: result.FileName,
                expiresAt: new Date(result.ExpiresAt),
                correlationId: result.CorrelationId
            });

            // Schedule auto-refresh
            scheduleRefresh(new Date(result.ExpiresAt));
        } catch (error: any) {
            console.error('[FileViewer] Error loading preview:', error);
            setState({
                loading: false,
                error: error.message,
                previewUrl: null,
                fileName: null,
                expiresAt: null,
                correlationId: null
            });
        }
    }, [documentId, customApiService]);

    /**
     * Schedule auto-refresh before URL expires
     * @param expiresAt Expiration date
     */
    const scheduleRefresh = (expiresAt: Date): void => {
        // Clear existing timer
        if (refreshTimerRef.current) {
            window.clearTimeout(refreshTimerRef.current);
        }

        // Calculate time until expiration
        const now = new Date();
        const minutesUntilExpiration = differenceInMinutes(expiresAt, now);

        // Refresh 1 minute before expiration
        const refreshInMinutes = Math.max(minutesUntilExpiration - 1, 0);
        const refreshInMs = refreshInMinutes * 60 * 1000;

        console.log(
            `[FileViewer] Scheduling refresh in ${refreshInMinutes} minutes`,
            { expiresAt: expiresAt.toISOString(), refreshInMs }
        );

        refreshTimerRef.current = window.setTimeout(() => {
            console.log('[FileViewer] Auto-refreshing preview URL');
            loadFilePreview();
        }, refreshInMs);
    };

    /**
     * Load preview on mount and when documentId changes
     */
    React.useEffect(() => {
        if (documentId) {
            loadFilePreview();
        }

        // Cleanup timer on unmount
        return () => {
            if (refreshTimerRef.current) {
                window.clearTimeout(refreshTimerRef.current);
            }
        };
    }, [documentId, loadFilePreview]);

    // Render loading state
    if (state.loading) {
        return <LoadingSpinner />;
    }

    // Render error state
    if (state.error) {
        return <ErrorMessage error={state.error} onRetry={loadFilePreview} />;
    }

    // Render file preview
    return (
        <div className={styles.container} style={{ height: `${height}px` }}>
            {showFileName && state.fileName && (
                <div className={styles.header}>
                    <Text weight="semibold">{state.fileName}</Text>
                </div>
            )}
            {state.previewUrl && (
                <iframe
                    src={state.previewUrl}
                    className={styles.iframe}
                    title={state.fileName || 'File Preview'}
                    sandbox="allow-scripts allow-same-origin allow-forms allow-popups"
                />
            )}
        </div>
    );
};
```

### File 6: App.tsx

```typescript
import * as React from 'react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { FileViewer } from './components/FileViewer';
import { IFileViewerProps } from './types/types';

export const App: React.FC<IFileViewerProps> = (props) => {
    return (
        <FluentProvider theme={webLightTheme}>
            <FileViewer {...props} />
        </FluentProvider>
    );
};
```

### File 7: index.ts (Root PCF Control)

Replace entire contents:

```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { App } from "./SpeFileViewer/App";

export class SpeFileViewer implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private context: ComponentFramework.Context<IInputs>;
    private notifyOutputChanged: () => void;

    /**
     * Initialize control
     */
    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.context = context;
        this.notifyOutputChanged = notifyOutputChanged;
        this.container = container;

        // Render React app
        this.renderControl();
    }

    /**
     * Update control when properties change
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        this.renderControl();
    }

    /**
     * Render React component
     */
    private renderControl(): void {
        const documentId = this.context.parameters.documentId.raw || "";
        const height = this.context.parameters.height?.raw || 600;
        const showFileName = this.context.parameters.showFileName?.raw || true;

        ReactDOM.render(
            React.createElement(App, {
                context: this.context,
                documentId,
                height,
                showFileName
            }),
            this.container
        );
    }

    /**
     * Get outputs (none for this control)
     */
    public getOutputs(): IOutputs {
        return {};
    }

    /**
     * Cleanup when control is destroyed
     */
    public destroy(): void {
        ReactDOM.unmountComponentAtNode(this.container);
    }
}
```

---

## Task 3.5: Build PCF Control

**Goal**: Compile TypeScript and create solution package

### Build Control

```bash
# Build PCF control
npm run build

# Expected output:
# ✓ TypeScript compiled successfully
# ✓ Bundle created
# ✓ Control manifest validated
```

### Create Solution Package

```bash
# Create solutions directory
mkdir solutions
cd solutions

# Initialize solution
pac solution init --publisher-name Spaarke --publisher-prefix sprk

# Add PCF control to solution
pac solution add-reference --path ..

# Build solution
msbuild /t:restore
msbuild /p:Configuration=Release

# Solution package created at:
# solutions/bin/Release/Spaarke_SpeFileViewer_1_0_0_0.zip
```

**Validation**:
```bash
# Verify solution zip exists
ls solutions/bin/Release/*.zip
```

---

## Task 3.6: Test PCF Control Locally

**Goal**: Test control in local test harness before deployment

### Start Test Harness

```bash
# Navigate back to control directory
cd ..

# Start test harness
npm start watch
```

**Expected**: Browser opens at `http://localhost:8181`

### Manual Test

1. **Test Harness** should display control
2. **Enter Document ID** in test property panel (use a real sprk_document ID from Dataverse)
3. **Verify**:
   - Loading spinner appears
   - Preview URL loads in iframe
   - File name displays (if showFileName = true)
   - No errors in console

### Debug Console

Check browser console for:
```
[CustomApiService] Getting preview URL for document: {guid}
[CustomApiService] Custom API response: {...}
[FileViewer] Preview URL retrieved: {...}
[FileViewer] Scheduling refresh in X minutes
```

**If Errors**: Check Custom API registration (Step 2) and BFF API deployment

---

## Validation Checklist

- [ ] **PCF Project**: Created with React template
- [ ] **Dependencies**: Fluent UI v9 installed
- [ ] **Manifest**: Configured with 3 properties (documentId, height, showFileName)
- [ ] **Types**: TypeScript interfaces defined
- [ ] **CustomApiService**: Calls Custom API correctly
- [ ] **FileViewer Component**: Renders iframe with preview URL
- [ ] **Auto-Refresh**: Scheduled before URL expiration
- [ ] **Error Handling**: Shows user-friendly error messages
- [ ] **Loading State**: Shows spinner during fetch
- [ ] **Build**: Compiles without errors
- [ ] **Solution Package**: Created successfully
- [ ] **Local Test**: Works in test harness

---

## Common Issues

### Issue: "Cannot find module '@fluentui/react-components'"

**Fix**: Install dependencies
```bash
npm install
```

### Issue: Build fails with TypeScript errors

**Fix**: Check tsconfig.json has correct settings:
```json
{
  "compilerOptions": {
    "module": "ESNext",
    "target": "ES2015",
    "jsx": "react",
    "strict": false
  }
}
```

### Issue: "CustomApiService is not defined"

**Fix**: Ensure import paths are correct:
```typescript
import { CustomApiService } from '../services/CustomApiService';
```

### Issue: Test harness shows "Failed to load control"

**Fix**: Rebuild control and restart test harness
```bash
npm run build
npm start watch
```

---

## Next Step

Once PCF control is built and tested locally, proceed to **Step 4: Deployment and Integration** to deploy to Dataverse and add to Document forms.

**Build Artifacts**:
- `solutions/bin/Release/Spaarke_SpeFileViewer_1_0_0_0.zip` - Solution package for import
