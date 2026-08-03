ALTER TABLE audit_logs RENAME TO audit_logs_legacy;

CREATE TABLE audit_logs (
    audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
    ticket_id TEXT,
    actor_user_id TEXT NOT NULL,
    actor_type TEXT NOT NULL DEFAULT 'human' CHECK (actor_type IN ('human', 'agent', 'system')),
    action TEXT NOT NULL,
    message TEXT NOT NULL DEFAULT '',
    before_json TEXT,
    after_json TEXT,
    metadata_json TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    event_id TEXT,
    ticket_version INTEGER,
    FOREIGN KEY (ticket_id) REFERENCES bug_tickets(id),
    FOREIGN KEY (actor_user_id) REFERENCES users(user_id)
);

INSERT INTO audit_logs (
    audit_id, ticket_id, actor_user_id, actor_type, action, message, before_json, after_json,
    metadata_json, created_at, event_id, ticket_version
)
SELECT
    audit_id, ticket_id, actor_user_id, actor_type, action, message, before_json, after_json,
    metadata_json, created_at, event_id, ticket_version
FROM audit_logs_legacy;

DROP TABLE audit_logs_legacy;

CREATE INDEX idx_audit_logs_ticket_created ON audit_logs (ticket_id, created_at DESC);
CREATE INDEX idx_audit_logs_actor_type_created ON audit_logs (actor_type, created_at DESC);
CREATE INDEX idx_audit_logs_action_created ON audit_logs (action, created_at DESC);
CREATE INDEX idx_audit_logs_event ON audit_logs (event_id);
