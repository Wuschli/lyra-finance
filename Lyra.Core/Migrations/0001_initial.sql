-- ============================================================
-- Initial migration (consolidated from 0001–0005)
-- ============================================================

-- Ensure the schema exists
CREATE SCHEMA IF NOT EXISTS lyra;

-- ============================================================
-- Users
-- ============================================================
CREATE TABLE IF NOT EXISTS lyra.users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    zitadel_id TEXT NOT NULL,
    last_login TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT uq_zitadel_id UNIQUE (zitadel_id)
);

CREATE INDEX IF NOT EXISTS idx_users_zitadel_id ON lyra.users(zitadel_id);

-- ============================================================
-- Accounts
-- ============================================================
CREATE TABLE IF NOT EXISTS lyra.accounts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,
    name TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    current_balance NUMERIC(18, 2) NULL,
    current_balance_at TIMESTAMP WITH TIME ZONE NULL,

    CONSTRAINT fk_user
        FOREIGN KEY(user_id)
        REFERENCES lyra.users(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_accounts_user_id ON lyra.accounts(user_id);

-- ============================================================
-- External Connections
-- ============================================================
CREATE TABLE IF NOT EXISTS lyra.external_connections (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,
    provider_name TEXT NOT NULL,
    session_id TEXT,
    expires_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    balance_type VARCHAR(10) NULL,
    connection_name TEXT NOT NULL DEFAULT '',
    provider_data JSONB,
    available_balance_types JSONB NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES lyra.users(id) ON DELETE CASCADE
);

-- ============================================================
-- External Connection Accounts
-- ============================================================
CREATE TABLE IF NOT EXISTS lyra.external_connection_account (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    connection_id UUID NOT NULL,
    account_id UUID NOT NULL UNIQUE,
    external_account_id TEXT NOT NULL,

    CONSTRAINT fk_connection FOREIGN KEY(connection_id) REFERENCES lyra.external_connections(id) ON DELETE CASCADE,
    CONSTRAINT fk_account FOREIGN KEY(account_id) REFERENCES lyra.accounts(id) ON DELETE CASCADE,
    CONSTRAINT uq_connection_external_account UNIQUE (connection_id, external_account_id)
);

-- ============================================================
-- Enable Banking Accounts
-- ============================================================
CREATE TABLE IF NOT EXISTS lyra.enable_banking_accounts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    external_connection_id UUID NOT NULL REFERENCES lyra.external_connections(id) ON DELETE CASCADE,

    identification_hash TEXT NOT NULL,

    name TEXT,
    details TEXT,
    iban TEXT,
    currency VARCHAR(3) NOT NULL,
    account_type TEXT,
    product_name TEXT,

    CONSTRAINT uq_account_hash_per_connection UNIQUE (identification_hash, external_connection_id)
);

CREATE INDEX IF NOT EXISTS idx_eba_connection_id ON lyra.enable_banking_accounts(external_connection_id);
CREATE INDEX IF NOT EXISTS idx_eba_identification_hash ON lyra.enable_banking_accounts(identification_hash);

-- ============================================================
-- Sync Logs
-- ============================================================
CREATE TABLE IF NOT EXISTS lyra.sync_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    connection_id UUID NOT NULL,
    sync_start TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    sync_end TIMESTAMP WITH TIME ZONE,
    status TEXT NOT NULL,
    message TEXT,

    CONSTRAINT fk_connection_log FOREIGN KEY(connection_id) REFERENCES lyra.external_connections(id) ON DELETE CASCADE
);

-- ============================================================
-- Transactions
-- ============================================================
CREATE TABLE IF NOT EXISTS lyra.transactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    account_id UUID NOT NULL,
    counterparty_name TEXT NOT NULL,
    counterparty_iban TEXT NOT NULL DEFAULT '',
    description TEXT,
    amount DECIMAL(15, 2) NOT NULL,
    currency VARCHAR(10) NOT NULL DEFAULT '',
    transaction_date TIMESTAMP WITH TIME ZONE NOT NULL,
    booking_date TIMESTAMP WITH TIME ZONE,
    value_date TIMESTAMP WITH TIME ZONE,
    category TEXT,
    external_identifier VARCHAR(128),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    is_pending BOOLEAN NOT NULL DEFAULT false,
    linked_transaction_id UUID NULL REFERENCES lyra.transactions(id) ON DELETE SET NULL,

    CONSTRAINT fk_account
        FOREIGN KEY(account_id)
        REFERENCES lyra.accounts(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_transactions_account_id ON lyra.transactions(account_id);
CREATE INDEX IF NOT EXISTS idx_transactions_date ON lyra.transactions(transaction_date DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_transactions_account_external_identifier
    ON lyra.transactions(account_id, external_identifier);

-- ============================================================
-- Trigger: keep updated_at current on transactions
-- ============================================================
CREATE OR REPLACE FUNCTION lyra.set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE TRIGGER trg_transactions_updated_at
    BEFORE UPDATE ON lyra.transactions
    FOR EACH ROW
    EXECUTE FUNCTION lyra.set_updated_at();