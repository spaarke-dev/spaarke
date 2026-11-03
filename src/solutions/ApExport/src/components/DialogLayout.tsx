/**
 * The page's layout inside the Dataverse dialog that `navigateTo` opens (ADR-050: the hostless ribbon script launches
 * with `navigateTo`, whose platform chrome carries the title and the close button, so the page renders no second
 * header). Body and footer follow the MODAL-DESIGN-SYSTEM footer contract: Cancel / Close on the left (`footerStart`),
 * the actions on the right (`footer`). Semantic tokens only (ADR-021), so it renders under the light and dark themes.
 */

import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { thinScrollbarStyle } from '@spaarke/ui-components';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  body: {
    flex: '1 1 auto',
    minHeight: 0,
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalXL}`,
    ...thinScrollbarStyle,
  },
  footer: {
    flex: '0 0 auto',
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalXL}`,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
  },
  footerEnd: {
    marginLeft: 'auto',
    display: 'flex',
    gap: tokens.spacingHorizontalS,
  },
});

export interface DialogLayoutProps {
  children: React.ReactNode;
  /** Left of the footer: Cancel or Close. */
  footerStart?: React.ReactNode;
  /** Right of the footer: the actions. */
  footer?: React.ReactNode;
}

export const DialogLayout: React.FC<DialogLayoutProps> = ({ children, footerStart, footer }) => {
  const s = useStyles();
  return (
    <div className={s.root}>
      <div className={s.body}>{children}</div>
      {(footerStart || footer) && (
        <div className={s.footer}>
          {footerStart}
          <div className={s.footerEnd}>{footer}</div>
        </div>
      )}
    </div>
  );
};
