/**
 * PlaceholderWidgetBody — the shared placeholder body for every registered widget (task 012).
 *
 * All widgets registered in `widgetRegistry.ts` resolve to THIS component today ("Actual data
 * widgets are tasks 016+ — 012 registers them as placeholders with the correct entitlement/role
 * metadata" — task 012 POML notes). Task 016+ swaps a widget's `lazyLoader` for its real body;
 * the registry/render plumbing (React.lazy + Suspense, title/description props) does not change.
 *
 * Fluent v9 semantic tokens only — zero hardcoded hex (ADR-021).
 */
import * as React from 'react';
import { makeStyles, shorthands, tokens, Text, Title3 } from '@fluentui/react-components';

const useStyles = makeStyles({
  placeholder: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalXL,
    ...shorthands.border(tokens.strokeWidthThin, 'dashed', tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusLarge,
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground2,
  },
});

export interface WidgetBodyProps {
  /** Widget title (shown as the placeholder heading). */
  title: string;
  /** Widget description from its registry definition. */
  description?: string;
}

/** The component type every registry `lazyLoader` must resolve to. */
export type WidgetBodyComponent = React.ComponentType<WidgetBodyProps>;

export const PlaceholderWidgetBody: React.FC<WidgetBodyProps> = ({ title, description }) => {
  const s = useStyles();
  return (
    <div className={s.placeholder}>
      <Title3>{title}</Title3>
      <Text>
        {description ??
          'This widget is registered but its real data view has not shipped yet — it opens as a placeholder ' +
            'so the workspace shell, tabs, and entitlement gating are demonstrable ahead of the data build.'}
      </Text>
    </div>
  );
};

PlaceholderWidgetBody.displayName = 'PlaceholderWidgetBody';

export default PlaceholderWidgetBody;
