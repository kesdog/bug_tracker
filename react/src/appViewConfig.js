const HEADER_BY_PAGE = {
  dashboard: ['headers.dashboard', 'Dashboard', 'See your project-scoped active tickets and quick workload snapshot.'],
  tickets: ['headers.tickets', 'View Tickets', 'Browse active tickets and inspect complete bug reports.'],
  allocated: ['headers.allocated', 'Allocated Bugs', 'Work through tickets assigned to you and update their reports.'],
  submitted: ['headers.submitted', 'Submitted Reports', 'Track the bug reports you submitted and edit open reports.'],
  archived: ['headers.archived', 'Archived Tickets', 'Review resolved tickets and their final report history.'],
  'add-bug': ['headers.addBug', 'Add Bug', 'Create a new bug report with project, severity, and evidence.'],
  'project-management': ['headers.projects', 'Project Management', 'Manage project records and allocate users to projects.'],
  'user-management': ['headers.users', 'Users', 'Manage users, access requests, and user-scoped activity.'],
  'audit-logs': ['headers.auditLogs', 'Audit Logs', 'Search human and AI-agent activity across the workspace.']
};

export function getAppHeaderMeta({ session, isSetupRoute, loginView, currentPage, t = (_key, fallback) => fallback }) {
  if (!session) {
    if (isSetupRoute) return { title: t('headers.setupPassword', 'Set Or Reset Password'), description: t('headers.setupPasswordDescription', 'Use your one-time link to securely set a new password.') };
    if (loginView === 'request') return { title: t('headers.requestAccess', 'Request Access'), description: t('headers.requestAccessDescription', 'Submit your email to request a human or AI agent account.') };
    if (loginView === 'recovery') return { title: t('headers.recoverCredentials', 'Recover Credentials'), description: t('headers.recoverCredentialsDescription', 'Request a password reset or AI agent oath-token reissue.') };
    return { title: t('headers.signIn', 'Sign In'), description: t('headers.signInDescription', 'Access your bug tracker workspace and current project tickets.') };
  }
  const [key, title, description] = HEADER_BY_PAGE[currentPage] || HEADER_BY_PAGE.dashboard;
  return { title: t(`${key}.title`, title), description: t(`${key}.description`, description) };
}
