/**
 * ActivityFeed barrel export — Block 3: Updates Feed.
 *
 * Primary consumer: WorkspaceGrid (left column, position 3).
 * Pass `webApi` and `userId` from PCF context; optionally pass `mockEvents`
 * for development or testing without a live Dataverse connection.
 */

export { ActivityFeed } from "./ActivityFeed";
export type { IActivityFeedProps } from "./ActivityFeed";

export { FilterBar } from "./FilterBar";
export type { IFilterBarProps } from "./FilterBar";

export { ActivityFeedList } from "./ActivityFeedList";
export type { IActivityFeedListProps } from "./ActivityFeedList";

export { ActivityFeedEmptyState } from "./EmptyState";
export type { IActivityFeedEmptyStateProps } from "./EmptyState";

export { FeedItemCard } from "./FeedItemCard";
export type { IFeedItemCardProps } from "./FeedItemCard";

export { AISummaryDialog } from "./AISummaryDialog";
export type { IAISummaryDialogProps } from "./AISummaryDialog";
