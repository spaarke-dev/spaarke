import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { McpHostProvider } from './mcp/mcpHost';

const container = document.getElementById('root');
if (!container) throw new Error('The widget root element is missing.');

createRoot(container).render(
  <StrictMode>
    <McpHostProvider name="spaarke-decisions">
      <App />
    </McpHostProvider>
  </StrictMode>
);
