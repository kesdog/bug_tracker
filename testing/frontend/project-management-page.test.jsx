import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '../../react/node_modules/@testing-library/react/dist/index.js';
import userEvent from '../../react/node_modules/@testing-library/user-event/dist/esm/index.js';
import ProjectManagementPage from '../../react/src/pages/ProjectManagementPage';

afterEach(() => {
  vi.restoreAllMocks();
});

describe('project management allocation constraints', () => {
  it('shows explicit project allocations and removes them from the user dropdown', async () => {
    vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ projectId: 'project-web-app', name: 'Web App', visibility: 'normal' }]
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [
          { userId: 'usr_dev_001', username: 'frontend-dev', email: 'dev@example.com', role: 'dev', userType: 'human' },
          { userId: 'usr_ai_001', username: 'triage-bot', email: 'ai@example.com', role: 'dev', userType: 'agent' },
          { userId: 'usr_senior_001', username: 'senior-dev', email: 'senior@example.com', role: 'senior', userType: 'human' }
        ]
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ projectId: 'project-web-app', projectName: 'Web App', userIds: ['usr_dev_001', 'usr_ai_001'] }]
      });

    render(<ProjectManagementPage token="admin-token" userRole="admin" />);

    const explicitAllocations = await screen.findByRole('region', { name: /explicit project allocations/i });
    expect(explicitAllocations).toHaveTextContent('frontend-dev - (dev@example.com)');
    expect(explicitAllocations).toHaveTextContent('triage-bot - (ai@example.com)');
    expect(explicitAllocations).toHaveTextContent(/senior developers and admins may also have effective access/i);
    expect(screen.queryByRole('option', { name: /frontend-dev/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /triage-bot/i })).not.toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'senior-dev - (senior@example.com)' })).toBeInTheDocument();
  });

  it('shows an actionable backend error when visibility cannot be changed', async () => {
    vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ projectId: 'project-web-app', name: 'Web App', visibility: 'normal' }]
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => []
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ projectId: 'project-web-app', projectName: 'Web App', userIds: [] }]
      })
      .mockResolvedValueOnce({
        ok: false,
        status: 400,
        json: async () => ({
          error: 'All current assignees must be project members before making this project sensitive.',
          hint: 'Allocate the missing assignees, then try again.'
        })
      });

    render(<ProjectManagementPage token="admin-token" userRole="admin" />);

    const user = userEvent.setup();
    await screen.findByText('Normal project');
    await user.selectOptions(screen.getByLabelText('Project visibility'), 'sensitive');
    await user.click(screen.getByRole('button', { name: /save visibility/i }));

    expect(await screen.findByText(/all current assignees must be project members/i)).toHaveTextContent(/allocate the missing assignees, then try again/i);
  });

  it('makes sensitive allocations read-only for seniors while allowing normal-project allocation', async () => {
    vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [
          { projectId: 'project-secret', name: 'Secret', visibility: 'sensitive' },
          { projectId: 'project-normal', name: 'Normal Work', visibility: 'normal' }
        ]
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ userId: 'usr_dev_001', username: 'frontend-dev', email: 'dev@example.com', role: 'dev', userType: 'human' }]
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [
          { projectId: 'project-secret', projectName: 'Secret', userIds: ['usr_dev_001'] },
          { projectId: 'project-normal', projectName: 'Normal Work', userIds: [] }
        ]
      });

    render(<ProjectManagementPage token="senior-token" userRole="senior" userType="human" />);
    const user = userEvent.setup();

    const allocations = await screen.findByRole('region', { name: /explicit project allocations/i });
    expect(allocations).toHaveTextContent('frontend-dev - (dev@example.com)');
    expect(screen.getByText(/allocations are read-only for senior developers/i)).toBeInTheDocument();
    expect(screen.queryByLabelText('User')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /allocate user/i })).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Project'), 'project-normal');
    expect(screen.getByLabelText('User')).toBeEnabled();
    expect(screen.getByRole('button', { name: /allocate user/i })).toBeEnabled();
  });

  it('lets admins change an existing project visibility and shows the sensitive warning', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ projectId: 'project-web-app', name: 'Web App', visibility: 'normal' }]
      })
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ projectId: 'project-web-app', projectName: 'Web App', userIds: [] }]
      })
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({ projectId: 'project-web-app', name: 'Web App', visibility: 'sensitive' })
      });

    render(<ProjectManagementPage token="admin-token" userRole="admin" />);
    const user = userEvent.setup();

    expect(await screen.findByText('Normal project')).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Project visibility'), 'sensitive');
    expect(screen.getByText(/confirm that all current ticket assignees are allocated/i)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /save visibility/i }));

    expect(await screen.findByText(/web app is now sensitive/i)).toBeInTheDocument();
    const patchCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/projects/project-web-app/visibility') && options?.method === 'PATCH');
    expect(JSON.parse(patchCall[1].body)).toEqual({ visibility: 'sensitive' });
  });

  it('lets admins choose sensitive visibility while creating a project', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ projectId: 'project-secret', name: 'Secret Work', visibility: 'sensitive' })
      });

    render(<ProjectManagementPage token="admin-token" userRole="admin" />);
    const user = userEvent.setup();

    await user.selectOptions(await screen.findByLabelText('Project'), '__add_project__');
    await user.type(screen.getByLabelText(/new project name/i), 'Secret Work');
    await user.selectOptions(screen.getByLabelText('Project visibility'), 'sensitive');
    expect(screen.getByText(/sensitive projects are membership-only/i)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /create project/i }));

    expect(await screen.findByText(/created sensitive project: secret work/i)).toBeInTheDocument();
    const postCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/projects') && options?.method === 'POST');
    expect(JSON.parse(postCall[1].body)).toEqual({ name: 'Secret Work', visibility: 'sensitive' });
  });

  it('restricts senior project creation to normal visibility without change controls', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ projectId: 'project-web-app', name: 'Web App', visibility: 'normal' }]
      })
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ projectId: 'project-api', name: 'API', visibility: 'normal' })
      });

    render(<ProjectManagementPage token="senior-token" userRole="senior" />);
    const user = userEvent.setup();

    expect(await screen.findByText(/only admins can change project visibility/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /save visibility/i })).not.toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Project'), '__add_project__');
    expect(screen.queryByLabelText(/project visibility/i)).not.toBeInTheDocument();
    expect(screen.getByText(/senior developers create normal projects/i)).toBeInTheDocument();
    await user.type(screen.getByLabelText(/new project name/i), 'API');
    await user.click(screen.getByRole('button', { name: /create project/i }));

    const postCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/projects') && options?.method === 'POST');
    expect(JSON.parse(postCall[1].body)).toEqual({ name: 'API', visibility: 'normal' });
  });
});
