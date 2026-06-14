CREATE TABLE IF NOT EXISTS api_keys (
    key_hash  TEXT        NOT NULL,
    label     TEXT        NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT api_keys_key_hash_unique UNIQUE (key_hash)
);

CREATE TABLE IF NOT EXISTS links (
    id            BIGSERIAL   PRIMARY KEY,
    code          TEXT        NOT NULL,
    target_url    TEXT        NOT NULL,
    owner_key_hash TEXT       NOT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT links_code_unique UNIQUE (code)
);

CREATE INDEX IF NOT EXISTS links_owner_key_hash_idx ON links (owner_key_hash);

CREATE TABLE IF NOT EXISTS clicks (
    id         BIGSERIAL   PRIMARY KEY,
    link_id    BIGINT      NOT NULL REFERENCES links (id) ON DELETE CASCADE,
    clicked_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    referer    TEXT,
    user_agent TEXT
);

CREATE INDEX IF NOT EXISTS clicks_link_id_idx ON clicks (link_id);
