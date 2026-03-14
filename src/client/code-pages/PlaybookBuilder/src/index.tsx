/**
 * PlaybookBuilder -- React 19 Code Page Entry Point
 *
 * Opened via Xrm.Navigation.navigateTo from the playbook form:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_playbookbuilder",
 *       data: "playbookId=..." },
 *     { target: 2, width: { value: 95, unit: "%" }, height: { value: 95, unit: "%" } }
 *   )
 *
 * Theme detection follows 4-level priority:
 *   1. URL parameter (?theme=dark|light|highcontrast)
 *   2. Xrm frame-walk (Dataverse host theme)
 *   3. System preference (prefers-color-scheme)
 *   4. Default: webLightTheme
 *
 * @see ADR-006 - Code Pages for standalone dialogs
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 * @see ADR-022 - React 19 for Code Pages (exempt from PCF React 16)
 */

import { useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { App } from './App';
import { useThemeDetection } from './hooks/useThemeDetection';

// ---------------------------------------------------------------------------
// Parse URL parameters
// ---------------------------------------------------------------------------

const rawUrlParams = new URLSearchParams(window.location.search);
const dataEnvelope = rawUrlParams.get('data');
const appParams = dataEnvelope ? new URLSearchParams(decodeURIComponent(dataEnvelope)) : rawUrlParams;

function resolvePlaybookId(): string {
  // 1. Explicit URL parameter (navigateTo data envelope)
  const explicit = appParams.get('playbookId');
  if (explicit) return explicit.replace(/[{}]/g, '').toLowerCase();

  // 2. Dataverse ?id= parameter
  const dvId = rawUrlParams.get('id');
  if (dvId) return dvId.replace(/[{}]/g, '').toLowerCase();

  // 3. Embedded web resource on form — get record ID from parent Xrm context
  try {
    /* eslint-disable @typescript-eslint/no-explicit-any */
    const xrm = (window as any).parent?.Xrm;
    const formContext = xrm?.Page;
    if (formContext?.data?.entity) {
      const entityId = formContext.data.entity.getId();
      if (entityId) return entityId.replace(/[{}]/g, '').toLowerCase();
    }
    /* eslint-enable @typescript-eslint/no-explicit-any */
  } catch {
    // Cross-origin or Xrm not available — continue
  }

  return '';
}

const playbookId = resolvePlaybookId();

// ---------------------------------------------------------------------------
// ThemeRoot -- wrapper that uses useThemeDetection hook
// ---------------------------------------------------------------------------

function ThemeRoot(): JSX.Element {
  const { theme } = useThemeDetection(appParams);

  useEffect(() => {
    const bgColor = (theme as Record<string, string>).colorNeutralBackground1;
    if (bgColor) {
      document.body.style.backgroundColor = bgColor;
    }
  }, [theme]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <App playbookId={playbookId} />
    </FluentProvider>
  );
}

// ---------------------------------------------------------------------------
// Render
// ---------------------------------------------------------------------------

const container = document.getElementById('root');
if (!container) throw new Error('[PlaybookBuilder] Root container #root not found in DOM.');

createRoot(container).render(<ThemeRoot />);
