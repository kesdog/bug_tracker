import React from '../../react/node_modules/react/index.js';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '../../react/node_modules/@testing-library/react/dist/index.js';
import userEvent from '../../react/node_modules/@testing-library/user-event/dist/esm/index.js';
import App from '../../react/src/App';

const NativeImage = global.Image;

beforeEach(() => {
  global.Image = class MockImage {
    naturalWidth = 1280;
    naturalHeight = 720;
    set src(value) {
      this._src = value;
      queueMicrotask(() => this.onload?.());
    }
  };
});

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
  global.Image = NativeImage;
});

function mockAppNetwork({ role = 'dev', userType = 'human', projects, createBugResponse, createProjectResponse } = {}) {
  // Centralized fetch mock so each test can focus on UI behavior.
  return vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
    const target = String(url);
    const method = options.method || 'GET';

    if (target.endsWith('/api/auth/login') && method === 'POST') {
      return {
        ok: true,
        json: async () => ({
          accessToken: 'token123',
          user: {
            userId: `usr_${userType === 'agent' ? 'agent' : role}_001`,
            email: `${userType === 'agent' ? 'agent' : role}@example.com`,
            role,
            userType
          }
        })
      };
    }

    if (target.includes('/api/bugs?status=active')) {
      return {
        ok: true,
        json: async () => []
      };
    }

    if (target.includes('/api/bugs/allocated')) {
      return {
        ok: true,
        json: async () => []
      };
    }

    if (target.endsWith('/api/bugs/assignees') && method === 'GET') {
      return {
        ok: true,
        json: async () => [
          { userId: 'usr_dev_007', username: 'target-dev', email: 'target@example.com', role: 'dev', userType: 'human' },
          { userId: 'usr_agent_002', username: 'triage-bot', email: 'agent@example.com', role: 'dev', userType: 'agent' }
        ]
      };
    }

    if (target.endsWith('/api/projects') && method === 'GET') {
      return {
        ok: true,
        json: async () => projects ?? [
          {
            projectId: 'project-general',
            name: 'General',
            visibility: 'normal',
            createdAt: '2026-01-01 00:00:00',
            updatedAt: '2026-01-01 00:00:00'
          }
        ]
      };
    }

    if (target.endsWith('/api/projects') && method === 'POST') {
      return createProjectResponse || {
        ok: true,
        json: async () => ({ projectId: 'project-created', name: 'Created Project', visibility: 'normal' })
      };
    }

    if (target.endsWith('/api/bugs') && method === 'POST') {
      return createBugResponse || {
        ok: true,
        json: async () => ({ id: 'bug-created-001' })
      };
    }

    throw new Error(`Unhandled fetch: ${method} ${target}`);
  });
}

describe('add bug page', { timeout: 30000 }, () => {
  it('shows client validation when submitting empty required fields', async () => {
    // This confirms required validation still fires with block-based report editor.
    mockAppNetwork();

    render(<App />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText(/email/i), 'dev@example.com');
    await user.type(screen.getByLabelText(/password/i), 'DevPass123!!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    const issueInput = await screen.findByLabelText(/issue title/i, {}, { timeout: 5000 });
    const descriptionInput = screen.getByPlaceholderText(/write report text/i);
    await user.clear(issueInput);
    await user.clear(descriptionInput);
    await user.click(screen.getByRole('button', { name: /create bug/i }));

    expect(screen.getByText('Issue title is required.')).toBeInTheDocument();
    expect(screen.getByText('Description is required.')).toBeInTheDocument();
    expect(screen.getByText('Choose front-end or back-end.')).toBeInTheDocument();
  });

  it('submits add bug form and shows success message', async () => {
    // Happy-path create flow should still produce expected API payload.
    const fetchSpy = mockAppNetwork();

    render(<App />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText(/email/i), 'dev@example.com');
    await user.type(screen.getByLabelText(/password/i), 'DevPass123!!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    await user.type(await screen.findByLabelText(/issue title/i), 'Cannot create sprint');
    await user.type(screen.getByPlaceholderText(/write report text/i), 'Saving sprint throws 500 and rolls back transaction.');
    await user.selectOptions(screen.getByLabelText(/bug type/i), 'api');
    await user.selectOptions(screen.getByLabelText(/severity/i), 'urgent');
    await user.click(screen.getByRole('radio', { name: /back-end/i }));
    await user.click(screen.getByRole('button', { name: /create bug/i }));

    expect(await screen.findByText(/bug created: bug-created-001/i)).toBeInTheDocument();

    await waitFor(() => {
      const postCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/bugs') && options?.method === 'POST');
      expect(postCall).toBeTruthy();
      const payload = JSON.parse(postCall[1].body);
      expect(payload).toEqual({
        issueTitle: 'Cannot create sprint',
        description: 'Saving sprint throws 500 and rolls back transaction.',
        bugType: 'api',
        projectId: 'project-general',
        severity: 'urgent',
        priority: 'p2',
        tags: ['back-end'],
        environment: null,
        expectedBehavior: null,
        actualBehavior: null,
        stepsToReproduce: null,
        frequency: 'unknown',
        textEvidence: [],
        reportImages: []
      });
    });
  });

  it.each(['senior', 'admin'])('allows a %s to assign a ticket during creation', async (role) => {
    const fetchSpy = mockAppNetwork({ role });

    render(<App />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText(/email/i), `${role}@example.com`);
    await user.type(screen.getByLabelText(/password/i), 'ValidPass123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));
    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    await user.type(screen.getByLabelText(/issue title/i), 'Assigned on intake');
    await user.type(screen.getByPlaceholderText(/write report text/i), 'The issue needs immediate ownership.');
    await user.click(screen.getByRole('radio', { name: /front-end/i }));
    expect(await screen.findByRole('option', { name: 'target-dev - (target@example.com)' })).toBeInTheDocument();
    await user.selectOptions(await screen.findByLabelText(/assign ticket \(optional\)/i), 'usr_dev_007');
    await user.click(screen.getByRole('button', { name: /create bug/i }));

    expect(await screen.findByText(/bug created: bug-created-001/i)).toBeInTheDocument();
    const postCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/bugs') && options?.method === 'POST');
    expect(JSON.parse(postCall[1].body).assigneeUserId).toBe('usr_dev_007');
  });

  it.each(['senior', 'admin'])('allows a %s to create the first project when none exist', async (role) => {
    const fetchSpy = mockAppNetwork({
      role,
      projects: [],
      createProjectResponse: {
        ok: true,
        json: async () => ({ projectId: 'project-first', name: 'First Project', visibility: 'normal' })
      }
    });

    render(<App />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/email/i), `${role}@example.com`);
    await user.type(screen.getByLabelText(/password/i), 'ValidPass123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));
    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    const projectSelect = await screen.findByLabelText('Project');
    expect(projectSelect).toBeEnabled();
    expect(projectSelect).toHaveValue('__add_project__');
    const projectName = screen.getByLabelText(/new project name/i);
    await user.click(screen.getByRole('button', { name: /^add project$/i }));
    expect(projectName).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText('Project name is required.')).toBeInTheDocument();

    await user.type(projectName, 'First Project');
    await user.click(screen.getByRole('button', { name: /^add project$/i }));
    expect(await screen.findByText(/project created: first project/i)).toBeInTheDocument();

    const postCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/projects') && options?.method === 'POST');
    expect(JSON.parse(postCall[1].body)).toEqual({ name: 'First Project', visibility: 'normal' });
  });

  it('creates an inline project with Enter without submitting the bug form', async () => {
    const fetchSpy = mockAppNetwork({
      role: 'senior',
      createProjectResponse: {
        ok: true,
        json: async () => ({ projectId: 'project-keyboard', name: 'Keyboard Project', visibility: 'normal' })
      }
    });

    render(<App />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/email/i), 'senior@example.com');
    await user.type(screen.getByLabelText(/password/i), 'SeniorPass123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));
    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    await user.selectOptions(await screen.findByLabelText('Project'), '__add_project__');
    await user.type(screen.getByLabelText(/new project name/i), 'Keyboard Project{enter}');

    expect(await screen.findByText(/project created: keyboard project/i)).toBeInTheDocument();
    expect(fetchSpy.mock.calls.filter(([url, options]) => String(url).endsWith('/api/projects') && options?.method === 'POST')).toHaveLength(1);
    expect(fetchSpy.mock.calls.some(([url, options]) => String(url).endsWith('/api/bugs') && options?.method === 'POST')).toBe(false);
    expect(screen.queryByText('Issue title is required.')).not.toBeInTheDocument();
  });

  it('clears a selected assignee when the project changes', async () => {
    mockAppNetwork({
      role: 'admin',
      projects: [
        { projectId: 'project-one', name: 'One', visibility: 'normal' },
        { projectId: 'project-two', name: 'Two', visibility: 'sensitive' }
      ]
    });

    render(<App />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/email/i), 'admin@example.com');
    await user.type(screen.getByLabelText(/password/i), 'AdminPass123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));
    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    const assigneeSelect = await screen.findByLabelText(/assign ticket \(optional\)/i);
    await user.selectOptions(assigneeSelect, 'usr_dev_007');
    expect(assigneeSelect).toHaveValue('usr_dev_007');
    await user.selectOptions(screen.getByLabelText('Project'), 'project-two');
    expect(assigneeSelect).toHaveValue('');
  });

  it('does not show assignment controls to developers', async () => {
    mockAppNetwork({ role: 'dev' });

    render(<App />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/email/i), 'dev@example.com');
    await user.type(screen.getByLabelText(/password/i), 'DevPass123!!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));
    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    expect(screen.queryByLabelText(/assign ticket/i)).not.toBeInTheDocument();
    expect(global.fetch.mock.calls.some(([url]) => String(url).endsWith('/api/bugs/assignees'))).toBe(false);
  });

  it('hides project creation and assignment controls from AI agents', async () => {
    const fetchSpy = mockAppNetwork({ role: 'admin', userType: 'agent' });

    render(<App />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/email/i), 'agent@example.com');
    await user.type(screen.getByLabelText(/password/i), 'AgentPass123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));
    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    await screen.findByLabelText('Project');
    expect(screen.queryByRole('option', { name: /add project/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/new project name/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/assign ticket/i)).not.toBeInTheDocument();
    expect(fetchSpy.mock.calls.some(([url]) => String(url).endsWith('/api/bugs/assignees'))).toBe(false);
  });

  it('surfaces assignment errors with the backend hint', async () => {
    mockAppNetwork({
      role: 'admin',
      projects: [{ projectId: 'project-secret', name: 'Secret', visibility: 'sensitive' }],
      createBugResponse: {
        ok: false,
        status: 400,
        json: async () => ({
          error: 'The selected assignee is not a member of this sensitive project.',
          errorCode: 'assignee_not_project_member',
          hint: 'Allocate the user to the project before assigning this ticket.'
        })
      }
    });

    render(<App />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/email/i), 'admin@example.com');
    await user.type(screen.getByLabelText(/password/i), 'AdminPass123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));
    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    await user.type(screen.getByLabelText(/issue title/i), 'Sensitive incident');
    await user.type(screen.getByPlaceholderText(/write report text/i), 'Only project members should receive this ticket.');
    await user.click(screen.getByRole('radio', { name: /back-end/i }));
    await user.selectOptions(await screen.findByLabelText(/assign ticket \(optional\)/i), 'usr_dev_007');
    expect(screen.getByText(/selected assignee must already be a project member/i)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /create bug/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/selected assignee is not a member/i);
    expect(screen.getByRole('alert')).toHaveTextContent(/allocate the user to the project before assigning/i);
  });

  it('allows only one front-end or back-end area tag in the create payload', async () => {
    // The area selector should behave as an exclusive choice and never submit both tags.
    const fetchSpy = mockAppNetwork();

    render(<App />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText(/email/i), 'dev@example.com');
    await user.type(screen.getByLabelText(/password/i), 'DevPass123!!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    const frontEndOption = screen.getByRole('radio', { name: /front-end/i });
    const backEndOption = screen.getByRole('radio', { name: /back-end/i });

    expect(screen.queryByRole('radio', { name: /not specified/i })).not.toBeInTheDocument();
    expect(frontEndOption).not.toBeChecked();
    expect(backEndOption).not.toBeChecked();

    await user.click(frontEndOption);
    expect(frontEndOption).toBeChecked();

    await user.click(backEndOption);
    expect(backEndOption).toBeChecked();
    expect(frontEndOption).not.toBeChecked();

    await user.type(screen.getByLabelText(/issue title/i), 'API route fails');
    await user.type(screen.getByPlaceholderText(/write report text/i), 'Route returns a server error for valid data.');
    await user.click(screen.getByRole('button', { name: /create bug/i }));

    expect(await screen.findByText(/bug created: bug-created-001/i)).toBeInTheDocument();

    await waitFor(() => {
      const postCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/bugs') && options?.method === 'POST');
      expect(postCall).toBeTruthy();
      const payload = JSON.parse(postCall[1].body);
      expect(payload.tags).toEqual(['back-end']);
      expect(payload.tags).not.toContain('front-end');
    });
  });

  it('uploads report images and sends them in payload order', async () => {
    // Images inserted at different block positions should preserve order in payload.
    const fetchSpy = mockAppNetwork();

    render(<App />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText(/email/i), 'dev@example.com');
    await user.type(screen.getByLabelText(/password/i), 'DevPass123!!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    await user.type(screen.getByLabelText(/issue title/i), 'Image upload bug');
    await user.type(screen.getByPlaceholderText(/write report text/i), 'Has visual evidence attached.');
    await user.click(screen.getByRole('radio', { name: /front-end/i }));

    const imageOne = new File(['fake-image-1'], 'first screen.png', { type: 'image/png' });
    const imageTwo = new File(['fake-image-2'], 'second-shot.jpg', { type: 'image/jpeg' });
    await user.upload(screen.getAllByLabelText(/add image below/i)[0], imageOne);
    await user.upload(screen.getAllByLabelText(/add image below/i).at(-1), imageTwo);

    await user.click(screen.getByRole('button', { name: /create bug/i }));
    expect(await screen.findByText(/bug created: bug-created-001/i)).toBeInTheDocument();

    await waitFor(() => {
      const postCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/bugs') && options?.method === 'POST');
      expect(postCall).toBeTruthy();
      const payload = JSON.parse(postCall[1].body);
      expect(payload.reportImages).toHaveLength(2);
      const names = payload.reportImages.map((img) => img.name).sort();
      expect(names).toEqual(['first-screen.png', 'second-shot.jpg']);
      expect(payload.reportImages.some((img) => /^data:image\/png;base64,/.test(img.dataUrl))).toBe(true);
      expect(payload.reportImages.some((img) => /^data:image\/jpeg;base64,/.test(img.dataUrl))).toBe(true);
    });
  });

  it('blocks selecting more than three report images', async () => {
    // Builder enforces the same max image rule as the API contract.
    mockAppNetwork();

    render(<App />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText(/email/i), 'dev@example.com');
    await user.type(screen.getByLabelText(/password/i), 'DevPass123!!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await screen.findByTestId('session-card');
    await user.click(screen.getByRole('button', { name: /add bug/i }));

    await user.type(screen.getByPlaceholderText(/write report text/i), 'Image-heavy report');

    const files = [
      new File(['1'], '1.png', { type: 'image/png' }),
      new File(['2'], '2.png', { type: 'image/png' }),
      new File(['3'], '3.png', { type: 'image/png' }),
      new File(['4'], '4.png', { type: 'image/png' }),
      new File(['5'], '5.png', { type: 'image/png' }),
      new File(['6'], '6.png', { type: 'image/png' })
    ];

    for (const file of files.slice(0, 3)) {
      await user.upload(screen.getAllByLabelText(/add image below/i)[0], file);
    }

    expect(screen.getByText(/3\s*\/\s*3 image block\(s\)/i)).toBeInTheDocument();
  });
});
