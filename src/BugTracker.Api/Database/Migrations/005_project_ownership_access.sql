ALTER TABLE projects ADD COLUMN owner_user_id TEXT REFERENCES users(user_id);

UPDATE projects
SET owner_user_id = COALESCE(
    (SELECT user_id FROM users
     WHERE is_active = 1 AND user_type = 'human' AND role = 'admin'
     ORDER BY created_at ASC, user_id ASC LIMIT 1),
    (SELECT user_id FROM users
     WHERE is_active = 1 AND user_type = 'human' AND role = 'senior'
     ORDER BY created_at ASC, user_id ASC LIMIT 1)
);

INSERT OR IGNORE INTO project_allocations (project_id, user_id, created_at)
SELECT project_id, owner_user_id, datetime('now')
FROM projects
WHERE owner_user_id IS NOT NULL;

ALTER TABLE ticket_activity ADD COLUMN subject_user_id TEXT REFERENCES users(user_id);

CREATE TABLE project_access_requests (
    request_id TEXT PRIMARY KEY,
    project_id TEXT NOT NULL,
    requester_user_id TEXT NOT NULL,
    source_ticket_id TEXT,
    reason TEXT,
    status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'approved', 'denied')),
    reviewed_by_user_id TEXT,
    review_note TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    reviewed_at TEXT,
    FOREIGN KEY (project_id) REFERENCES projects(project_id),
    FOREIGN KEY (requester_user_id) REFERENCES users(user_id),
    FOREIGN KEY (source_ticket_id) REFERENCES bug_tickets(id),
    FOREIGN KEY (reviewed_by_user_id) REFERENCES users(user_id)
);

CREATE UNIQUE INDEX ux_project_access_requests_pending
    ON project_access_requests (project_id, requester_user_id)
    WHERE status = 'pending';
CREATE INDEX idx_project_access_requests_review
    ON project_access_requests (status, project_id, created_at, request_id);
CREATE INDEX idx_bug_tickets_status_created_id
    ON bug_tickets (status, created_at DESC, id DESC);
CREATE INDEX idx_ticket_activity_deterministic
    ON ticket_activity (ticket_id, created_at DESC, activity_id DESC);
CREATE INDEX idx_ticket_activity_subject
    ON ticket_activity (subject_user_id, created_at DESC);
