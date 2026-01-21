import React, { useEffect, useState } from 'react';
import {
  makeStyles,
  tokens,
  Card,
  CardHeader,
  Text,
  Body1,
  ProgressBar,
  Badge,
  Spinner,
  MessageBar,
  MessageBarBody,
  Button,
} from '@fluentui/react-components';
import {
  CheckmarkCircleRegular,
  ErrorCircleRegular,
  ClockRegular,
  ArrowSyncRegular,
} from '@fluentui/react-icons';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  jobCard: {
    marginBottom: tokens.spacingVerticalS,
  },
  jobHeader: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  stageList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalS,
  },
  stageItem: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalXS,
  },
  stageIcon: {
    width: '20px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
  },
  actions: {
    display: 'flex',
    flexDirection: 'row',
    justifyContent: 'flex-end',
    marginTop: tokens.spacingVerticalS,
  },
});

export interface StatusViewProps {
  /** Callback to fetch active jobs */
  onFetchJobs?: () => Promise<ProcessingJob[]>;
  /** Auto-refresh interval in ms (0 to disable) */
  refreshInterval?: number;
  /** Error message */
  error?: string | null;
}

export interface ProcessingJob {
  id: string;
  jobType: 'DocumentSave' | 'EmailSave' | 'ShareLinks' | 'ProfileSummary' | 'DeepAnalysis';
  status: 'Pending' | 'InProgress' | 'Completed' | 'Failed' | 'Cancelled';
  progress: number;
  currentStage?: string;
  stages?: string[];
  stageStatus?: Record<string, { status: string; completedAt?: string }>;
  errorMessage?: string;
  startedDate?: string;
  completedDate?: string;
}

type BadgeColor = 'informative' | 'success' | 'danger' | 'warning';

const statusColors: Record<string, BadgeColor> = {
  Pending: 'informative',
  InProgress: 'informative',
  Completed: 'success',
  Failed: 'danger',
  Cancelled: 'warning',
};

function getStatusColor(status: string): BadgeColor {
  return statusColors[status] || 'informative';
}

const StageIcon: React.FC<{ status: string }> = ({ status }) => {
  switch (status) {
    case 'Completed':
      return <CheckmarkCircleRegular style={{ color: tokens.colorPaletteGreenForeground1 }} />;
    case 'InProgress':
      return <Spinner size="tiny" />;
    case 'Failed':
      return <ErrorCircleRegular style={{ color: tokens.colorPaletteRedForeground1 }} />;
    default:
      return <ClockRegular style={{ color: tokens.colorNeutralForeground3 }} />;
  }
};

export const StatusView: React.FC<StatusViewProps> = ({
  onFetchJobs,
  refreshInterval = 5000,
  error,
}) => {
  const styles = useStyles();
  const [jobs, setJobs] = useState<ProcessingJob[]>([]);
  const [isLoading, setIsLoading] = useState(false);

  const fetchJobs = async () => {
    if (onFetchJobs) {
      setIsLoading(true);
      try {
        const result = await onFetchJobs();
        setJobs(result);
      } finally {
        setIsLoading(false);
      }
    }
  };

  useEffect(() => {
    fetchJobs();

    if (refreshInterval > 0) {
      const intervalId = setInterval(fetchJobs, refreshInterval);
      return () => clearInterval(intervalId);
    }
    return undefined;
  }, [refreshInterval]);

  const activeJobs = jobs.filter((j) => j.status === 'Pending' || j.status === 'InProgress');
  const recentJobs = jobs.filter((j) => j.status !== 'Pending' && j.status !== 'InProgress').slice(0, 5);

  return (
    <div className={styles.container}>
      {/* Error */}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Active Jobs */}
      <Card>
        <CardHeader
          header={
            <div className={styles.jobHeader}>
              <Text weight="semibold">Active Jobs ({activeJobs.length})</Text>
              <Button
                appearance="subtle"
                icon={isLoading ? <Spinner size="tiny" /> : <ArrowSyncRegular />}
                onClick={fetchJobs}
                disabled={isLoading}
              />
            </div>
          }
        />
        {activeJobs.length === 0 ? (
          <div className={styles.emptyState}>
            <ClockRegular style={{ fontSize: '32px', marginBottom: tokens.spacingVerticalS }} />
            <Body1>No active jobs</Body1>
          </div>
        ) : (
          activeJobs.map((job) => (
            <div key={job.id} className={styles.jobCard}>
              <div className={styles.jobHeader}>
                <Body1>{job.jobType}</Body1>
                <Badge color={getStatusColor(job.status)}>{job.status}</Badge>
              </div>
              <ProgressBar value={job.progress / 100} />
              {job.currentStage && (
                <Text size={200}>Stage: {job.currentStage}</Text>
              )}

              {/* Stage List */}
              {job.stages && job.stageStatus && (
                <div className={styles.stageList}>
                  {job.stages.map((stage) => {
                    const stageInfo = job.stageStatus?.[stage];
                    return (
                      <div key={stage} className={styles.stageItem}>
                        <div className={styles.stageIcon}>
                          <StageIcon status={stageInfo?.status || 'Pending'} />
                        </div>
                        <Text size={200}>{stage}</Text>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          ))
        )}
      </Card>

      {/* Recent Jobs */}
      {recentJobs.length > 0 && (
        <Card>
          <CardHeader header={<Text weight="semibold">Recent Jobs</Text>} />
          {recentJobs.map((job) => (
            <div key={job.id} className={styles.jobCard}>
              <div className={styles.jobHeader}>
                <Body1>{job.jobType}</Body1>
                <Badge color={getStatusColor(job.status)}>{job.status}</Badge>
              </div>
              {job.completedDate && (
                <Text size={200}>
                  Completed: {new Date(job.completedDate).toLocaleString()}
                </Text>
              )}
              {job.status === 'Failed' && job.errorMessage && (
                <MessageBar intent="error">
                  <MessageBarBody>{job.errorMessage}</MessageBarBody>
                </MessageBar>
              )}
            </div>
          ))}
        </Card>
      )}
    </div>
  );
};
