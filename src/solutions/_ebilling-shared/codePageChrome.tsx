/**
 * The chrome the e-billing code pages share (issue 2965): the themed surface with its error boundary, and the centred message and spinner
 * states. The mount into `#root` is `mountWizardPage` from `@spaarke/ui-components/utils/mountWizardPage` (same render and same log).
 *
 * Two densities, because the pages differ in where they sit: `roomy` for a page or dialog that owns its window (AP Export, Rule Review),
 * `compact` for a section embedded in a form or a tighter review page (Invoice Files, Record Procedures, Invoice Review).
 *
 * The FluentProvider carries only the theme; the surface colours sit on a wrapper div (project gotcha F-10).
 */

import * as React from 'react';
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
