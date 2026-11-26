import { createRoot } from 'react-dom/client';
import { App } from './App';
import { McpHostProvider } from '../../shared/mcp/mcpHost';

const container = document.getElementById('root');
if (!container) throw new Error('The widget root element is missing.');

createRoot(container).render(
  <McpHostProvider name="spaarke-search-results">
    <App />
  </McpHostProvider>
);
