/**
 * Disclosure — an inline reveal: a small transparent button with a chevron that shows or hides a block of detail,
 * for example "Why this was flagged". Not Fluent `Accordion`: its header is a full-width heading row that sets
 * `aria-expanded` but no `aria-controls`, and it cannot sit inline beside other content as a quiet text button.
 *
 * Closed content is not mounted. Context-agnostic (ADR-012): the label and the body come from the caller.
 * React-16-safe: no `useId` (ids come from a module counter), no `useSyncExternalStore`, no `createRoot`.
 *
 * Task: ebilling-r1, task 109.
 */

import * as React from 'react';
import { Button, makeStyles, tokens } from '@fluentui/react-components';
import { ChevronDown16Regular, ChevronRight16Regular } from '@fluentui/react-icons';

export interface DisclosureProps {
  /** The button text, for example "Why this was flagged". */
  label: string;
  /** The revealed content. */
  children: React.ReactNode;
  /** Start expanded. Default: collapsed. */
  defaultOpen?: boolean;
  className?: string;
}

const useStyles = makeStyles({
  button: {
    paddingLeft: 0,
    minWidth: 0,
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground2,
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingTop: tokens.spacingVerticalXS,
  },
});

let disclosureSeq = 0;

export const Disclosure: React.FC<DisclosureProps> = ({ label, children, defaultOpen = false, className }) => {
  const styles = useStyles();
  const [open, setOpen] = React.useState<boolean>(defaultOpen);
  // A stable id for aria-controls without the React 18 id hook (React 16/17 hosts). Allocated once per instance.
  const idRef = React.useRef<string>('');
  if (!idRef.current) {
    disclosureSeq += 1;
    idRef.current = `sprk-disclosure-${disclosureSeq}`;
  }
  const bodyId = idRef.current;

  return (
    <div className={className} data-testid="disclosure">
      <Button
        appearance="transparent"
        size="small"
        className={styles.button}
        icon={open ? <ChevronDown16Regular /> : <ChevronRight16Regular />}
        aria-expanded={open}
        aria-controls={bodyId}
        onClick={() => setOpen(o => !o)}
      >
        {label}
      </Button>
      {open && (
        <div id={bodyId} className={styles.body} data-testid="disclosure-body">
          {children}
        </div>
      )}
    </div>
  );
};

Disclosure.displayName = 'Disclosure';
