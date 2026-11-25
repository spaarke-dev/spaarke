/**
 * The chrome the e-billing code pages share (issue 2965): the themed surface with its error boundary, the centred message and spinner states,
 * and the mount into `#root`.
 *
 * Two densities, because the pages differ in where they sit: `roomy` for a page or dialog that owns its window (AP Export, Rule Review),
 * `compact` for a section embedded in a form or a tighter review page (Invoice Files, Record Procedures, Invoice Review).
 *
 * The FluentProvider carries only the theme; the surface colours sit on a wrapper div (project gotcha F-10).
 */

import * as React from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider, Spinner, Text, Button, makeStyles, tokens, type Theme } from '@fluentui/react-components';
import { AppErrorBoundary } from '@spaarke/ui-components';

const useStyles = makeStyles({
  centeredRoomy: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    width: '100%',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalXXL,
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  centeredCompact: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    width: '100%',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalL,
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  surface: {
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  surfaceSubtle: {
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1,
  },
});

export type CodePageDensity = 'roomy' | 'compact';

export interface CodePageSurfaceProps {
  theme: Theme;
  /** The name the error boundary shows, e.g. `"Invoice Files"`. */
  surfaceName: string;
  /** Neutral background 2 instead of 1 (a page that sits cards on its surface). */
  subtle?: boolean;
  children: React.ReactNode;
}

/** The page's FluentProvider, surface wrapper and error boundary. */
export const CodePageSurface: React.FC<CodePageSurfaceProps> = ({ theme, surfaceName, subtle = false, children }) => {
  const s = useStyles();
  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <div className={subtle ? s.surfaceSubtle : s.surface}>
        <AppErrorBoundary surfaceName={surfaceName}>{children}</AppErrorBoundary>
      </div>
    </FluentProvider>
  );
};

export interface CodePageMessageProps {
  testId: string;
  title: string;
  body: string;
  /** Shows a Retry button. */
  onRetry?: () => void;
  density?: CodePageDensity;
}

/** A centred title and sentence, with an optional Retry (the launch, sign-in and failure states). */
export const CodePageMessage: React.FC<CodePageMessageProps> = ({ testId, title, body, onRetry, density = 'compact' }) => {
  const s = useStyles();
  const roomy = density === 'roomy';
  return (
    <div className={roomy ? s.centeredRoomy : s.centeredCompact} data-testid={testId}>
      <Text weight="semibold" size={roomy ? 500 : undefined}>
        {title}
      </Text>
      <Text>{body}</Text>
      {onRetry && (
        <Button appearance="primary" size={roomy ? undefined : 'small'} onClick={onRetry}>
          Retry
        </Button>
      )}
    </div>
  );
};

export interface CodePageSpinnerProps {
  testId: string;
  label: string;
  density?: CodePageDensity;
}

/** A centred spinner (the bootstrapping state). */
export const CodePageSpinner: React.FC<CodePageSpinnerProps> = ({ testId, label, density = 'compact' }) => {
  const s = useStyles();
  const roomy = density === 'roomy';
  return (
    <div className={roomy ? s.centeredRoomy : s.centeredCompact} data-testid={testId}>
      <Spinner size={roomy ? 'large' : 'small'} label={label} />
    </div>
  );
};

/** Render the page into `#root` under StrictMode; log (never throw) when the host page has no root element. */
export function mountCodePage(label: string, page: React.ReactElement): void {
  const rootElement = document.getElementById('root');
  if (rootElement) {
    createRoot(rootElement).render(<React.StrictMode>{page}</React.StrictMode>);
  } else {
    // eslint-disable-next-line no-console
    console.error(`[${label}] Root element not found`);
  }
}
