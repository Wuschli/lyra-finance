ALTER TABLE external_connection_account
    ADD CONSTRAINT uq_connection_external_account UNIQUE (connection_id, external_account_id);

ALTER TABLE external_connections
    ADD COLUMN IF NOT EXISTS available_balance_types jsonb NULL;