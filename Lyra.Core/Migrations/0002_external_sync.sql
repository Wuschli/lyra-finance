-- 1. The main connection (one per bank access / consent)
CREATE TABLE IF NOT EXISTS lyra.external_connections (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,
    provider_name TEXT NOT NULL, -- 'enable_banking', etc.
    external_session_id TEXT NOT NULL,
    access_token TEXT,
    refresh_token TEXT,
    expires_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    last_sync_at TIMESTAMP WITH TIME ZONE,
    
    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES lyra.users(id) ON DELETE CASCADE
);

-- 2. Junction table: Maps one external account to one Lyra account
CREATE TABLE IF NOT EXISTS lyra.external_connection_account (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    connection_id UUID NOT NULL,
    account_id UUID NOT NULL UNIQUE, -- One Lyra account can only have one sync source
    external_account_id TEXT NOT NULL, -- The ID provided by external data provider
    
    CONSTRAINT fk_connection FOREIGN KEY(connection_id) REFERENCES lyra.external_connections(id) ON DELETE CASCADE,
    CONSTRAINT fk_account FOREIGN KEY(account_id) REFERENCES lyra.accounts(id) ON DELETE CASCADE
);

-- 3. Sync logs tied to the connection
CREATE TABLE IF NOT EXISTS lyra.sync_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    connection_id UUID NOT NULL,
    sync_start TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    sync_end TIMESTAMP WITH TIME ZONE,
    status TEXT NOT NULL, -- 'success', 'error', 'partial'
    message TEXT,
    
    CONSTRAINT fk_connection_log FOREIGN KEY(connection_id) REFERENCES lyra.external_connections(id) ON DELETE CASCADE
);