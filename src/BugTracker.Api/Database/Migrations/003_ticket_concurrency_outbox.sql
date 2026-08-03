ALTER TABLE bug_tickets ADD COLUMN version INTEGER NOT NULL DEFAULT 1 CHECK (version >= 1);

ALTER TABLE ticket_activity ADD COLUMN event_id TEXT;
ALTER TABLE ticket_activity ADD COLUMN ticket_version INTEGER;
ALTER TABLE ticket_activity ADD COLUMN changed_fields_json TEXT;

ALTER TABLE audit_logs ADD COLUMN event_id TEXT;
ALTER TABLE audit_logs ADD COLUMN ticket_version INTEGER;

ALTER TABLE notifications ADD COLUMN event_id TEXT;
ALTER TABLE notifications ADD COLUMN ticket_version INTEGER;

CREATE TABLE outbox_messages (
    outbox_id TEXT PRIMARY KEY,
    event_id TEXT NOT NULL,
    event_type TEXT NOT NULL CHECK (event_type IN ('notification.websocket', 'audit.jsonl')),
    aggregate_id TEXT,
    ticket_version INTEGER,
    payload_json TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    available_at TEXT NOT NULL DEFAULT (datetime('now')),
    attempts INTEGER NOT NULL DEFAULT 0,
    processed_at TEXT,
    last_error TEXT
);

CREATE INDEX idx_outbox_pending ON outbox_messages (processed_at, available_at, created_at);
CREATE INDEX idx_outbox_event ON outbox_messages (event_id);
CREATE INDEX idx_ticket_activity_event ON ticket_activity (event_id);
CREATE INDEX idx_notifications_event ON notifications (event_id);
CREATE INDEX idx_audit_logs_event ON audit_logs (event_id);
