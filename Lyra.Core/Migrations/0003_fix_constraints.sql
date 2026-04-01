ALTER TABLE lyra.external_connection_account
    ADD CONSTRAINT uq_connection_external_account UNIQUE (connection_id, external_account_id);