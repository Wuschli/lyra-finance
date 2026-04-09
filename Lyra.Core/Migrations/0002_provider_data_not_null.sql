-- Remove legacy connections that have no provider_data and therefore cannot be re-authorized.
-- These originate from a time before provider_data was persisted on connection creation.
DELETE FROM lyra.external_connections
WHERE provider_data IS NULL;

-- Enforce provider_data as NOT NULL going forward.
ALTER TABLE lyra.external_connections
    ALTER COLUMN provider_data SET NOT NULL;