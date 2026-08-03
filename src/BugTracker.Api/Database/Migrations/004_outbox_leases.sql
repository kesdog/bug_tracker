ALTER TABLE outbox_messages ADD COLUMN claim_owner TEXT;
ALTER TABLE outbox_messages ADD COLUMN claimed_until TEXT;

CREATE INDEX idx_outbox_claimable
    ON outbox_messages (processed_at, available_at, claimed_until, created_at);
