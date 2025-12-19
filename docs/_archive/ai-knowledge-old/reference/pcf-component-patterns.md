# PCF Component Patterns for Spaarke

> **Purpose**: Patterns and best practices for building PCF (PowerApps Component Framework) controls in Spaarke
> **Last Updated**: December 11, 2025
> **Applies To**: PCF development, Analysis Workspace component, custom control integration

---

## TL;DR

PCF controls = TypeScript/React components embedded in Power Apps. Use for rich UI that Canvas/MDA can't provide. Key patterns: Dataset binding, environment variables, WebAPI access, event communication.

---

## Spaarke PCF Architecture

### Existing PCF Controls

| Control | Purpose | Technology | Location |
|---------|---------|------------|----------|
| `SpeFileViewer` | Document preview (Office Online, PDF) | React + Fluent UI | `src/client/pcf/SpeFileViewer/` |
| `UniversalQuickCreate` | Document upload + metadata | React + Fluent UI | `src/client/pcf/UniversalQuickCreate/` |
| `UniversalDatasetGrid` | Enhanced grid display | React + Fluent UI | `src/client/pcf/UniversalDatasetGrid/` |

### New Control for Analysis

| Control | Purpose | Technology |
|---------|---------|------------|
| `AnalysisWorkspace` | Two-column workspace + chat | React + Fluent UI + Monaco Editor |

---

## Project Structure

```
src/client/pcf/AnalysisWorkspace/
├── AnalysisWorkspace/
│   ├── index.ts              # PCF entry point
│   ├── ControlManifest.Input.xml  # PCF manifest
│   ├── components/           # React components
│   │   ├── AnalysisWorkspaceContainer.tsx
│   │   ├── WorkingDocumentEditor.tsx
│   │   ├── SourceDocumentPreview.tsx
│   │   ├── ChatInterface.tsx
│   │   └── StreamingResponse.tsx
│   ├── services/             # Business logic
│   │   ├── SseClient.ts      # Server-Sent Events
│   │   ├── ApiClient.ts      # BFF API calls
│   │   └── AuthService.ts    # Token acquisition
│   ├── models/               # TypeScript types
│   │   ├── AnalysisTypes.ts
│   │   └── ApiContracts.ts
│   └── styles/               # Component styles
│       └── AnalysisWorkspace.module.css
├── package.json
├── tsconfig.json
└── .eslintrc.json
```

---

## PCF Manifest (ControlManifest.Input.xml)

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke" 
           constructor="AnalysisWorkspace" 
           version="1.0.0" 
           display-name-key="AnalysisWorkspace_Display_Key" 
           description-key="AnalysisWorkspace_Desc_Key" 
           control-type="standard">
    
    <!-- Input Properties (from Power Apps) -->
    <property name="analysisId" 
              display-name-key="AnalysisId_Display_Key" 
              description-key="AnalysisId_Desc_Key" 
              of-type="SingleLine.Text" 
              usage="input" 
              required="true" />
    
    <property name="documentId" 
              display-name-key="DocumentId_Display_Key" 
              of-type="SingleLine.Text" 
              usage="input" 
              required="true" />
    
    <property name="message" 
              display-name-key="Message_Display_Key" 
              of-type="Multiple" 
              usage="input" 
              required="false" />
    
    <property name="sendMessage" 
              display-name-key="SendMessage_Display_Key" 
              of-type="TwoOptions" 
              usage="input" 
              required="false" />
    
    <!-- Output Properties (to Power Apps) -->
    <property name="responseChunk" 
              display-name-key="ResponseChunk_Display_Key" 
              of-type="Multiple" 
              usage="output" />
    
    <property name="status" 
              display-name-key="Status_Display_Key" 
              of-type="SingleLine.Text" 
              usage="output" />
    
    <property name="onAnalysisComplete" 
              display-name-key="OnAnalysisComplete_Display_Key" 
              of-type="Multiple" 
              usage="output" />
    
    <!-- Resources -->
    <resources>
      <code path="index.ts" order="1"/>
      <css path="styles/AnalysisWorkspace.module.css" order="1" />
      <resx path="strings/AnalysisWorkspace.1033.resx" version="1.0.0" />
    </resources>
    
    <!-- Feature Usage -->
    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

---

## PCF Entry Point (index.ts)

```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { AnalysisWorkspaceContainer } from "./components/AnalysisWorkspaceContainer";

export class AnalysisWorkspace implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private _container: HTMLDivElement;
  private _context: ComponentFramework.Context<IInputs>;
  private _notifyOutputChanged: () => void;
  
  // Output values
  private _responseChunk: string = "";
  private _status: string = "idle";
  private _analysisCompleteData: any = null;

  /**
   * Initialize control - called once when control is loaded
   */
  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this._context = context;
    this._container = container;
    this._notifyOutputChanged = notifyOutputChanged;
    
    // Render React component
    this.renderComponent();
  }

  /**
   * Update control - called when inputs change
   */
  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this._context = context;
    
    // Check if sendMessage trigger changed
    if (context.parameters.sendMessage.raw && 
        context.parameters.message.raw) {
      this.handleSendMessage(context.parameters.message.raw);
    }
    
    // Re-render with updated props
    this.renderComponent();
  }

  /**
   * Render React component
   */
  private renderComponent(): void {
    const props = {
      analysisId: this._context.parameters.analysisId.raw || "",
      documentId: this._context.parameters.documentId.raw || "",
      context: this._context,
      onResponseChunk: this.handleResponseChunk.bind(this),
      onStatusChange: this.handleStatusChange.bind(this),
      onAnalysisComplete: this.handleAnalysisComplete.bind(this)
    };

    ReactDOM.render(
      React.createElement(AnalysisWorkspaceContainer, props),
      this._container
    );
  }

  /**
   * Handle sending message to AI
   */
  private async handleSendMessage(message: string): Promise<void> {
    // Implementation in service layer
    // This triggers SSE stream
  }

  /**
   * Handle SSE response chunks
   */
  private handleResponseChunk(chunk: string): void {
    this._responseChunk = chunk;
    this._notifyOutputChanged(); // Tell Power Apps output changed
  }

  /**
   * Handle status changes
   */
  private handleStatusChange(status: string): void {
    this._status = status;
    this._notifyOutputChanged();
  }

  /**
   * Handle analysis completion
   */
  private handleAnalysisComplete(data: any): void {
    this._analysisCompleteData = data;
    this._notifyOutputChanged();
  }

  /**
   * Get outputs (called by Power Apps)
   */
  public getOutputs(): IOutputs {
    return {
      responseChunk: this._responseChunk,
      status: this._status,
      onAnalysisComplete: this._analysisCompleteData
    };
  }

  /**
   * Cleanup - called when control is destroyed
   */
  public destroy(): void {
    ReactDOM.unmountComponentAtNode(this._container);
  }
}
```

---

## Environment Variables Access

```typescript
/**
 * Get environment variable value from Dataverse
 */
export async function getEnvironmentVariable(
  context: ComponentFramework.Context<any>,
  schemaName: string
): Promise<string> {
  try {
    // Query environment variable definition
    const result = await context.webAPI.retrieveMultipleRecords(
      "environmentvariabledefinition",
      `?$filter=schemaname eq '${schemaName}'&$expand=environmentvariabledefinition_environmentvariablevalue`
    );

    if (result.entities.length === 0) {
      console.warn(`Environment variable not found: ${schemaName}`);
      return "";
    }

    const definition = result.entities[0];
    const values = definition.environmentvariabledefinition_environmentvariablevalue;

    // Return current value or default value
    if (values && values.length > 0) {
      return values[0].value || definition.defaultvalue || "";
    }

    return definition.defaultvalue || "";
  } catch (error) {
    console.error(`Error retrieving environment variable ${schemaName}:`, error);
    return "";
  }
}

/**
 * Usage in service
 */
export class ApiClient {
  private _baseUrl: string = "";
  
  async initialize(context: ComponentFramework.Context<any>): Promise<void> {
    this._baseUrl = await getEnvironmentVariable(context, "sprk_BffApiBaseUrl");
    
    if (!this._baseUrl) {
      // Fallback for development
      this._baseUrl = "https://localhost:5001";
      console.warn("Using fallback API URL for development");
    }
  }
}
```

---

## Authentication & Token Acquisition

```typescript
/**
 * Acquire access token for BFF API using MSAL
 */
export class AuthService {
  private _context: ComponentFramework.Context<any>;
  
  constructor(context: ComponentFramework.Context<any>) {
    this._context = context;
  }

  /**
   * Get token for BFF API
   * Uses Power Apps context to get user token
   */
  public async getAccessToken(): Promise<string> {
    try {
      // Power Apps provides token through context
      // For delegated scenarios (OBO)
      const userSettings = this._context.userSettings;
      
      // Note: In PCF, we typically rely on the hosting app's authentication
      // The Custom Page or MDA form already has the user authenticated
      
      // For BFF API calls, use the organization's URL to get token
      const orgUrl = (this._context as any).page.getClientUrl();
      
      // Token acquisition typically handled by browser's cookie/session
      // For explicit MSAL, you'd need to implement MSAL.js flow
      
      // Return empty string - let browser handle auth via cookies
      // OR implement explicit MSAL token acquisition (see below)
      return "";
      
    } catch (error) {
      console.error("Error acquiring token:", error);
      throw error;
    }
  }
  
  /**
   * Alternative: Use MSAL.js for explicit token acquisition
   */
  public async getAccessTokenMsal(scopes: string[]): Promise<string> {
    // Import MSAL library in PCF
    // Add to package.json: "@azure/msal-browser": "^3.0.0"
    
    const msalConfig = {
      auth: {
        clientId: await getEnvironmentVariable(this._context, "sprk_ClientId"),
        authority: "https://login.microsoftonline.com/organizations"
      }
    };
    
    // Initialize MSAL
    const msalInstance = new PublicClientApplication(msalConfig);
    
    // Acquire token silently (from cache or refresh token)
    const request = {
      scopes: scopes,
      account: msalInstance.getAllAccounts()[0]
    };
    
    try {
      const response = await msalInstance.acquireTokenSilent(request);
      return response.accessToken;
    } catch (error) {
      // Fallback to interactive login
      const response = await msalInstance.acquireTokenPopup(request);
      return response.accessToken;
    }
  }
}
```

---

## Server-Sent Events (SSE) Client

```typescript
/**
 * SSE client for streaming AI responses
 */
export class SseClient {
  private _eventSource: EventSource | null = null;
  private _onChunk: (chunk: string) => void;
  private _onComplete: () => void;
  private _onError: (error: any) => void;

  constructor(
    onChunk: (chunk: string) => void,
    onComplete: () => void,
    onError: (error: any) => void
  ) {
    this._onChunk = onChunk;
    this._onComplete = onComplete;
    this._onError = onError;
  }

  /**
   * Start SSE stream
   */
  public async stream(url: string, token?: string): Promise<void> {
    try {
      // Note: EventSource doesn't support custom headers (like Authorization)
      // Workaround: Pass token as query param (less secure but necessary)
      const urlWithToken = token 
        ? `${url}?token=${encodeURIComponent(token)}`
        : url;

      this._eventSource = new EventSource(urlWithToken);

      this._eventSource.addEventListener("message", (event) => {
        try {
          const data = JSON.parse(event.data);
          
          if (data.type === "chunk") {
            this._onChunk(data.content);
          } else if (data.type === "done") {
            this._onComplete();
            this.close();
          } else if (data.type === "error") {
            this._onError(new Error(data.message));
            this.close();
          }
        } catch (error) {
          console.error("Error parsing SSE data:", error);
        }
      });

      this._eventSource.addEventListener("error", (error) => {
        this._onError(error);
        this.close();
      });

    } catch (error) {
      this._onError(error);
    }
  }

  /**
   * Close SSE connection
   */
  public close(): void {
    if (this._eventSource) {
      this._eventSource.close();
      this._eventSource = null;
    }
  }
}

/**
 * Usage in React component
 */
export const ChatInterface: React.FC<ChatInterfaceProps> = (props) => {
  const [sseClient] = useState<SseClient>(() => 
    new SseClient(
      (chunk) => {
        // Append chunk to message
        setStreamingMessage(prev => prev + chunk);
      },
      () => {
        // Stream complete
        setIsStreaming(false);
        props.onComplete();
      },
      (error) => {
        // Handle error
        console.error("SSE error:", error);
        setError(error.message);
      }
    )
  );

  const handleSendMessage = async () => {
    const apiUrl = `${props.apiBaseUrl}/api/ai/analysis/${props.analysisId}/continue`;
    const token = await props.authService.getAccessToken();
    
    setIsStreaming(true);
    await sseClient.stream(apiUrl, token);
  };

  useEffect(() => {
    return () => sseClient.close(); // Cleanup on unmount
  }, []);

  // ... rest of component
};
```

---

## WebAPI Usage (Dataverse Access)

```typescript
/**
 * Read data from Dataverse
 */
export async function getAnalysis(
  context: ComponentFramework.Context<any>,
  analysisId: string
): Promise<any> {
  try {
    const result = await context.webAPI.retrieveRecord(
      "sprk_analysis",
      analysisId,
      "?$select=sprk_name,sprk_workingdocument,sprk_status,sprk_sessionid" +
      "&$expand=sprk_documentid($select=sprk_name,sprk_driveid,sprk_itemid)"
    );
    return result;
  } catch (error) {
    console.error("Error retrieving analysis:", error);
    throw error;
  }
}

/**
 * Update data in Dataverse
 */
export async function updateWorkingDocument(
  context: ComponentFramework.Context<any>,
  analysisId: string,
  content: string,
  sessionId: string
): Promise<void> {
  try {
    await context.webAPI.updateRecord(
      "sprk_analysis",
      analysisId,
      {
        sprk_workingdocument: content,
        sprk_sessionid: sessionId
      }
    );
  } catch (error) {
    console.error("Error updating working document:", error);
    throw error;
  }
}

/**
 * Create related record
 */
export async function createChatMessage(
  context: ComponentFramework.Context<any>,
  analysisId: string,
  role: string,
  content: string
): Promise<string> {
  try {
    const result = await context.webAPI.createRecord(
      "sprk_analysischatmessage",
      {
        "sprk_analysisid@odata.bind": `/sprk_analyses(${analysisId})`,
        sprk_role: role === "user" ? 100000000 : 100000001, // Choice value
        sprk_content: content
      }
    );
    return result.id;
  } catch (error) {
    console.error("Error creating chat message:", error);
    throw error;
  }
}
```

---

## React Component Pattern

```typescript
/**
 * Main container component for Analysis Workspace
 */
import React, { useState, useEffect } from "react";
import { Stack } from "@fluentui/react";
import { WorkingDocumentEditor } from "./WorkingDocumentEditor";
import { SourceDocumentPreview } from "./SourceDocumentPreview";
import { ChatInterface } from "./ChatInterface";

export interface AnalysisWorkspaceContainerProps {
  analysisId: string;
  documentId: string;
  context: ComponentFramework.Context<any>;
  onResponseChunk: (chunk: string) => void;
  onStatusChange: (status: string) => void;
  onAnalysisComplete: (data: any) => void;
}

export const AnalysisWorkspaceContainer: React.FC<AnalysisWorkspaceContainerProps> = (props) => {
  const [analysis, setAnalysis] = useState<any>(null);
  const [workingDocument, setWorkingDocument] = useState<string>("");
  const [isStreaming, setIsStreaming] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  // Load analysis on mount
  useEffect(() => {
    loadAnalysis();
  }, [props.analysisId]);

  const loadAnalysis = async () => {
    try {
      props.onStatusChange("loading");
      const data = await getAnalysis(props.context, props.analysisId);
      setAnalysis(data);
      setWorkingDocument(data.sprk_workingdocument || "");
      props.onStatusChange("ready");
    } catch (error) {
      setError("Failed to load analysis");
      props.onStatusChange("error");
    }
  };

  const handleWorkingDocumentChange = (content: string) => {
    setWorkingDocument(content);
    // Debounced auto-save
    debouncedSave(content);
  };

  const handleChatMessage = async (message: string) => {
    setIsStreaming(true);
    props.onStatusChange("streaming");
    
    // SSE streaming handled in ChatInterface component
    // Chunks will come through props.onResponseChunk
  };

  const handleStreamComplete = () => {
    setIsStreaming(false);
    props.onStatusChange("ready");
    props.onAnalysisComplete({
      analysisId: props.analysisId,
      status: "completed"
    });
  };

  if (!analysis) {
    return <div>Loading...</div>;
  }

  return (
    <Stack styles={{ root: { height: "100%", width: "100%" } }}>
      {/* Header */}
      <Stack horizontal styles={{ root: { padding: 16, borderBottom: "1px solid #e1e1e1" } }}>
        <Stack.Item grow={1}>
          <h2>{analysis.sprk_name}</h2>
        </Stack.Item>
      </Stack>

      {/* Two-column layout */}
      <Stack horizontal styles={{ root: { flex: 1, overflow: "hidden" } }}>
        {/* Left: Working document */}
        <Stack.Item grow={1} styles={{ root: { borderRight: "1px solid #e1e1e1", overflow: "auto" } }}>
          <WorkingDocumentEditor
            content={workingDocument}
            onChange={handleWorkingDocumentChange}
            readOnly={isStreaming}
          />
        </Stack.Item>

        {/* Right: Source preview */}
        <Stack.Item grow={1} styles={{ root: { overflow: "auto" } }}>
          <SourceDocumentPreview
            documentId={props.documentId}
            driveId={analysis.sprk_documentid?.sprk_driveid}
            itemId={analysis.sprk_documentid?.sprk_itemid}
            context={props.context}
          />
        </Stack.Item>
      </Stack>

      {/* Footer: Chat interface */}
      <Stack styles={{ root: { padding: 16, borderTop: "1px solid #e1e1e1" } }}>
        <ChatInterface
          analysisId={props.analysisId}
          context={props.context}
          onSendMessage={handleChatMessage}
          onStreamComplete={handleStreamComplete}
          isStreaming={isStreaming}
        />
      </Stack>
    </Stack>
  );
};
```

---

## Building & Packaging

```powershell
# Install dependencies
npm install

# Build PCF control
npm run build

# Watch mode (development)
npm run start watch

# Package solution
pac solution init --publisher-name Spaarke --publisher-prefix sprk
pac solution add-reference --path ../AnalysisWorkspace

# Create managed solution
pac solution pack --zipfile ../../solutions/AnalysisWorkspace_managed.zip --packagetype Managed

# Deploy to environment
pac auth create --environment https://yourorg.crm.dynamics.com
pac pcf push --publisher-prefix sprk
```

---

## Debugging

```typescript
// Development mode checks
if (process.env.NODE_ENV === "development") {
  console.log("Analysis ID:", props.analysisId);
  console.log("API Base URL:", apiBaseUrl);
}

// Use browser DevTools
// Set breakpoints in TypeScript (source maps enabled)

// Power Apps Monitor
// View PCF control events and property changes
```

---

## Best Practices

### Performance
- ✅ **Debounce auto-save** (don't save every keystroke)
- ✅ **Lazy load heavy components** (Monaco editor, file preview)
- ✅ **Cancel pending requests** on unmount
- ✅ **Use React.memo** for expensive renders
- ❌ **Don't call WebAPI in render** (use useEffect)

### Security
- ✅ **Validate all inputs** before API calls
- ✅ **Use HTTPS only** for all requests
- ✅ **Handle token expiration** gracefully
- ✅ **Sanitize user content** before rendering
- ❌ **Don't log sensitive data** (tokens, PII)

### Error Handling
- ✅ **Show user-friendly error messages**
- ✅ **Log errors to console** (with context)
- ✅ **Implement retry logic** for transient failures
- ✅ **Graceful degradation** (offline support)
- ❌ **Don't swallow errors** silently

---

## Related Documentation

- [Power Apps Custom Pages](./power-apps-custom-pages.md)
- [PCF Controls Overview](https://docs.microsoft.com/power-apps/developer/component-framework/overview)
- [Fluent UI React Components](https://developer.microsoft.com/fluentui)
- [Monaco Editor](https://microsoft.github.io/monaco-editor/)

---

*Last Updated: December 11, 2025*
