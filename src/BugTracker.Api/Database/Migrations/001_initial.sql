CREATE TABLE users (
    user_id TEXT PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    role TEXT NOT NULL CHECK (role IN ('dev', 'senior', 'admin')),
    user_type TEXT NOT NULL DEFAULT 'human' CHECK (user_type IN ('human', 'agent')),
    projects_json TEXT NOT NULL DEFAULT '[]',
    is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    last_seen_at TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE projects (
    project_id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    visibility TEXT NOT NULL DEFAULT 'normal' CHECK (visibility IN ('normal', 'sensitive')),
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE user_requests (
    request_id TEXT PRIMARY KEY,
    request_type TEXT NOT NULL CHECK (request_type IN ('human', 'ai_agent')),
    email TEXT NOT NULL UNIQUE,
    username TEXT NOT NULL UNIQUE,
    status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'approved', 'removed')),
    user_id TEXT,
    setup_token_hash TEXT,
    setup_token_expires_at TEXT,
    api_key_hash TEXT,
    api_key_prefix TEXT,
    api_key_expires_at TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (user_id) REFERENCES users(user_id)
);

CREATE TABLE bug_tickets (
    id TEXT PRIMARY KEY,
    issue_title TEXT NOT NULL,
    description TEXT NOT NULL,
    bug_type TEXT NOT NULL CHECK (bug_type IN ('page_not_loading', 'form_submission', 'crash', 'api', 'database')),
    reporter_user_id TEXT NOT NULL,
    project_id TEXT NOT NULL,
    assignee_user_id TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    status TEXT NOT NULL CHECK (status IN ('todo', 'open', 'closed', 'reopened')),
    severity TEXT NOT NULL CHECK (severity IN ('low', 'mid', 'high', 'urgent')),
    priority TEXT NOT NULL DEFAULT 'p2' CHECK (priority IN ('p0', 'p1', 'p2', 'p3')),
    tags_json TEXT NOT NULL DEFAULT '[]',
    environment TEXT,
    expected_behavior TEXT,
    actual_behavior TEXT,
    steps_to_reproduce TEXT,
    frequency TEXT CHECK (frequency IS NULL OR frequency IN ('unknown', 'once', 'intermittent', 'frequent', 'always')),
    close_date TEXT,
    resolved_by_user_id TEXT,
    assigned_at TEXT,
    resolution_notes TEXT,
    post_resolution_report TEXT,
    report_images_json TEXT,
    resolution_report_images_json TEXT,
    text_evidence_json TEXT,
    CHECK (severity <> 'urgent' OR priority IN ('p0', 'p1')),
    CHECK (NOT (instr(lower(COALESCE(tags_json, '')), '"front-end"') > 0 AND instr(lower(COALESCE(tags_json, '')), '"back-end"') > 0)),
    FOREIGN KEY (reporter_user_id) REFERENCES users(user_id),
    FOREIGN KEY (project_id) REFERENCES projects(project_id),
    FOREIGN KEY (assignee_user_id) REFERENCES users(user_id),
    FOREIGN KEY (resolved_by_user_id) REFERENCES users(user_id)
);

CREATE TABLE project_allocations (
    allocation_id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE (project_id, user_id),
    FOREIGN KEY (project_id) REFERENCES projects(project_id),
    FOREIGN KEY (user_id) REFERENCES users(user_id)
);

CREATE TABLE ticket_activity (
    activity_id TEXT PRIMARY KEY,
    ticket_id TEXT NOT NULL,
    actor_user_id TEXT NOT NULL,
    actor_type TEXT NOT NULL CHECK (actor_type IN ('human', 'agent', 'system')),
    kind TEXT NOT NULL CHECK (kind IN ('comment', 'created', 'edited', 'assigned', 'closed', 'reopened', 'priority_changed', 'tags_changed', 'attachment_added')),
    body TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (ticket_id) REFERENCES bug_tickets(id),
    FOREIGN KEY (actor_user_id) REFERENCES users(user_id)
);

CREATE TABLE ticket_attachments (
    attachment_id TEXT PRIMARY KEY,
    ticket_id TEXT NOT NULL,
    uploaded_by_user_id TEXT NOT NULL,
    purpose TEXT NOT NULL CHECK (purpose IN ('initial-report', 'solution-report', 'close-report')),
    file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    kind TEXT NOT NULL CHECK (kind IN ('image')),
    size_bytes INTEGER NOT NULL,
    width INTEGER,
    height INTEGER,
    sha256 TEXT NOT NULL,
    content_blob BLOB NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (ticket_id) REFERENCES bug_tickets(id),
    FOREIGN KEY (uploaded_by_user_id) REFERENCES users(user_id)
);

CREATE TABLE audit_logs (
    audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
    ticket_id TEXT,
    actor_user_id TEXT NOT NULL,
    actor_type TEXT NOT NULL DEFAULT 'human' CHECK (actor_type IN ('human', 'agent')),
    action TEXT NOT NULL,
    message TEXT NOT NULL DEFAULT '',
    before_json TEXT,
    after_json TEXT,
    metadata_json TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (ticket_id) REFERENCES bug_tickets(id),
    FOREIGN KEY (actor_user_id) REFERENCES users(user_id)
);

CREATE TABLE auth_tokens (
    token_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    token_hash TEXT NOT NULL UNIQUE,
    issued_at TEXT NOT NULL DEFAULT (datetime('now')),
    expires_at TEXT NOT NULL,
    revoked_at TEXT,
    FOREIGN KEY (user_id) REFERENCES users(user_id)
);

CREATE TABLE notifications (
    notification_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    ticket_id TEXT,
    kind TEXT NOT NULL,
    message TEXT NOT NULL,
    is_read INTEGER NOT NULL DEFAULT 0 CHECK (is_read IN (0, 1)),
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (user_id) REFERENCES users(user_id),
    FOREIGN KEY (ticket_id) REFERENCES bug_tickets(id)
);

CREATE INDEX idx_bug_tickets_status_created ON bug_tickets (status, created_at DESC);
CREATE INDEX idx_bug_tickets_assignee ON bug_tickets (assignee_user_id);
CREATE INDEX idx_bug_tickets_project ON bug_tickets (project_id);
CREATE INDEX idx_ticket_activity_ticket_created ON ticket_activity (ticket_id, created_at DESC);
CREATE INDEX idx_ticket_attachments_ticket_created ON ticket_attachments (ticket_id, created_at ASC);
CREATE INDEX idx_project_allocations_user ON project_allocations (user_id, project_id);
CREATE INDEX idx_audit_logs_ticket_created ON audit_logs (ticket_id, created_at DESC);
CREATE INDEX idx_audit_logs_actor_type_created ON audit_logs (actor_type, created_at DESC);
CREATE INDEX idx_audit_logs_action_created ON audit_logs (action, created_at DESC);
CREATE INDEX idx_auth_tokens_user_expires ON auth_tokens (user_id, expires_at);
CREATE INDEX idx_notifications_user_read_created ON notifications (user_id, is_read, created_at DESC);
CREATE INDEX idx_user_requests_type_status ON user_requests (request_type, status, created_at DESC);

CREATE TRIGGER trg_users_updated_at AFTER UPDATE ON users FOR EACH ROW
BEGIN
    UPDATE users SET updated_at = datetime('now') WHERE user_id = OLD.user_id;
END;

CREATE TRIGGER trg_bug_tickets_updated_at AFTER UPDATE ON bug_tickets FOR EACH ROW
BEGIN
    UPDATE bug_tickets SET updated_at = datetime('now') WHERE id = OLD.id;
END;

CREATE TRIGGER trg_projects_updated_at AFTER UPDATE ON projects FOR EACH ROW
BEGIN
    UPDATE projects SET updated_at = datetime('now') WHERE project_id = OLD.project_id;
END;
