import { DragEvent, useCallback } from 'react';
import { makeStyles, tokens, Text, Title1, Card, CardHeader } from '@fluentui/react-components';
import {
  BrainCircuit20Regular,
  Branch20Regular,
  Mail20Regular,
  DocumentArrowRight20Regular,
  TaskListSquareLtr20Regular,
} from '@fluentui/react-icons';
import { Canvas } from './Canvas';
import { PropertiesPanel } from './Properties';
import type { PlaybookNodeType } from '../stores/canvasStore';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  content: {
    display: 'flex',
    flex: 1,
    overflow: 'hidden',
  },
  sidebar: {
    width: '250px',
    borderRight: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground2,
    padding: tokens.spacingHorizontalM,
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  canvas: {
    flex: 1,
  },
  sectionTitle: {
    marginBottom: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground2,
  },
  paletteItem: {
    cursor: 'grab',
    '&:active': {
      cursor: 'grabbing',
    },
  },
  nodeIcon: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '32px',
    height: '32px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  nodeIconAi: {
    backgroundColor: '#0078D4',
  },
  nodeIconCondition: {
    backgroundColor: '#FFB900',
  },
  nodeIconDelivery: {
    backgroundColor: '#107C10',
  },
  nodeIconIntegration: {
    backgroundColor: '#8764B8',
  },
});

// Palette items available for drag-and-drop
const paletteItems: Array<{
  type: PlaybookNodeType;
  label: string;
  icon: JSX.Element;
  iconStyle: string;
}> = [
  { type: 'aiAnalysis', label: 'AI Analysis', icon: <BrainCircuit20Regular />, iconStyle: 'nodeIconAi' },
  { type: 'aiCompletion', label: 'AI Completion', icon: <BrainCircuit20Regular />, iconStyle: 'nodeIconAi' },
  { type: 'condition', label: 'Condition', icon: <Branch20Regular />, iconStyle: 'nodeIconCondition' },
  { type: 'deliverOutput', label: 'Deliver Output', icon: <DocumentArrowRight20Regular />, iconStyle: 'nodeIconDelivery' },
  { type: 'createTask', label: 'Create Task', icon: <TaskListSquareLtr20Regular />, iconStyle: 'nodeIconIntegration' },
  { type: 'sendEmail', label: 'Send Email', icon: <Mail20Regular />, iconStyle: 'nodeIconIntegration' },
];

/**
 * Main layout for the Playbook Builder.
 * Consists of:
 * - Header with title and actions
 * - Left sidebar with node palette
 * - Center canvas with React Flow
 * - Right panel for node properties
 */
export function BuilderLayout() {
  const styles = useStyles();

  // Handle drag start from palette
  const onDragStart = useCallback(
    (event: DragEvent<HTMLDivElement>, type: PlaybookNodeType, label: string) => {
      event.dataTransfer.setData('application/reactflow', JSON.stringify({ type, label }));
      event.dataTransfer.effectAllowed = 'move';
    },
    []
  );

  return (
    <div className={styles.container}>
      {/* Header */}
      <header className={styles.header}>
        <Title1>Playbook Builder</Title1>
      </header>

      {/* Main Content */}
      <div className={styles.content}>
        {/* Node Palette Sidebar */}
        <aside className={styles.sidebar}>
          <Text className={styles.sectionTitle} weight="semibold">
            Node Palette
          </Text>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3, marginBottom: tokens.spacingVerticalM }}>
            Drag nodes to canvas
          </Text>

          {paletteItems.map((item) => (
            <Card
              key={item.type}
              className={styles.paletteItem}
              draggable
              onDragStart={(e) => onDragStart(e, item.type, item.label)}
            >
              <CardHeader
                image={
                  <div className={`${styles.nodeIcon} ${styles[item.iconStyle as keyof typeof styles]}`}>
                    {item.icon}
                  </div>
                }
                header={<Text weight="semibold">{item.label}</Text>}
              />
            </Card>
          ))}
        </aside>

        {/* Canvas */}
        <main className={styles.canvas}>
          <Canvas />
        </main>

        {/* Properties Panel */}
        <PropertiesPanel />
      </div>
    </div>
  );
}
