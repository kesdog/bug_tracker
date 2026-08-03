const HEADER_BY_PAGE = {
  dashboard: ['Dashboard', 'See your project-scoped active tickets and quick workload snapshot.'],
  tickets: ['View Tickets', 'Browse active tickets and inspect complete bug reports.'],
  allocated: ['Allocated Bugs', 'Work through tickets assigned to you and update their reports.'],
  submitted: ['Submitted Reports', 'Track the bug reports you submitted and edit open reports.'],
  archived: ['Archived Tickets', 'Review resolved tickets and their final report history.'],
  'add-bug': ['Add Bug', 'Create a new bug report with project, severity, and evidence.'],
  'project-management': ['Project Management', 'Manage project records and allocate users to projects.'],
  'user-management': ['Users', 'Manage users, access requests, and user-scoped activity.'],
  'audit-logs': ['Audit Logs', 'Search human and AI-agent activity across the workspace.']
};

export function getAppHeaderMeta({ session, isSetupRoute, loginView, currentPage }) {
  if (!session) {
    if (isSetupRoute) return { title: 'Set Or Reset Password', description: 'Use your one-time link to securely set a new password.' };
    if (loginView === 'request') return { title: 'Request Access', description: 'Submit your email to request a human or AI agent account.' };
    if (loginView === 'recovery') return { title: 'Recover Credentials', description: 'Request a password reset or AI agent oath-token reissue.' };
    return { title: 'Sign In', description: 'Access your bug tracker workspace and current project tickets.' };
  }
  const [title, description] = HEADER_BY_PAGE[currentPage] || HEADER_BY_PAGE.dashboard;
  return { title, description };
}
