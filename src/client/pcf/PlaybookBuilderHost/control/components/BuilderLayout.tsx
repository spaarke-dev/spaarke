/**
 * BuilderLayout - Main layout for the Playbook Builder
 *
 * Collapsible left palette (default collapsed) and right properties panel.
 * Properties panel auto-opens when a node is selected.
 */

import * as React from 'react';
import { DragEvent, useCallback, useState, useEffect } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Card,
  CardHeader,
  Button,
  shorthands,
  mergeClasses,
} from '@fluentui/react-components';
import {
  PanelLeft20Regular,
  PanelRight20Regular,
  BrainCircuit20Regular,
  Branch20Regular,
  Mail20Regular,
  DocumentArrowRight20Regular,
  TaskListSquareLtr20Regular,
  Clock20Regular,
} from '@fluentui/react-icons';
import { Canvas } from './Canvas';
import { PropertiesPanel } from './Properties';
import { useCanvasStore, type PlaybookNodeType } from '../stores/canvasStore';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    height: '100%',
    width: '100%',
    ...shorthands.overflow('hidden'),
  },
  // Sidebar toggle button
  sidebarToggle: {
    position: 'absolute',
    zIndex: 10,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke1),
    boxShadow: tokens.shadow4,
  },
  leftToggle: {
    left: '8px',
    top: '8px',
  },
  rightToggle: {
    right: '8px',
    top: '8px',
  },
  // Sidebar styles
  sidebar: {
    width: '200px',
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.padding(tokens.spacingHorizontalS),
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    transitionProperty: 'width, padding, opacity',
    transitionDuration: '0.2s',
    transitionTimingFunction: 'ease-out',
  },
  sidebarCollapsed: {
    width: '0',
    ...shorthands.padding('0'),
    opacity: 0,
    ...shorthands.overflow('hidden'),
  },
  leftSidebar: {
    ...shorthands.borderRight('1px', 'solid', tokens.colorNeutralStroke1),
  },
  rightSidebar: {
    ...shorthands.borderLeft('1px', 'solid', tokens.colorNeutralStroke1),
    width: '280px',
  },
  canvasContainer: {
    flex: 1,
    position: 'relative',
  },
  sectionTitle: {
    marginBottom: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground2,
    paddingTop: tokens.spacingVerticalS,
  },
  paletteItem: {
    cursor: 'grab',
    ':active': {
      cursor: 'grabbing',
    },
  },
  nodeIcon: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '24px',
    height: '24px',
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  nodeIconAi: {
    backgroundColor: '#0078D4',
  },
  nodeIconCondition: {
    backgroundColor: '#FFB900',
    color: '#000000',
  },
  nodeIconDelivery: {
    backgroundColor: '#107C10',
  },
  nodeIconIntegration: {
    backgroundColor: '#8764B8',
  },
  nodeIconWait: {
    backgroundColor: '#E3008C',
  },
});

// Palette items available for drag-and-drop
interface PaletteItem {
  type: PlaybookNodeType;
  label: string;
  icon: React.ReactNode;
  iconStyle:
    | 'nodeIconAi'
    | 'nodeIconCondition'
    | 'nodeIconDelivery'
    | 'nodeIconIntegration'
    | 'nodeIconWait';
}

const paletteItems: PaletteItem[] = [
  {
    type: 'aiAnalysis',
    label: 'AI Analysis',
    icon: <BrainCircuit20Regular />,
    iconStyle: 'nodeIconAi',
  },
  {
    type: 'aiCompletion',
    label: 'AI Completion',
    icon: <BrainCircuit20Regular />,
    iconStyle: 'nodeIconAi',
  },
  {
    type: 'condition',
    label: 'Condition',
    icon: <Branch20Regular />,
    iconStyle: 'nodeIconCondition',
  },
  {
    type: 'deliverOutput',
    label: 'Deliver Output',
    icon: <DocumentArrowRight20Regular />,
    iconStyle: 'nodeIconDelivery',
  },
  {
    type: 'createTask',
    label: 'Create Task',
    icon: <TaskListSquareLtr20Regular />,
    iconStyle: 'nodeIconIntegration',
  },
  {
    type: 'sendEmail',
    label: 'Send Email',
    icon: <Mail20Regular />,
    iconStyle: 'nodeIconIntegration',
  },
  {
    type: 'wait',
    label: 'Wait',
    icon: <Clock20Regular />,
    iconStyle: 'nodeIconWait',
  },
];

/**
 * Main layout for the Playbook Builder.
 * Consists of:
 * - Left collapsible sidebar with node palette (default collapsed)
 * - Center canvas with React Flow
 * - Right collapsible panel for node properties (auto-opens on selection)
 */
export const BuilderLayout = React.memo(function BuilderLayout() {
  const styles = useStyles();

  // Panel states - left collapsed by default, right auto-opens on selection
  const [leftPanelOpen, setLeftPanelOpen] = useState(false);
  const [rightPanelOpen, setRightPanelOpen] = useState(false);

  // Track selected node for auto-opening properties panel
  const selectedNodeId = useCanvasStore((state) => state.selectedNodeId);

  // Auto-open properties panel when a node is selected
  useEffect(() => {
    if (selectedNodeId) {
      setRightPanelOpen(true);
    }
  }, [selectedNodeId]);

  // Handle drag start from palette
  const onDragStart = useCallback(
    (event: DragEvent<HTMLDivElement>, type: PlaybookNodeType, label: string) => {
      event.dataTransfer.setData(
        'application/reactflow',
        JSON.stringify({ type, label })
      );
      event.dataTransfer.effectAllowed = 'move';
    },
    []
  );

  return (
    <div className={styles.container}>
      {/* Left Sidebar - Node Palette */}
      <aside
        className={mergeClasses(
          styles.sidebar,
          styles.leftSidebar,
          !leftPanelOpen && styles.sidebarCollapsed
        )}
      >
        <Text className={styles.sectionTitle} weight="semibold" size={200}>
          Drag nodes to canvas
        </Text>

        {paletteItems.map((item) => (
          <Card
            key={item.type}
            className={styles.paletteItem}
            size="small"
            draggable
            onDragStart={(e) => onDragStart(e, item.type, item.label)}
          >
            <CardHeader
              image={
                <div className={mergeClasses(styles.nodeIcon, styles[item.iconStyle])}>
                  {item.icon}
                </div>
              }
              header={<Text size={200}>{item.label}</Text>}
            />
          </Card>
        ))}
      </aside>

      {/* Canvas Container */}
      <main className={styles.canvasContainer}>
        {/* Left Toggle Button */}
        <Button
          className={mergeClasses(styles.sidebarToggle, styles.leftToggle)}
          icon={<PanelLeft20Regular />}
          appearance="subtle"
          size="small"
          onClick={() => setLeftPanelOpen(!leftPanelOpen)}
          title={leftPanelOpen ? 'Hide palette' : 'Show palette'}
          aria-label={leftPanelOpen ? 'Hide palette' : 'Show palette'}
        />

        {/* Right Toggle Button */}
        <Button
          className={mergeClasses(styles.sidebarToggle, styles.rightToggle)}
          icon={<PanelRight20Regular />}
          appearance="subtle"
          size="small"
          onClick={() => setRightPanelOpen(!rightPanelOpen)}
          title={rightPanelOpen ? 'Hide properties' : 'Show properties'}
          aria-label={rightPanelOpen ? 'Hide properties' : 'Show properties'}
        />

        <Canvas />
      </main>

      {/* Right Sidebar - Properties Panel */}
      <aside
        className={mergeClasses(
          styles.sidebar,
          styles.rightSidebar,
          !rightPanelOpen && styles.sidebarCollapsed
        )}
      >
        <PropertiesPanel />
      </aside>
    </div>
  );
});
