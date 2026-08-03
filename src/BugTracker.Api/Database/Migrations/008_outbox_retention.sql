CREATE INDEX idx_outbox_processed_retention
ON outbox_messages (processed_at)
WHERE processed_at IS NOT NULL;
