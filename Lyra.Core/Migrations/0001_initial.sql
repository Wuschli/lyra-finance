-- Ensure the schema exists
CREATE SCHEMA IF NOT EXISTS lyra;

-- Create the users table
CREATE TABLE IF NOT EXISTS lyra.users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    zitadel_id TEXT NOT NULL,
    last_login TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    -- Ensure we don't have duplicate Zitadel users
    CONSTRAINT uq_zitadel_id UNIQUE (zitadel_id)
);

-- Index for faster lookups during login/auth
CREATE INDEX IF NOT EXISTS idx_users_zitadel_id ON lyra.users(zitadel_id);

-- Create the accounts table in the lyra schema
CREATE TABLE IF NOT EXISTS lyra.accounts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,
    name TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    -- Constraint: Each account must belong to an existing user
    CONSTRAINT fk_user 
        FOREIGN KEY(user_id) 
        REFERENCES lyra.users(id) 
        ON DELETE CASCADE
);

-- Index for performance: we will constantly filter accounts by user_id
CREATE INDEX IF NOT EXISTS idx_accounts_user_id ON lyra.accounts(user_id);

-- Create the transactions table in the lyra schema
CREATE TABLE IF NOT EXISTS lyra.transactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    account_id UUID NOT NULL,
    counterparty_name TEXT NOT NULL,
    counterparty_iban TEXT NOT NULL DEFAULT '',
    description TEXT,
    amount DECIMAL(15, 2) NOT NULL,
    transaction_date TIMESTAMP WITH TIME ZONE NOT NULL,
    booking_date TIMESTAMP WITH TIME ZONE,
    value_date TIMESTAMP WITH TIME ZONE,
    category TEXT,
    
    -- Constraint: Transactions must belong to an account
    CONSTRAINT fk_account 
        FOREIGN KEY(account_id) 
        REFERENCES lyra.accounts(id) 
        ON DELETE CASCADE
);

-- Performance: Most queries will filter by account_id and sort by date
CREATE INDEX IF NOT EXISTS idx_transactions_account_id ON lyra.transactions(account_id);
CREATE INDEX IF NOT EXISTS idx_transactions_date ON lyra.transactions(transaction_date DESC);