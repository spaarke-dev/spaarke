import { useState, useCallback, useEffect } from 'react';
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { useThemeDetection } from './hooks/useThemeDetection';
import { useHostBridge } from './hooks/useHostBridge';
import { BuilderLayout } from './components/BuilderLayout';

const useStyles = makeStyles({
  root: {
    height: '100vh',
    width: '100vw',
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
});

export function App() {
  const systemIsDarkMode = useThemeDetection();
  const styles = useStyles();

  // Host theme override (from PCF host via postMessage)
  const [hostTheme, setHostTheme] = useState<'light' | 'dark' | null>(null);

  // Handle theme changes from host
  const handleThemeChange = useCallback((theme: 'light' | 'dark') => {
    setHostTheme(theme);
  }, []);

  // Initialize host bridge (handles INIT, dirty state sync, save)
  const { isEmbedded } = useHostBridge({
    onThemeChange: handleThemeChange,
  });

  // Use host theme if embedded, otherwise use system detection
  const isDarkMode = hostTheme !== null ? hostTheme === 'dark' : systemIsDarkMode;
  const theme = isDarkMode ? webDarkTheme : webLightTheme;

  // Log embedding status on mount
  useEffect(() => {
    console.info('[App] Running in', isEmbedded ? 'embedded mode (iframe)' : 'standalone mode');
  }, [isEmbedded]);

  return (
    <FluentProvider theme={theme} className={styles.root}>
      <BuilderLayout />
    </FluentProvider>
  );
}
