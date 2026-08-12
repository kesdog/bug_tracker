CREATE TABLE login_security_state (
    account_fingerprint TEXT NOT NULL,
    ip_fingerprint TEXT NOT NULL,
    flow TEXT NOT NULL,
    failed_attempts INTEGER NOT NULL DEFAULT 0,
    first_failed_at TEXT NOT NULL,
    last_failed_at TEXT NOT NULL,
    locked_until TEXT,
    PRIMARY KEY (account_fingerprint, ip_fingerprint, flow)
);

CREATE INDEX idx_login_security_state_retention
ON login_security_state (last_failed_at);
