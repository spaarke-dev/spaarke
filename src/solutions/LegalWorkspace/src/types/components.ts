import { IMatter, IEvent, IProject, IDocument } from './entities';
import { IPortfolioHealth } from './portfolio';

/** Block 2 - Portfolio Health Summary */
export interface IPortfolioHealthStripProps {
  health?: IPortfolioHealth;
  isLoading: boolean;
  error?: string;
}

export interface IMetricCardProps {
  label: string;
  value: string | number;
  trend?: 'up' | 'down' | 'flat';
  severity?: 'success' | 'warning' | 'danger' | 'info';
}

/** Block 5 - My Portfolio */
export interface IMyPortfolioProps {
  activeTab: 'matters' | 'projects' | 'documents';
  onTabChange: (tab: 'matters' | 'projects' | 'documents') => void;
}

export interface IMatterItemProps {
  matter: IMatter;
  onClick?: (matterId: string) => void;
}

export interface IProjectItemProps {
  project: IProject;
  onClick?: (projectId: string) => void;
}

export interface IDocumentItemProps {
  document: IDocument;
  onClick?: (documentId: string) => void;
}

/** Shell components */
export interface IPageHeaderProps {
  title: string;
  notificationCount: number;
  onNotificationClick: () => void;
}

export interface IThemeToggleProps {
  // No props needed — uses useTheme hook internally
}

/** Block 3 - Updates Feed */
export interface IFeedItemProps {
  event: IEvent;
  onFlag?: (eventId: string) => void;
  onSummarize?: (eventId: string) => void;
}

/** Block 4 - Smart To Do */
export interface ITodoItemProps {
  event: IEvent;
  onComplete?: (eventId: string) => void;
  onDismiss?: (eventId: string) => void;
}
