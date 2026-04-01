CREATE TABLE IF NOT EXISTS lyra.external_connections (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,
    provider_name TEXT NOT NULL, -- 'enable_banking', etc.
    session_id TEXT,
    expires_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    balance_type VARCHAR(10) NULL,
    
    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES lyra.users(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS lyra.external_connection_account (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    connection_id UUID NOT NULL,
    account_id UUID NOT NULL UNIQUE, -- One Lyra account can only have one sync source
    external_account_id TEXT NOT NULL, -- The ID provided by external data provider
    
    CONSTRAINT fk_connection FOREIGN KEY(connection_id) REFERENCES lyra.external_connections(id) ON DELETE CASCADE,
    CONSTRAINT fk_account FOREIGN KEY(account_id) REFERENCES lyra.accounts(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS lyra.sync_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    connection_id UUID NOT NULL,
    sync_start TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    sync_end TIMESTAMP WITH TIME ZONE,
    status TEXT NOT NULL, -- 'success', 'error', 'partial'
    message TEXT,
    
    CONSTRAINT fk_connection_log FOREIGN KEY(connection_id) REFERENCES lyra.external_connections(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS lyra.enable_banking_accounts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    external_connection_id UUID NOT NULL REFERENCES external_connections(id) ON DELETE CASCADE,
    
    identification_hash TEXT NOT NULL,
    
    -- Account Details
    name TEXT,
    details TEXT,
    iban TEXT,
    currency VARCHAR(3) NOT NULL,
    account_type TEXT,
    product_name TEXT,

    -- Unique Constraint for Upsert
    CONSTRAINT uq_account_hash_per_connection UNIQUE (identification_hash, external_connection_id)
);

CREATE INDEX IF NOT EXISTS idx_eba_connection_id ON enable_banking_accounts(external_connection_id);
CREATE INDEX IF NOT EXISTS idx_eba_identification_hash ON enable_banking_accounts(identification_hash);

ALTER TABLE lyra.accounts
    ADD COLUMN IF NOT EXISTS current_balance NUMERIC(18, 2) NULL,
    ADD COLUMN IF NOT EXISTS current_balance_at TIMESTAMP WITH TIME ZONE NULL;

ALTER TABLE external_connections
    ADD COLUMN IF NOT EXISTS connection_name TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS provider_data JSONB;