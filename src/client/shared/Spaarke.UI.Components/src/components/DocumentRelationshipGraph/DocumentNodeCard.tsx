/**
 * DocumentNodeCard — the body of one node in the document relationship graph.
 *
 * Shared by the DocumentRelationshipViewer code page (`@xyflow/react` v12) and PCF
 * (`react-flow-renderer` v10) (#3093). It imports neither flow library: connection handles
 * come from the host through `renderHandle`, so each host's node component is a thin wrapper
 * that maps a {@link DocumentNodeHandleSpec} onto its library's `<Handle>`.
 *
 * Three renderings:
 * - compact (`data.compactMode`): a round icon, named through its `title`
 * - parent hub (matter / project / invoice / email): a green card
 * - document: a card with the name, relationship indicators and a preview affordance
 *
 * The preview link has no handler of its own: a click on it bubbles to the host's node click,
 * which is what opens the preview (code page) or the action bar (PCF).
 *
 * ADR-021 (Fluent v9, tokens), ADR-022 (React 16-safe: no React 18 APIs).
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Card,
  CardHeader,
  Badge,
  Caption1,
  Body1Strong,
  mergeClasses,
  Link,
} from '@fluentui/react-components';
import {
  Document20Regular,
  DocumentPdf20Regular,
  Image20Regular,
  DocumentText20Regular,
  Folder20Regular,
  Table20Regular,
  SlideText20Regular,
  Mail20Regular,
  Code20Regular,
  FolderZip20Regular,
  Video20Regular,
  DocumentQuestionMark20Regular,
  Briefcase24Regular,
  Building24Regular,
  Receipt24Regular,
  MailInbox24Regular,
  Eye16Regular,
} from '@fluentui/react-icons';
import { isParentHubNode } from './types';
import type { DocumentNodeData, DocumentRelationship, NodeType } from './types';

/** Which side of the node a connection handle sits on. */
export type DocumentNodeHandleSide = 'left' | 'right';

/** A connection handle the card asks its host to render. */
export interface DocumentNodeHandleSpec {
  /** "target" receives edges, "source" starts them. */
  type: 'source' | 'target';
  side: DocumentNodeHandleSide;
  /** Size and colour for the handle dot. */
  style: React.CSSProperties;
}

export interface DocumentNodeCardProps {
  /** Node data from the visualization API. */
  data: DocumentNodeData;
  /** Whether the node is selected on the canvas. */
  selected?: boolean;
  /**
   * Renders one connection handle with the host's flow library, e.g.
   * `spec => <Handle type={spec.type} position={spec.side === 'left' ? Position.Left : Position.Right} style={spec.style} />`.
   * Omit it to render the card without handles (tests, static previews).
   */
  renderHandle?: (spec: DocumentNodeHandleSpec) => React.ReactNode;
}

/** Icon for a file extension. */
export const getFileTypeIcon = (fileType: string): React.ReactElement => {
  switch (fileType.toLowerCase()) {
    case 'pdf':
      return <DocumentPdf20Regular />;
    case 'docx':
    case 'doc':
    case 'txt':
    case 'rtf':
      return <DocumentText20Regular />;
    case 'xlsx':
    case 'xls':
    case 'csv':
      return <Table20Regular />;
    case 'pptx':
    case 'ppt':
      return <SlideText20Regular />;
    case 'msg':
    case 'eml':
      return <Mail20Regular />;
    case 'jpg':
    case 'jpeg':
    case 'png':
    case 'gif':
    case 'svg':
    case 'bmp':
    case 'tiff':
      return <Image20Regular />;
    case 'html':
    case 'htm':
    case 'xml':
    case 'json':
      return <Code20Regular />;
    case 'zip':
    case 'rar':
    case '7z':
    case 'tar':
    case 'gz':
      return <FolderZip20Regular />;
    case 'mp4':
    case 'avi':
    case 'mov':
    case 'wmv':
    case 'mkv':
      return <Video20Regular />;
    case 'folder':
      return <Folder20Regular />;
    case 'file':
    case 'unknown':
      return <DocumentQuestionMark20Regular />;
    default:
      return <Document20Regular />;
  }
};

const getParentHubIcon = (nodeType: NodeType): React.ReactElement => {
  switch (nodeType) {
    case 'matter':
      return <Briefcase24Regular />;
    case 'project':
      return <Building24Regular />;
    case 'invoice':
      return <Receipt24Regular />;
    case 'email':
      return <MailInbox24Regular />;
    default:
      return <Document20Regular />;
  }
};

/** Display label for a parent hub node type ("Matter", "Project", ...). */
export const getParentHubLabel = (nodeType: NodeType): string => {
  switch (nodeType) {
    case 'matter':
      return 'Matter';
    case 'project':
      return 'Project';
    case 'invoice':
      return 'Invoice';
    case 'email':
      return 'Email';
    default:
      return 'Parent';
  }
};

const getRelationshipBadgeColor = (type: string): 'brand' | 'success' | 'warning' | 'informative' => {
  switch (type) {
    case 'semantic':
      return 'brand';
    case 'same_matter':
    case 'same_project':
      return 'success';
    case 'same_email':
    case 'same_thread':
      return 'warning';
    default:
      return 'informative';
  }
};

/**
 * Accessible name for an icon-only (compact) node: the document name, its primary relationship,
 * "(File only)" for orphan files and the similarity — or "Matter: name" for a hub.
 */
export function getCompactNodeTitle(data: DocumentNodeData): string {
  const isSource = data.isSource ?? false;
  const isOrphanFile = data.isOrphanFile ?? false;
  const nodeType = data.nodeType ?? (isSource ? 'source' : isOrphanFile ? 'orphan' : 'related');
  if (isParentHubNode(nodeType)) return `${getParentHubLabel(nodeType)}: ${data.name}`;
  const similarity = data.similarity ?? 0;
  return (
    data.name +
    (data.relationshipLabel ? ` • ${data.relationshipLabel}` : '') +
    (isOrphanFile ? ' (File only)' : '') +
    (similarity > 0 ? ` (${Math.round(similarity * 100)}%)` : '')
  );
}

const useStyles = makeStyles({
  nodeContainer: { minWidth: '120px', maxWidth: '160px' },
  sourceCard: {
    backgroundColor: tokens.colorBrandBackground,
    border: `2px solid ${tokens.colorBrandStroke1}`,
    boxShadow: 'none',
    '& *': { color: tokens.colorNeutralForegroundOnBrand },
  },
  relatedCard: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    boxShadow: 'none',
  },
  orphanCard: {
    backgroundColor: tokens.colorNeutralBackground2,
    border: `2px dashed ${tokens.colorNeutralStroke2}`,
    boxShadow: 'none',
    opacity: 0.9,
  },
  parentHubCard: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
    border: `2px solid ${tokens.colorPaletteGreenBorder2}`,
    boxShadow: 'none',
    minWidth: '120px',
    maxWidth: '150px',
  },
  parentHubIcon: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '28px',
    height: '28px',
    borderRadius: '50%',
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorPaletteGreenForeground1,
  },
  compactParentHubIcon: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
    border: `2px solid ${tokens.colorPaletteGreenBorder2}`,
    color: tokens.colorPaletteGreenForeground1,
    width: '48px',
    height: '48px',
  },
  compactContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    cursor: 'pointer',
  },
  compactIcon: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '40px',
    height: '40px',
    borderRadius: '50%',
  },
  compactSourceIcon: {
    backgroundColor: tokens.colorBrandBackground,
    border: `2px solid ${tokens.colorBrandStroke1}`,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  compactRelatedIcon: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    color: tokens.colorNeutralForeground1,
  },
  compactOrphanIcon: {
    backgroundColor: tokens.colorNeutralBackground2,
    border: `2px dashed ${tokens.colorNeutralStroke2}`,
    color: tokens.colorNeutralForeground3,
    opacity: 0.9,
  },
  cardHeader: { paddingBottom: tokens.spacingVerticalXS },
  icon: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '20px',
    height: '20px',
    borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorNeutralBackground3,
  },
  sourceIcon: { backgroundColor: tokens.colorBrandBackgroundPressed },
  headerContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    overflow: 'hidden',
  },
  documentName: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    maxWidth: '100px',
    fontSize: tokens.fontSizeBase100,
  },
  caption: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground3,
  },
  sourceCaption: { color: tokens.colorNeutralForegroundOnBrand, opacity: 0.8 },
  footer: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalXS,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  sourceFooter: { borderTop: `1px solid ${tokens.colorBrandStroke2}` },
  orphanBadge: { fontSize: tokens.fontSizeBase100 },
  openLink: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    fontSize: tokens.fontSizeBase100,
    textDecoration: 'none',
    color: tokens.colorBrandForegroundLink,
    cursor: 'pointer',
    marginLeft: 'auto',
    '&:hover': { textDecoration: 'underline' },
  },
  sourceOpenLink: { color: tokens.colorNeutralForegroundOnBrand, opacity: 0.9 },
  footerRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    width: '100%',
  },
  dot: {
    width: '20px',
    height: '20px',
    borderRadius: '50%',
    flexShrink: 0,
  },
  semanticDot: { backgroundColor: tokens.colorBrandStroke1 },
  greenDot: { backgroundColor: tokens.colorPaletteGreenBorder2 },
});

/** Handle dot styles (inline because the flow libraries take `style` on their handle). */
const handleStyle = (size: string, color: string): React.CSSProperties => ({
  width: size,
  height: size,
  background: color,
  border: `1px solid ${tokens.colorNeutralBackground1}`,
});

export const DocumentNodeCard: React.FC<DocumentNodeCardProps> = ({ data, selected, renderHandle }) => {
  const styles = useStyles();
  const isSource = data.isSource ?? false;
  const isOrphanFile = data.isOrphanFile ?? false;
  const fileType = data.fileType;
  const compactMode = data.compactMode ?? false;
  const nodeType = data.nodeType ?? (isSource ? 'source' : isOrphanFile ? 'orphan' : 'related');
  const isParentHub = isParentHubNode(nodeType);

  // Every rendering has a target handle on the left and a source handle on the right.
  const handles = (size: string, color: string) => ({
    before: renderHandle ? renderHandle({ type: 'target', side: 'left', style: handleStyle(size, color) }) : null,
    after: renderHandle ? renderHandle({ type: 'source', side: 'right', style: handleStyle(size, color) }) : null,
  });

  if (compactMode) {
    const h = handles('6px', isParentHub ? tokens.colorPaletteGreenBorder2 : tokens.colorBrandBackground);
    return (
      <>
        {h.before}
        <div
          className={mergeClasses(
            styles.compactContainer,
            styles.compactIcon,
            isParentHub
              ? styles.compactParentHubIcon
              : isSource
                ? styles.compactSourceIcon
                : isOrphanFile
                  ? styles.compactOrphanIcon
                  : styles.compactRelatedIcon
          )}
          title={getCompactNodeTitle(data)}
          data-testid="document-node-compact"
        >
          {isParentHub ? getParentHubIcon(nodeType) : getFileTypeIcon(fileType)}
        </div>
        {h.after}
      </>
    );
  }

  if (isParentHub) {
    const h = handles('8px', tokens.colorPaletteGreenBorder2);
    return (
      <>
        {h.before}
        <Card className={mergeClasses(styles.nodeContainer, styles.parentHubCard)} selected={selected} size="small">
          <CardHeader
            className={styles.cardHeader}
            image={<div className={styles.parentHubIcon}>{getParentHubIcon(nodeType)}</div>}
            header={
              <div className={styles.headerContent}>
                <Body1Strong className={styles.documentName}>{data.name}</Body1Strong>
                <Caption1 className={styles.caption}>{getParentHubLabel(nodeType)}</Caption1>
              </div>
            }
          />
          <div className={styles.footer}>
            <div className={styles.footerRow}>
              <div className={mergeClasses(styles.dot, styles.greenDot)} />
            </div>
          </div>
        </Card>
        {h.after}
      </>
    );
  }

  /** A coloured dot for semantic and same matter/project; a badge for the others. */
  const renderRelationshipIndicator = (rel: DocumentRelationship) => {
    if (rel.type === 'semantic') {
      return <div key={rel.type} className={mergeClasses(styles.dot, styles.semanticDot)} title="Semantic" />;
    }
    if (rel.type === 'same_matter' || rel.type === 'same_project') {
      return <div key={rel.type} className={mergeClasses(styles.dot, styles.greenDot)} title={rel.label} />;
    }
    return (
      <Badge key={rel.type} appearance="outline" color={getRelationshipBadgeColor(rel.type)} size="small">
        {rel.label}
      </Badge>
    );
  };

  const h = handles('6px', tokens.colorBrandBackground);
  return (
    <>
      {h.before}
      <Card
        className={mergeClasses(
          styles.nodeContainer,
          isSource ? styles.sourceCard : isOrphanFile ? styles.orphanCard : styles.relatedCard
        )}
        selected={selected}
        size="small"
      >
        <CardHeader
          className={styles.cardHeader}
          image={
            <div className={mergeClasses(styles.icon, isSource && styles.sourceIcon)}>{getFileTypeIcon(fileType)}</div>
          }
          header={
            <div className={styles.headerContent}>
              <Body1Strong className={styles.documentName}>{data.name}</Body1Strong>
              {isSource && <Caption1 className={mergeClasses(styles.caption, styles.sourceCaption)}>Source</Caption1>}
            </div>
          }
        />
        {!isSource && (
          <div className={styles.footer}>
            <div className={styles.footerRow}>
              {data.relationshipTypes && data.relationshipTypes.length > 0 ? (
                data.relationshipTypes.slice(0, 2).map(renderRelationshipIndicator)
              ) : isOrphanFile ? (
                <Badge className={styles.orphanBadge} appearance="outline" color="warning" size="small">
                  File only
                </Badge>
              ) : (
                <span />
              )}
              <Link className={styles.openLink} title="Preview document" aria-label="Preview document">
                <Eye16Regular />
              </Link>
            </div>
          </div>
        )}
        {isSource && (
          <div className={mergeClasses(styles.footer, styles.sourceFooter)}>
            <div className={styles.footerRow}>
              <Badge appearance="filled" color="brand" size="small">
                Source
              </Badge>
              {/* No stopPropagation: the click reaches the host's node click, as on related nodes. */}
              <Link
                className={mergeClasses(styles.openLink, styles.sourceOpenLink)}
                title="Preview document"
                aria-label="Preview document"
              >
                <Eye16Regular />
              </Link>
            </div>
          </div>
        )}
      </Card>
      {h.after}
    </>
  );
};

export default DocumentNodeCard;
