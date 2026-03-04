# Custom Dialogs in Dataverse Web Resources

> **Category**: Web Resources / UI Patterns
> **Last Updated**: January 2026
> **ADR Reference**: ADR-023 (Choice Dialogs)

---

## The Problem: Iframe Context

Dataverse model-driven apps run web resources inside **iframes**. This creates a critical issue for custom DOM-based dialogs:

```javascript
// ❌ WRONG - This renders inside the iframe, not visible to user
document.body.appendChild(overlay);
// overlay.offsetWidth === 0 (invisible!)
```

The dialog is appended to the iframe's `document.body`, which may have limited dimensions or be hidden from view. The user sees nothing.

---

## Solution: Use `window.top.document`

To render dialogs over the full page, use the **top window's document**:

```javascript
// ✅ CORRECT - Escapes iframe context
var targetDoc = window.top ? window.top.document : document;

// Create all elements using targetDoc
var overlay = targetDoc.createElement('div');
var dialog = targetDoc.createElement('div');
// ... etc

// Append to top window's body
targetDoc.body.appendChild(overlay);

// Event listeners on top document
targetDoc.addEventListener('keydown', escHandler);

// Cleanup uses targetDoc
function cleanup() {
    targetDoc.body.removeChild(overlay);
    targetDoc.removeEventListener('keydown', escHandler);
}
```

---

## Complete Pattern: Custom Choice Dialog

```javascript
function showChoiceDialog(config) {
    return new Promise(function(resolve) {
        // CRITICAL: Escape iframe context
        var targetDoc = window.top ? window.top.document : document;

        // Create overlay with inline styles (no CSS dependencies)
        var overlay = targetDoc.createElement('div');
        overlay.style.cssText = 'position:fixed;top:0;left:0;right:0;bottom:0;' +
            'background:rgba(0,0,0,0.4);display:flex;align-items:center;' +
            'justify-content:center;z-index:10000;font-family:"Segoe UI",sans-serif;';

        // Create dialog surface
        var dialog = targetDoc.createElement('div');
        dialog.style.cssText = 'background:#fff;border-radius:8px;' +
            'box-shadow:0 25px 65px rgba(0,0,0,0.35);max-width:480px;width:90%;';

        // ... build dialog content using targetDoc.createElement()

        // Cleanup function
        function cleanup(result) {
            targetDoc.body.removeChild(overlay);
            targetDoc.removeEventListener('keydown', escHandler);
            resolve(result);
        }

        // ESC key handler
        function escHandler(e) {
            if (e.key === 'Escape') cleanup(null);
        }
        targetDoc.addEventListener('keydown', escHandler);

        // Append to DOM
        targetDoc.body.appendChild(overlay);
    });
}
```

---

## When to Use Custom Dialogs vs Xrm.Navigation

### Use Custom Dialogs When:
- Need rich UI (icons, styled buttons, custom layouts)
- ADR-023 choice dialogs with multiple options + descriptions
- Need full control over appearance (Fluent-style design)
- Complex interactions (email links, multiple actions)

### Use Xrm.Navigation When:
- Simple confirmations (`openConfirmDialog`)
- Error/alert messages (`openAlertDialog`, `openErrorDialog`)
- Standard Yes/No decisions
- Don't need custom styling

### Comparison

| Feature | Custom Dialog | Xrm.Navigation |
|---------|--------------|----------------|
| **Styling** | Full control | Limited to Dataverse theme |
| **Complexity** | Higher | Simple API |
| **Iframe handling** | Must use `window.top` | Automatic |
| **Multiple options** | Yes (n choices) | 2 buttons max |
| **Icons/descriptions** | Yes | No |
| **Accessibility** | Manual (focus, ESC) | Built-in |

### Xrm.Navigation Examples

```javascript
// Simple confirmation - handles iframe automatically
var result = await Xrm.Navigation.openConfirmDialog({
    title: "Confirm Action",
    text: "Are you sure?",
    confirmButtonLabel: "Yes",
    cancelButtonLabel: "No"
});
if (result.confirmed) { /* proceed */ }

// Error dialog
await Xrm.Navigation.openErrorDialog({
    message: "Something went wrong"
});

// Alert dialog
await Xrm.Navigation.openAlertDialog({
    title: "Notice",
    text: "Operation completed successfully"
});
```

---

## Key Implementation Notes

### 1. Always Use Inline Styles
CSS files may not load in the top window context. Use inline `style.cssText`:

```javascript
// ✅ Works across contexts
button.style.cssText = 'padding:8px 20px;background:#0078d4;color:#fff;';

// ❌ May not work - CSS class might not exist in top window
button.className = 'my-button';
```

### 2. Z-Index Considerations
Dataverse UI uses high z-index values. Use `z-index: 10000` or higher for dialogs.

### 3. Cross-Origin Restrictions
If `window.top` is blocked by cross-origin policy, catch the error and fall back:

```javascript
var targetDoc;
try {
    targetDoc = window.top ? window.top.document : document;
} catch (e) {
    // Cross-origin restriction - fall back to Xrm dialogs
    console.warn("Cannot access top window, using Xrm.Navigation");
    return Xrm.Navigation.openConfirmDialog({...});
}
```

### 4. Cleanup on Navigation
If user navigates away while dialog is open, the cleanup function won't run. For critical cleanup, consider using `beforeunload`:

```javascript
function handleUnload() {
    if (overlay.parentNode) {
        targetDoc.body.removeChild(overlay);
    }
}
window.top.addEventListener('beforeunload', handleUnload);
```

---

## Reference Implementation

See `sprk_DocumentOperations.js` function `showChoiceDialog()` for a complete working implementation that:
- Uses `window.top.document` for all DOM operations
- Implements ADR-023 choice dialog pattern
- Handles ESC key for cancel
- Focuses first option for accessibility
- Cleans up event listeners properly

---

## Related

- **ADR-023**: Choice Dialog Pattern (design specifications)
- **ADR-006**: PCF over WebResources (when to use each approach)
- [sprk_DocumentOperations.js](../../../src/client/webresources/js/sprk_DocumentOperations.js) - Reference implementation

---

## Multi-Step Wizard Dialogs (WizardShell)

> **Last Updated**: March 2026
> **Reference**: `src/solutions/LegalWorkspace/src/components/Wizard/`

### Overview

`WizardShell` is a reusable, generic multi-step dialog component. It handles the wizard chrome — sidebar stepper, content area, navigation footer (Back / Next / Finish), and the post-finish success screen. Domain-specific content is injected via `IWizardStepConfig` arrays.

Key capabilities:
- Sidebar stepper with `pending` / `active` / `completed` states
- Dynamic step insertion/removal at runtime (via ref handle)
- "Early finish" pattern — treat Next as Finish mid-wizard based on context
- Per-step footer actions alongside standard navigation buttons
- Built-in success screen (`IWizardSuccessConfig`) after `onFinish` resolves

### File Locations and Imports

```typescript
// Component
import { WizardShell } from './components/Wizard/WizardShell';

// Types (zero domain dependencies — safe to import anywhere)
import type {
  IWizardStepConfig,
  IWizardShellHandle,
  IWizardShellProps,
  IWizardSuccessConfig,
} from './components/Wizard/wizardShellTypes';
```

File locations within a solution:
```
src/solutions/{SolutionName}/src/components/Wizard/
├── WizardShell.tsx          # Component implementation
└── wizardShellTypes.ts      # All type definitions
```

### Basic Usage

```tsx
import * as React from 'react';
import { CheckmarkCircle24Regular } from '@fluentui/react-icons';
import { WizardShell } from './components/Wizard/WizardShell';
import type { IWizardStepConfig, IWizardSuccessConfig } from './components/Wizard/wizardShellTypes';

const MyWizard: React.FC<{ open: boolean; onClose: () => void }> = ({ open, onClose }) => {
  const [name, setName] = React.useState('');
  const [confirmed, setConfirmed] = React.useState(false);

  const steps: IWizardStepConfig[] = [
    {
      id: 'step-details',
      label: 'Details',
      renderContent: (_handle) => (
        <div>
          <label>Name</label>
          <input value={name} onChange={e => setName(e.target.value)} />
        </div>
      ),
      canAdvance: () => name.trim().length > 0,
    },
    {
      id: 'step-review',
      label: 'Review',
      renderContent: (_handle) => (
        <div>
          <p>Name: {name}</p>
          <input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />
          <label> I confirm the details are correct</label>
        </div>
      ),
      canAdvance: () => confirmed,
    },
    {
      id: 'step-submit',
      label: 'Submit',
      renderContent: (_handle) => <p>Click Finish to create the record.</p>,
      canAdvance: () => true,
    },
  ];

  const handleFinish = async (): Promise<IWizardSuccessConfig> => {
    await createRecord({ name }); // your API call
    return {
      icon: <CheckmarkCircle24Regular style={{ color: '#107C10', fontSize: 48 }} />,
      title: 'Record Created',
      body: `"${name}" was created successfully.`,
      actions: <button onClick={onClose}>Close</button>,
    };
  };

  return (
    <WizardShell
      open={open}
      title="Create Record"
      steps={steps}
      onClose={onClose}
      onFinish={handleFinish}
    />
  );
};
```

### Step Config API (IWizardStepConfig)

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `id` | `string` | Yes | Unique step identifier. Must be stable across re-renders. |
| `label` | `string` | Yes | Display label in the sidebar stepper. |
| `renderContent` | `(handle: IWizardShellHandle) => ReactNode` | Yes | Renders the step's main content area. Receives the shell handle for dynamic step control. |
| `canAdvance` | `() => boolean` | Yes | Controls whether the Next/Finish button is enabled. Called on every render. |
| `isEarlyFinish` | `() => boolean` | No | When `true`, the Next button acts as Finish and triggers `onFinish`. Use for conditional branching where remaining steps are not needed. |
| `footerActions` | `ReactNode` | No | Extra buttons rendered in the footer alongside Back/Next/Finish (e.g., "Reset", "Preview"). |

### Dynamic Steps

Steps can be added or removed at runtime from within a step's `renderContent` via the `IWizardShellHandle`:

```tsx
const steps: IWizardStepConfig[] = [
  {
    id: 'step-options',
    label: 'Follow-on Actions',
    renderContent: (handle) => {
      const canonicalOrder = ['step-options', 'step-task', 'step-deadline', 'step-submit'];

      return (
        <div>
          <label>
            <input
              type="checkbox"
              onChange={e => {
                if (e.target.checked) {
                  handle.addDynamicStep(
                    {
                      id: 'step-task',
                      label: 'Create Task',
                      renderContent: (_h) => <TaskForm />,
                      canAdvance: () => true,
                    },
                    canonicalOrder  // optional: enforce insertion order
                  );
                } else {
                  handle.removeDynamicStep('step-task');
                }
              }}
            />
            Create a follow-up task
          </label>
        </div>
      );
    },
    canAdvance: () => true,
    // Early finish: if no follow-on actions selected, treat Next as Finish
    isEarlyFinish: () => !handle.state.steps.some(s => s.id === 'step-task'),
  },
  // step-task is inserted dynamically above
  {
    id: 'step-submit',
    label: 'Submit',
    renderContent: (_h) => <p>Ready to submit.</p>,
    canAdvance: () => true,
  },
];
```

Note: `addDynamicStep` is idempotent — calling it with an existing `id` is a no-op. `removeDynamicStep` is safe to call when the step does not exist.

### onFinish and IWizardSuccessConfig

`onFinish` is an async callback that fires when the user clicks Finish (last step or early finish). Return an `IWizardSuccessConfig` to display a success screen, or return `void` to have the shell close itself via `onClose`.

```tsx
const handleFinish = async (): Promise<IWizardSuccessConfig | void> => {
  try {
    const result = await submitWizardData(formState);

    return {
      icon: <CheckmarkCircle24Regular style={{ color: '#107C10', fontSize: 48 }} />,
      title: 'Matter Created',
      body: (
        <span>
          Matter <strong>{result.reference}</strong> is ready.
        </span>
      ),
      actions: (
        <>
          <button onClick={() => openRecord(result.id)}>Open Matter</button>
          <button onClick={onClose}>Close</button>
        </>
      ),
      // Optional: surface partial failures without blocking success
      warnings: result.skippedActions.map(a => `Follow-on action "${a}" could not be created`),
    };
  } catch {
    // Re-throw to keep the wizard open with the Finish button re-enabled
    throw new Error('Failed to create matter. Please try again.');
  }
};
```

`IWizardSuccessConfig` fields:

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `icon` | `ReactNode` | Yes | Icon or illustration shown above the title. |
| `title` | `string` | Yes | Primary success heading. |
| `body` | `ReactNode` | Yes | Body content below the title — string or JSX. |
| `actions` | `ReactNode` | Yes | Action buttons (e.g., "Open Record", "Close"). |
| `warnings` | `string[]` | No | Non-fatal caveats shown alongside the success content. |

### Lazy Loading via React.lazy()

For large wizards that are not always shown, lazy-load `WizardShell` to keep bundle sizes down:

```tsx
// Lazy-load the entire wizard component tree
const MyWizard = React.lazy(() =>
  import('./components/MyWizard').then(m => ({ default: m.MyWizard }))
);

// Wrap with Suspense at the call site
const ParentComponent: React.FC = () => {
  const [open, setOpen] = React.useState(false);

  return (
    <>
      <button onClick={() => setOpen(true)}>Open Wizard</button>
      {open && (
        <React.Suspense fallback={null}>
          <MyWizard open={open} onClose={() => setOpen(false)} />
        </React.Suspense>
      )}
    </>
  );
};
```

The `WizardShell` itself can also be lazy-loaded within your wizard component if WizardShell's dependencies are heavy. Only render the `<Suspense>` boundary when `open` is `true` to defer the network request until the wizard is actually needed.

---

## Related

- **ADR-023**: Choice Dialog Pattern (design specifications)
- **ADR-006**: PCF over WebResources (when to use each approach)
- [sprk_DocumentOperations.js](../../../src/client/webresources/js/sprk_DocumentOperations.js) - Reference implementation (choice dialogs)
- [wizardShellTypes.ts](../../../src/solutions/LegalWorkspace/src/components/Wizard/wizardShellTypes.ts) - Full type definitions for WizardShell
