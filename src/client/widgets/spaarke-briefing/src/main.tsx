import { createRoot } from 'react-dom/client';
import { App } from './App';
import { McpHostProvider } from '../../shared/mcp/mcpHost';

const container = document.getElementById('root');
if (!container) throw new Error('The widget root element is missing.');

// No StrictMode: it runs an effect twice in development, and the briefing's first read must be the one that takes the opening result.
createRoot(container).render(
  <McpHostProvider name="spaarke-briefing">
    <App />
  </McpHostProvider>
);
