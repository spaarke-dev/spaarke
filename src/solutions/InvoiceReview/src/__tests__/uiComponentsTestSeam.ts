/**
 * The `@spaarke/ui-components` modules the Invoice Review page imports, at their source, for the jest harness (jest.config.cjs maps
 * `@spaarke/ui-components` here so the library barrel is never loaded). Every component is the REAL shared one except
 * `PdfHighlightViewer`: its pdfjs rendering is 108's to test, so here it is a stand-in that shows the highlight the page asks for and calls
 * `loadData` (the page's PDF fetch).
 */
import * as React from 'react';
import type { IPdfHighlight, IPdfHighlightViewerProps } from '../../../../client/shared/Spaarke.UI.Components/src/components/FilePreview/PdfHighlightViewer';

export { MetricCardRow } from '../../../../client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/MetricCardRow';
export type { MetricCardConfig } from '../../../../client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/types';
export { PanelSplitter } from '../../../../client/shared/Spaarke.UI.Components/src/components/PanelSplitter/PanelSplitter';
export { useTwoPanelLayout } from '../../../../client/shared/Spaarke.UI.Components/src/hooks/useTwoPanelLayout';
export { parseDataParams } from '../../../../client/shared/Spaarke.UI.Components/src/utils/parseDataParams';
export { StatusBadge } from '../../../../client/shared/Spaarke.UI.Components/src/components/StatusBadge/StatusBadge';
export type { StatusBadgeTone } from '../../../../client/shared/Spaarke.UI.Components/src/components/StatusBadge/StatusBadge';
export { StatusBar } from '../../../../client/shared/Spaarke.UI.Components/src/components/ConsoleKit/StatusBar';
export { EvidenceLine } from '../../../../client/shared/Spaarke.UI.Components/src/components/ConsoleKit/EvidenceLine';
export type { ConsoleStatusOverride } from '../../../../client/shared/Spaarke.UI.Components/src/components/ConsoleKit/consoleStatus';
export { AiFieldTag } from '../../../../client/shared/Spaarke.UI.Components/src/components/AiFieldTag/AiFieldTag';
export { FormModal } from '../../../../client/shared/Spaarke.UI.Components/src/components/SprkModal/presets/FormModal';
export { RecordRow } from '../../../../client/shared/Spaarke.UI.Components/src/components/ConsoleKit/RecordRow';
export type { RecordClass } from '../../../../client/shared/Spaarke.UI.Components/src/components/ConsoleKit/consoleStatus';
export { WizardShell } from '../../../../client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell';
export type {
  IWizardShellHandle,
  IWizardStepConfig,
  IWizardSuccessConfig,
} from '../../../../client/shared/Spaarke.UI.Components/src/components/Wizard/wizardShellTypes';
export type { IPdfHighlight, PdfBytes } from '../../../../client/shared/Spaarke.UI.Components/src/components/FilePreview/PdfHighlightViewer';
export type { IPdfNormalisedBox } from '../../../../client/shared/Spaarke.UI.Components/src/components/FilePreview/pdfHighlightGeometry';

/** Stand-in for 108's viewer: renders the highlight as data and loads the bytes once through the page's loader. */
export function PdfHighlightViewer(props: IPdfHighlightViewerProps) {
  const { loadData, highlight } = props;
  const [state, setState] = React.useState<'loading' | 'loaded' | 'error'>('loading');
  React.useEffect(() => {
    let cancelled = false;
    if (!loadData) return undefined;
    loadData().then(
      () => {
        if (!cancelled) setState('loaded');
      },
      () => {
        if (!cancelled) setState('error');
      }
    );
    return () => {
      cancelled = true;
    };
  }, [loadData]);
  const h: IPdfHighlight | null = highlight ?? null;
  return React.createElement('div', {
    'data-testid': 'pdf-viewer',
    'data-state': state,
    'data-highlight': h ? JSON.stringify(h) : '',
  });
}
