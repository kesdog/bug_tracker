CREATE TABLE credential_recovery_requests (
    recovery_id TEXT PRIMARY KEY,
    request_type TEXT NOT NULL CHECK (request_type IN ('human', 'ai_agent')),
    email TEXT NOT NULL,
    user_id TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'issued', 'used', 'superseded')),
    token_hash TEXT,
    token_expires_at TEXT,
    issued_by_user_id TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (user_id) REFERENCES users(user_id),
    FOREIGN KEY (issued_by_user_id) REFERENCES users(user_id)
);

CREATE UNIQUE INDEX idx_credential_recovery_requests_active
    ON credential_recovery_requests (user_id, request_type)
    WHERE status IN ('pending', 'issued');

CREATE INDEX idx_credential_recovery_requests_created
    ON credential_recovery_requests (request_type, created_at DESC);

CREATE INDEX idx_credential_recovery_requests_token
    ON credential_recovery_requests (token_hash);
