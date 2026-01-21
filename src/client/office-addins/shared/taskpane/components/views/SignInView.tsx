import React from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Card,
  Text,
  Body1,
  Spinner,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import {
  PersonRegular,
  LockClosedRegular,
} from '@fluentui/react-icons';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingVerticalXXL,
  },
  logo: {
    fontSize: '64px',
    color: tokens.colorBrandForeground1,
    marginBottom: tokens.spacingVerticalM,
  },
  title: {
    textAlign: 'center',
    marginBottom: tokens.spacingVerticalS,
  },
  description: {
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
    maxWidth: '280px',
    marginBottom: tokens.spacingVerticalL,
  },
  card: {
    width: '100%',
    maxWidth: '320px',
    padding: tokens.spacingVerticalL,
    textAlign: 'center',
  },
  features: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginTop: tokens.spacingVerticalM,
    textAlign: 'left',
  },
  featureItem: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  errorMessage: {
    width: '100%',
    maxWidth: '320px',
  },
});

export interface SignInViewProps {
  /** Callback when user clicks sign in */
  onSignIn?: () => Promise<void>;
  /** Whether sign in is in progress */
  isLoading?: boolean;
  /** Error message from failed sign in */
  error?: string | null;
}

export const SignInView: React.FC<SignInViewProps> = ({
  onSignIn,
  isLoading = false,
  error,
}) => {
  const styles = useStyles();

  const handleSignIn = async () => {
    if (onSignIn) {
      await onSignIn();
    }
  };

  return (
    <div className={styles.container}>
      {/* Logo/Icon */}
      <div className={styles.logo}>
        <LockClosedRegular />
      </div>

      {/* Welcome Text */}
      <Text size={500} weight="semibold" className={styles.title}>
        Welcome to Spaarke
      </Text>
      <Body1 className={styles.description}>
        Sign in with your Microsoft account to access document management features.
      </Body1>

      {/* Error Message */}
      {error && (
        <MessageBar intent="error" className={styles.errorMessage}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Sign In Card */}
      <Card className={styles.card}>
        <Button
          appearance="primary"
          size="large"
          icon={isLoading ? <Spinner size="tiny" /> : <PersonRegular />}
          onClick={handleSignIn}
          disabled={isLoading}
          style={{ width: '100%' }}
        >
          {isLoading ? 'Signing in...' : 'Sign in with Microsoft'}
        </Button>

        {/* Features List */}
        <div className={styles.features}>
          <Text weight="semibold">With Spaarke you can:</Text>
          <div className={styles.featureItem}>
            <Text size={200}>Save emails and documents to Spaarke DMS</Text>
          </div>
          <div className={styles.featureItem}>
            <Text size={200}>Share documents with secure links</Text>
          </div>
          <div className={styles.featureItem}>
            <Text size={200}>Manage document access permissions</Text>
          </div>
          <div className={styles.featureItem}>
            <Text size={200}>Track processing status in real-time</Text>
          </div>
        </div>
      </Card>
    </div>
  );
};
