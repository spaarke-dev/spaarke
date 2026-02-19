import { IPortfolioHealth } from './portfolio';
import { IHealthMetrics } from './portfolio';
import { IPriorityScore, IEffortScore } from './scoring';

/** GET /api/workspace/portfolio response */
export interface IPortfolioResponse extends IPortfolioHealth {
  cachedAt: string;  // ISO timestamp
}

/** GET /api/workspace/health response */
export interface IHealthMetricsResponse extends IHealthMetrics {
  timestamp: string;  // ISO timestamp
}

/** AI Summary request */
export interface IAiSummaryRequest {
  entityType: string;
  entityId: string;
  context?: Record<string, unknown>;
}

/** AI Summary response */
export interface IAiSummaryResponse {
  summary: string;
  suggestedActions?: string[];
  confidence?: number;
}

/** BFF scoring request */
export interface IScoringRequest {
  eventId: string;
  matterId: string;
}

/** BFF scoring response */
export interface IScoringResponse {
  priority: IPriorityScore;
  effort: IEffortScore;
  calculatedAt: string;
}

/** ProblemDetails-compatible error (RFC 7807) */
export interface IApiError {
  type?: string;
  title: string;
  status: number;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}
