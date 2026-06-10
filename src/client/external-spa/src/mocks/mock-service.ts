/**
 * Mock API service for local development (VITE_DEV_MOCK=true).
 * Intercepts bffApiCall requests and returns mock data from mock-data.ts.
 */

import {
  MOCK_USER,
  MOCK_PROJECTS,
  MOCK_DOCUMENTS,
  MOCK_EVENTS,
  MOCK_TODOS,
  MOCK_CONTACTS,
  MOCK_ORGANIZATIONS,
} from './mock-data';
import { ApiError } from '../types';

/** Simulate realistic API latency */
function delay<T>(value: T, ms = 400): Promise<T> {
  return new Promise(resolve => setTimeout(() => resolve(value), ms));
}

/** Wrap a list in the OData collection envelope */
function collection<T>(items: T[]): { value: T[] } {
  return { value: items };
}

export function getMockResponse<T>(path: string, options: RequestInit = {}): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase();

  // GET /api/v1/external/me
  if (method === 'GET' && path === '/api/v1/external/me') {
    return delay(MOCK_USER as unknown as T);
  }

  // GET /api/v1/external/projects
  if (method === 'GET' && path === '/api/v1/external/projects') {
    return delay(collection(MOCK_PROJECTS) as unknown as T);
  }

  // GET /api/v1/external/projects/:id
  const projectById = path.match(/^\/api\/v1\/external\/projects\/([^/]+)$/);
  if (method === 'GET' && projectById) {
    const project = MOCK_PROJECTS.find(p => p.sprk_projectid === projectById[1]);
    if (!project) throw new ApiError(404, `Mock project not found: ${projectById[1]}`);
    return delay(project as unknown as T);
  }

  // GET /api/v1/external/projects/:id/documents
  const docsByProject = path.match(/^\/api\/v1\/external\/projects\/([^/]+)\/documents/);
  if (method === 'GET' && docsByProject) {
    return delay(collection(MOCK_DOCUMENTS[docsByProject[1]] ?? []) as unknown as T);
  }

  // GET /api/v1/external/projects/:id/events
  const eventsByProject = path.match(/^\/api\/v1\/external\/projects\/([^/]+)\/events/);
  if (method === 'GET' && eventsByProject) {
    return delay(collection(MOCK_EVENTS[eventsByProject[1]] ?? []) as unknown as T);
  }

  // GET /api/v1/external/projects/:id/todos (NEW — R3 task 007)
  const todosByProject = path.match(/^\/api\/v1\/external\/projects\/([^/]+)\/todos/);
  if (method === 'GET' && todosByProject) {
    return delay(collection(MOCK_TODOS[todosByProject[1]] ?? []) as unknown as T);
  }

  // GET /api/v1/external/projects/:id/contacts
  const contactsByProject = path.match(/^\/api\/v1\/external\/projects\/([^/]+)\/contacts/);
  if (method === 'GET' && contactsByProject) {
    return delay(collection(MOCK_CONTACTS[contactsByProject[1]] ?? []) as unknown as T);
  }

  // GET /api/v1/external/projects/:id/organizations
  const orgsByProject = path.match(/^\/api\/v1\/external\/projects\/([^/]+)\/organizations/);
  if (method === 'GET' && orgsByProject) {
    return delay(collection(MOCK_ORGANIZATIONS[orgsByProject[1]] ?? []) as unknown as T);
  }

  // POST /api/v1/external/projects/:id/events (create event)
  const createEvent = path.match(/^\/api\/v1\/external\/projects\/([^/]+)\/events$/);
  if (method === 'POST' && createEvent) {
    const body = options.body ? JSON.parse(options.body as string) : {};
    const newEvent = {
      sprk_eventid: `evt-mock-${Date.now()}`,
      sprk_name: body.sprk_name ?? 'New Event',
      sprk_duedate: body.sprk_duedate ?? null,
      sprk_status: body.sprk_status ?? 0,
      _sprk_projectid_value: createEvent[1],
      createdon: new Date().toISOString(),
    };
    return delay(newEvent as unknown as T, 600);
  }

  // POST /api/v1/external/projects/:id/todos (create to-do — NEW, R3 task 007)
  const createTodo = path.match(/^\/api\/v1\/external\/projects\/([^/]+)\/todos$/);
  if (method === 'POST' && createTodo) {
    const body = options.body ? JSON.parse(options.body as string) : {};
    const projectId = createTodo[1];
    // BFF would populate resolver fields server-side; mock the same shape.
    const mockProject = MOCK_PROJECTS.find(p => p.sprk_projectid === projectId);
    const projectName = mockProject?.sprk_name ?? 'Unknown Project';
    const newTodo = {
      sprk_todoid: `todo-mock-${Date.now()}`,
      sprk_name: body.sprk_name ?? 'New Task',
      sprk_notes: body.sprk_notes ?? null,
      sprk_duedate: body.sprk_duedate ?? null,
      sprk_priorityscore: body.sprk_priorityscore ?? 50,
      sprk_effortscore: body.sprk_effortscore ?? 50,
      sprk_todocolumn: body.sprk_todocolumn ?? 100000000,
      sprk_todopinned: body.sprk_todopinned ?? false,
      statecode: 0, // Active
      statuscode: 1, // Open
      createdon: new Date().toISOString(),
      _sprk_regardingproject_value: projectId,
      sprk_regardingrecordid: projectId,
      sprk_regardingrecordname: projectName,
      sprk_regardingrecordurl: `/main.aspx?pagetype=entityrecord&etn=sprk_project&id=${projectId}`,
    };
    return delay(newTodo as unknown as T, 600);
  }

  // PATCH /api/v1/external/events/:id (update event)
  const updateEvent = path.match(/^\/api\/v1\/external\/events\/([^/]+)$/);
  if (method === 'PATCH' && updateEvent) {
    return delay(undefined as unknown as T, 300);
  }

  // PATCH /api/v1/external/todos/:id (update to-do — NEW, R3 task 007)
  const updateTodoPath = path.match(/^\/api\/v1\/external\/todos\/([^/]+)$/);
  if (method === 'PATCH' && updateTodoPath) {
    return delay(undefined as unknown as T, 300);
  }

  // POST /api/v1/external-access/* (grant, revoke, invite)
  if (method === 'POST' && path.startsWith('/api/v1/external-access/')) {
    if (path.endsWith('/invite')) {
      return delay(
        { contactId: 'mock-contact-new', inviteRedeemUrl: '#mock', status: 'PendingAcceptance' } as unknown as T,
        800
      );
    }
    return delay(undefined as unknown as T, 500);
  }

  console.warn(`[MockService] Unhandled mock path: ${method} ${path}`);
  throw new ApiError(404, `Mock: no handler for ${method} ${path}`);
}
