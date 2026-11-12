import React, { useEffect, useRef, useState } from 'react';
import { FluentProvider, webDarkTheme, webLightTheme, Button, Text, tokens } from '@fluentui/react-components';
// ext-apps 2.x declares @modelcontextprotocol/client|core and zod as peer dependencies; the "-with-deps"
// entry points bundle them, which is the single-file route (risk R13, recorded in the Phase 0 measurements note).
import { useApp } from '@modelcontextprotocol/ext-apps/react-with-deps';

type Theme = 'light' | 'dark';

const started = performance.now();

export function App() {
  const [theme, setTheme] = useState<Theme>('light');
  const [toolResult, setToolResult] = useState<string>('(no tool result yet)');
  const [echo, setEcho] = useState<string>('(not called)');
  const [writeResult, setWriteResult] = useState<string>('(not called)');
  const [initMs, setInitMs] = useState<number | null>(null);
  const routeRef = useRef<string>('legal-operations');

  const { app, isConnected, error } = useApp({
    appInfo: { name: 'spaarke-spike-test', version: '0.0.1' },
    capabilities: {},
    onAppCreated: created => {
      created.ontoolresult = result => {
        const sc = result?.structuredContent as { route?: string } | undefined;
        if (sc?.route) routeRef.current = sc.route;
        setToolResult(JSON.stringify(result?.structuredContent ?? result?.content ?? null));
      };
      created.onhostcontextchanged = ctx => {
        if (ctx?.theme === 'dark' || ctx?.theme === 'light') setTheme(ctx.theme);
      };
    },
  });

  useEffect(() => {
    if (!app || !isConnected) return;
    setInitMs(Math.round(performance.now() - started));
    const initial = app.getHostContext();
    if (initial?.theme === 'dark' || initial?.theme === 'light') setTheme(initial.theme);
  }, [app, isConnected]);

  const callEcho = async () => {
    if (!app) return;
    const tool = routeRef.current === 'spike-area-b' ? 'spike_echo_b' : 'spike_echo';
    try {
      const result = await app.callServerTool({ name: tool, arguments: { message: 'hello from the widget' } });
      setEcho(JSON.stringify(result.structuredContent ?? result.content));
    } catch (e) {
      setEcho(`error: ${(e as Error).message}`);
    }
  };

  // T3: the widget calls a tool that is hidden from the model (_meta.ui.visibility = ["app"]).
  const callWriteApp = async () => {
    if (!app) return;
    try {
      const result = await app.callServerTool({
        name: 'spike_write_app',
        arguments: { title: 'Phase 0 T3', note: 'from the widget' },
      });
      setWriteResult(JSON.stringify(result.structuredContent ?? result.content));
    } catch (e) {
      setWriteResult(`error: ${(e as Error).message}`);
    }
  };

  return (
    <FluentProvider theme={theme === 'dark' ? webDarkTheme : webLightTheme}>
      <div style={{ padding: 12, background: tokens.colorNeutralBackground1, color: tokens.colorNeutralForeground1 }}>
        <Text weight="semibold" block>
          Spaarke spike test widget
        </Text>
        <Text block>
          connected: {String(isConnected)}
          {error ? ` (error: ${error.message})` : ''}
        </Text>
        <Text block>init ms (script start to connected): {initMs ?? '-'}</Text>
        <Text block>theme: {theme}</Text>
        <Text block>
          pad bytes: {String((window as unknown as { __sprkSpikePadBytes?: string }).__sprkSpikePadBytes?.length ?? 0)}
        </Text>
        <Text block>tool result: {toolResult}</Text>
        <Button appearance="primary" onClick={callEcho} disabled={!isConnected}>
          Call echo
        </Button>
        <Text block>echo result: {echo}</Text>
        <Button appearance="secondary" onClick={callWriteApp} disabled={!isConnected}>
          Call write (app-only)
        </Button>
        <Text block>write result: {writeResult}</Text>
      </div>
    </FluentProvider>
  );
}
