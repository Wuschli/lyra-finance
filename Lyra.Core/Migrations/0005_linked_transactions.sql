ALTER TABLE lyra.transactions
    ADD COLUMN IF NOT EXISTS linked_transaction_id uuid NULL
        REFERENCES lyra.transactions (id) ON DELETE SET NULL;