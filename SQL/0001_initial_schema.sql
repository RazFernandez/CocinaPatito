-- =============================================================================
-- SplitBill — initial schema
-- Target: PostgreSQL 18.x (uses native uuidv7()).
-- Conventions: snake_case, singular table names, FK columns "<entity>_id",
--              timestamps "*_at" in timestamptz (UTC), booleans "is_*".
-- Money: numeric(14,2) + currency_code (ISO 4217). Never float/double.
-- Security: no dynamic SQL anywhere; application uses static parameterised
--           statements. Sensitive columns are stored as AES-256-GCM ciphertext
--           (bytea) produced by the application; the database never sees keys.
-- Design reference: docs/01-data-model.md, docs/04-database-uml.md
-- =============================================================================

BEGIN;

CREATE SCHEMA IF NOT EXISTS splitbill;
SET LOCAL search_path TO splitbill, public;

-- -----------------------------------------------------------------------------
-- Shared trigger: keep updated_at current on every UPDATE.
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION splitbill.set_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$;

-- -----------------------------------------------------------------------------
-- currency — reference catalogue (only MXN active today)
-- -----------------------------------------------------------------------------
CREATE TABLE currency (
    code        char(3)     PRIMARY KEY,
    name        text        NOT NULL,
    minor_unit  smallint    NOT NULL CHECK (minor_unit BETWEEN 0 AND 4),
    is_active   boolean     NOT NULL DEFAULT true,
    CONSTRAINT currency_code_format CHECK (code ~ '^[A-Z]{3}$')
);
COMMENT ON TABLE  currency            IS 'ISO 4217 currency catalogue. Extension point for multi-currency.';
COMMENT ON COLUMN currency.minor_unit IS 'Number of decimal places for the currency (2 for MXN).';

INSERT INTO currency (code, name, minor_unit, is_active)
VALUES ('MXN', 'Mexican peso', 2, true);

-- -----------------------------------------------------------------------------
-- app_user — platform user (bill owner or registered participant)
-- -----------------------------------------------------------------------------
CREATE TABLE app_user (
    id                      uuid        PRIMARY KEY DEFAULT uuidv7(),
    email_encrypted         bytea       NOT NULL,
    email_hash              bytea       NOT NULL,
    phone_encrypted         bytea       NULL,
    display_name            text        NOT NULL CHECK (char_length(display_name) BETWEEN 1 AND 80),
    password_hash           text        NOT NULL,
    encryption_key_version  smallint    NOT NULL CHECK (encryption_key_version > 0),
    status                  text        NOT NULL DEFAULT 'ACTIVE'
                                        CHECK (status IN ('ACTIVE', 'BLOCKED', 'DELETED')),
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now(),
    deleted_at              timestamptz NULL,
    CONSTRAINT app_user_email_hash_unique UNIQUE (email_hash),
    CONSTRAINT app_user_deleted_consistency CHECK ((status = 'DELETED') = (deleted_at IS NOT NULL))
);
COMMENT ON TABLE  app_user                 IS 'Platform user. Personal data is encrypted at application level.';
COMMENT ON COLUMN app_user.email_encrypted IS 'AES-256-GCM ciphertext of the normalised e-mail.';
COMMENT ON COLUMN app_user.email_hash      IS 'HMAC-SHA256 of the normalised e-mail; used for lookups and uniqueness.';
COMMENT ON COLUMN app_user.password_hash   IS 'Argon2id encoded hash.';
COMMENT ON COLUMN app_user.deleted_at      IS 'Set when the data subject exercises the ARCO cancellation right.';

CREATE INDEX app_user_status_active_idx ON app_user (status) WHERE deleted_at IS NULL;

CREATE TRIGGER app_user_set_updated_at
    BEFORE UPDATE ON app_user
    FOR EACH ROW EXECUTE FUNCTION splitbill.set_updated_at();

-- -----------------------------------------------------------------------------
-- privacy_consent — evidence of privacy-notice acceptance (LFPDPPP)
-- -----------------------------------------------------------------------------
CREATE TABLE privacy_consent (
    id              uuid        PRIMARY KEY DEFAULT uuidv7(),
    user_id         uuid        NOT NULL REFERENCES app_user (id),
    notice_version  text        NOT NULL,
    accepted_at     timestamptz NOT NULL DEFAULT now(),
    ip_hash         bytea       NULL,
    CONSTRAINT privacy_consent_once_per_version UNIQUE (user_id, notice_version)
);
COMMENT ON TABLE privacy_consent IS 'One row per privacy-notice version accepted by the user. Never deleted.';

-- -----------------------------------------------------------------------------
-- refresh_token — session state lives here, never in process memory
-- -----------------------------------------------------------------------------
CREATE TABLE refresh_token (
    id              uuid        PRIMARY KEY DEFAULT uuidv7(),
    user_id         uuid        NOT NULL REFERENCES app_user (id),
    token_hash      bytea       NOT NULL,
    issued_at       timestamptz NOT NULL DEFAULT now(),
    expires_at      timestamptz NOT NULL,
    revoked_at      timestamptz NULL,
    replaced_by_id  uuid        NULL REFERENCES refresh_token (id),
    CONSTRAINT refresh_token_hash_unique UNIQUE (token_hash),
    CONSTRAINT refresh_token_expiry_after_issue CHECK (expires_at > issued_at)
);
COMMENT ON TABLE  refresh_token                IS 'Opaque refresh tokens stored as SHA-256 hashes. Rotated on every use.';
COMMENT ON COLUMN refresh_token.replaced_by_id IS 'Rotation chain; reuse of a replaced token revokes the whole chain.';

CREATE INDEX refresh_token_user_live_idx ON refresh_token (user_id) WHERE revoked_at IS NULL;
CREATE INDEX refresh_token_expires_idx   ON refresh_token (expires_at);

-- -----------------------------------------------------------------------------
-- user_bank_account — receiving account registered at Pey (never the full CLABE)
-- -----------------------------------------------------------------------------
CREATE TABLE user_bank_account (
    id                   uuid        PRIMARY KEY DEFAULT uuidv7(),
    user_id              uuid        NOT NULL REFERENCES app_user (id),
    pey_bank_account_id  text        NOT NULL,
    alias                text        NOT NULL CHECK (char_length(alias) BETWEEN 1 AND 60),
    clabe_last4          char(4)     NOT NULL CHECK (clabe_last4 ~ '^[0-9]{4}$'),
    codi_status          text        NOT NULL DEFAULT 'PENDING'
                                     CHECK (codi_status IN ('PENDING', 'ACTIVE', 'REJECTED')),
    created_at           timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT user_bank_account_pey_id_unique UNIQUE (pey_bank_account_id)
);
COMMENT ON TABLE  user_bank_account                     IS 'Owner bank account as registered at Pey. Pey custodies the full CLABE.';
COMMENT ON COLUMN user_bank_account.pey_bank_account_id IS 'Pey ULID; sent as bankAccountId when creating CoDi QRs.';
COMMENT ON COLUMN user_bank_account.codi_status         IS 'Mirror of Pey codiStatus, updated by the bank-account.updated webhook.';

CREATE INDEX user_bank_account_user_idx ON user_bank_account (user_id);

CREATE TRIGGER user_bank_account_set_updated_at
    BEFORE UPDATE ON user_bank_account
    FOR EACH ROW EXECUTE FUNCTION splitbill.set_updated_at();

-- -----------------------------------------------------------------------------
-- bill — the shared account to split
-- -----------------------------------------------------------------------------
CREATE TABLE bill (
    id                         uuid           PRIMARY KEY DEFAULT uuidv7(),
    owner_user_id              uuid           NOT NULL REFERENCES app_user (id),
    receiving_bank_account_id  uuid           NOT NULL REFERENCES user_bank_account (id),
    currency_code              char(3)        NOT NULL REFERENCES currency (code),
    title                      text           NOT NULL CHECK (char_length(title) BETWEEN 1 AND 120),
    total_amount               numeric(14,2)  NOT NULL CHECK (total_amount > 0),
    split_method               text           NOT NULL CHECK (split_method IN ('EQUAL', 'CUSTOM')),
    status                     text           NOT NULL DEFAULT 'OPEN'
                                              CHECK (status IN ('OPEN', 'SETTLED', 'CANCELLED')),
    created_at                 timestamptz    NOT NULL DEFAULT now(),
    updated_at                 timestamptz    NOT NULL DEFAULT now(),
    settled_at                 timestamptz    NULL,
    cancelled_at               timestamptz    NULL,
    CONSTRAINT bill_settled_consistency   CHECK ((status = 'SETTLED')   = (settled_at   IS NOT NULL)),
    CONSTRAINT bill_cancelled_consistency CHECK ((status = 'CANCELLED') = (cancelled_at IS NOT NULL))
);
COMMENT ON TABLE  bill              IS 'A bill split among participants. Progress (paid/remaining) is derived, never stored.';
COMMENT ON COLUMN bill.split_method IS 'EQUAL: total / n with cent adjustment. CUSTOM: client-provided shares.';

CREATE INDEX bill_owner_status_idx ON bill (owner_user_id, status);
CREATE INDEX bill_created_idx      ON bill (created_at DESC);

CREATE TRIGGER bill_set_updated_at
    BEFORE UPDATE ON bill
    FOR EACH ROW EXECUTE FUNCTION splitbill.set_updated_at();

-- -----------------------------------------------------------------------------
-- bill_participant — who takes part and their charge (share)
-- -----------------------------------------------------------------------------
CREATE TABLE bill_participant (
    id            uuid           PRIMARY KEY DEFAULT uuidv7(),
    bill_id       uuid           NOT NULL REFERENCES bill (id) ON DELETE CASCADE,
    user_id       uuid           NULL REFERENCES app_user (id),
    display_name  text           NOT NULL CHECK (char_length(display_name) BETWEEN 1 AND 80),
    position      smallint       NOT NULL CHECK (position >= 1),
    share_amount  numeric(14,2)  NOT NULL CHECK (share_amount >= 0),
    is_owner      boolean        NOT NULL DEFAULT false,
    status        text           NOT NULL DEFAULT 'PENDING'
                                 CHECK (status IN ('PENDING', 'PAID', 'WAIVED')),
    paid_at       timestamptz    NULL,
    created_at    timestamptz    NOT NULL DEFAULT now(),
    updated_at    timestamptz    NOT NULL DEFAULT now(),
    CONSTRAINT bill_participant_position_unique UNIQUE (bill_id, position),
    CONSTRAINT bill_participant_owner_is_waived CHECK (is_owner = false OR status = 'WAIVED'),
    CONSTRAINT bill_participant_paid_consistency CHECK ((status = 'PAID') = (paid_at IS NOT NULL))
);
COMMENT ON TABLE  bill_participant          IS 'Participant and their charge. One row = one "cobro".';
COMMENT ON COLUMN bill_participant.user_id  IS 'NULL for guest participants without an account.';
COMMENT ON COLUMN bill_participant.position IS '1..n; position 1 absorbs the cent adjustment of EQUAL splits.';

CREATE UNIQUE INDEX bill_participant_user_unique
    ON bill_participant (bill_id, user_id) WHERE user_id IS NOT NULL;
CREATE INDEX bill_participant_bill_idx ON bill_participant (bill_id);
CREATE INDEX bill_participant_user_idx ON bill_participant (user_id) WHERE user_id IS NOT NULL;

CREATE TRIGGER bill_participant_set_updated_at
    BEFORE UPDATE ON bill_participant
    FOR EACH ROW EXECUTE FUNCTION splitbill.set_updated_at();

-- Invariant: SUM(share_amount) = bill.total_amount, checked at COMMIT so that
-- participants can be inserted one by one inside the same transaction.
CREATE OR REPLACE FUNCTION splitbill.assert_bill_shares_balance()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_bill_id      uuid;
    v_total        numeric(14,2);
    v_sum          numeric(14,2);
BEGIN
    v_bill_id := COALESCE(NEW.bill_id, OLD.bill_id);

    SELECT b.total_amount INTO v_total
    FROM splitbill.bill b
    WHERE b.id = v_bill_id;

    IF v_total IS NULL THEN
        RETURN NULL; -- bill deleted in the same transaction (cascade); nothing to check
    END IF;

    SELECT COALESCE(SUM(p.share_amount), 0) INTO v_sum
    FROM splitbill.bill_participant p
    WHERE p.bill_id = v_bill_id;

    IF v_sum <> v_total THEN
        RAISE EXCEPTION 'bill % shares (%) do not balance total (%)', v_bill_id, v_sum, v_total
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NULL;
END;
$$;

CREATE CONSTRAINT TRIGGER bill_participant_shares_balance
    AFTER INSERT OR UPDATE OF share_amount OR DELETE ON bill_participant
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION splitbill.assert_bill_shares_balance();

-- -----------------------------------------------------------------------------
-- codi_qr — live QR issued at Pey. Ephemeral: deleted on payment or expiry.
-- -----------------------------------------------------------------------------
CREATE TABLE codi_qr (
    id                      uuid           PRIMARY KEY DEFAULT uuidv7(),
    participant_id          uuid           NOT NULL REFERENCES bill_participant (id) ON DELETE CASCADE,
    pey_codi_qr_id          text           NOT NULL,
    qr_string_encrypted     bytea          NOT NULL,
    encryption_key_version  smallint       NOT NULL CHECK (encryption_key_version > 0),
    amount_requested        numeric(14,2)  NOT NULL CHECK (amount_requested > 0),
    pey_num_ref             char(7)        NOT NULL CHECK (pey_num_ref ~ '^[0-9]{7}$'),
    created_at              timestamptz    NOT NULL DEFAULT now(),
    expires_at              timestamptz    NOT NULL,
    CONSTRAINT codi_qr_one_per_participant UNIQUE (participant_id),
    CONSTRAINT codi_qr_pey_id_unique       UNIQUE (pey_codi_qr_id),
    CONSTRAINT codi_qr_expiry_after_issue  CHECK (expires_at > created_at)
);
COMMENT ON TABLE  codi_qr                IS 'Live CoDi QR. Hard-deleted when paid or after 20 minutes.';
COMMENT ON COLUMN codi_qr.pey_codi_qr_id IS 'IDC assigned by Banxico, returned by Pey as "id".';

CREATE INDEX codi_qr_expires_idx ON codi_qr (expires_at);

-- -----------------------------------------------------------------------------
-- webhook_event — inbox for signed Pey webhooks
-- -----------------------------------------------------------------------------
CREATE TABLE webhook_event (
    id                      uuid        PRIMARY KEY DEFAULT uuidv7(),
    provider                text        NOT NULL DEFAULT 'PEY' CHECK (provider IN ('PEY')),
    provider_message_id     text        NOT NULL,
    event_type              text        NOT NULL,
    payload_encrypted       bytea       NULL,
    encryption_key_version  smallint    NULL CHECK (encryption_key_version IS NULL OR encryption_key_version > 0),
    provider_timestamp      timestamptz NOT NULL,
    received_at             timestamptz NOT NULL DEFAULT now(),
    status                  text        NOT NULL DEFAULT 'RECEIVED'
                                        CHECK (status IN ('RECEIVED', 'PROCESSED', 'FAILED', 'IGNORED')),
    attempts                smallint    NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    processed_at            timestamptz NULL,
    last_error              text        NULL,
    CONSTRAINT webhook_event_provider_message_unique UNIQUE (provider, provider_message_id)
);
COMMENT ON TABLE  webhook_event                     IS 'Webhook inbox. The HTTP endpoint only verifies the signature and inserts here.';
COMMENT ON COLUMN webhook_event.provider_message_id IS 'Standard Webhooks "webhook-id" header; deduplication key.';
COMMENT ON COLUMN webhook_event.payload_encrypted   IS 'Raw body ciphertext. Set to NULL by the retention job after 90 days.';

CREATE INDEX webhook_event_pending_idx ON webhook_event (received_at) WHERE status IN ('RECEIVED', 'FAILED');

-- -----------------------------------------------------------------------------
-- payment — immutable fact: a payment confirmed by Pey
-- -----------------------------------------------------------------------------
CREATE TABLE payment (
    id                  uuid           PRIMARY KEY DEFAULT uuidv7(),
    participant_id      uuid           NOT NULL REFERENCES bill_participant (id),
    webhook_event_id    uuid           NOT NULL REFERENCES webhook_event (id),
    pey_transaction_id  text           NOT NULL,
    pey_codi_qr_id      text           NOT NULL,
    currency_code       char(3)        NOT NULL REFERENCES currency (code),
    amount              numeric(14,2)  NOT NULL CHECK (amount > 0),
    paid_at             timestamptz    NOT NULL,
    pey_num_ref         text           NULL,
    pey_tracking_key    text           NULL,
    payer_bank_code     integer        NULL,
    payer_account_type  smallint       NULL,
    is_unexpected       boolean        NOT NULL DEFAULT false,
    created_at          timestamptz    NOT NULL DEFAULT now(),
    CONSTRAINT payment_pey_transaction_unique UNIQUE (pey_transaction_id)
);
COMMENT ON TABLE  payment                    IS 'Immutable payment fact. Never updated or deleted.';
COMMENT ON COLUMN payment.pey_transaction_id IS 'Pey transaction ULID; idempotency key against duplicate webhooks.';
COMMENT ON COLUMN payment.pey_codi_qr_id     IS 'IDC copy kept after codi_qr row deletion (documented denormalisation).';
COMMENT ON COLUMN payment.is_unexpected      IS 'True when the payment does not match an open charge (duplicate, wrong amount, unknown QR).';

CREATE INDEX payment_participant_idx ON payment (participant_id);
CREATE INDEX payment_codi_qr_idx     ON payment (pey_codi_qr_id);
CREATE INDEX payment_paid_at_idx     ON payment (paid_at DESC);

-- -----------------------------------------------------------------------------
-- outbox_event — transactional outbox for domain/integration events
-- -----------------------------------------------------------------------------
CREATE TABLE outbox_event (
    id              uuid        PRIMARY KEY DEFAULT uuidv7(),
    aggregate_type  text        NOT NULL,
    aggregate_id    uuid        NOT NULL,
    event_type      text        NOT NULL,
    payload         jsonb       NOT NULL,
    occurred_at     timestamptz NOT NULL DEFAULT now(),
    published_at    timestamptz NULL,
    attempts        smallint    NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    last_error      text        NULL
);
COMMENT ON TABLE outbox_event IS 'Transactional outbox. Rows are written in the same transaction as the state change.';

CREATE INDEX outbox_event_unpublished_idx ON outbox_event (occurred_at) WHERE published_at IS NULL;

-- -----------------------------------------------------------------------------
-- audit_log — append-only security/compliance trail (no personal data inside)
-- -----------------------------------------------------------------------------
CREATE TABLE audit_log (
    id             uuid        PRIMARY KEY DEFAULT uuidv7(),
    actor_user_id  uuid        NULL REFERENCES app_user (id),
    action         text        NOT NULL,
    entity_type    text        NOT NULL,
    entity_id      uuid        NULL,
    ip_hash        bytea       NULL,
    details        jsonb       NOT NULL DEFAULT '{}'::jsonb,
    occurred_at    timestamptz NOT NULL DEFAULT now()
);
COMMENT ON TABLE  audit_log         IS 'Append-only audit trail. "details" must never contain personal data.';

CREATE INDEX audit_log_actor_idx    ON audit_log (actor_user_id, occurred_at DESC);
CREATE INDEX audit_log_entity_idx   ON audit_log (entity_type, entity_id);
CREATE INDEX audit_log_occurred_idx ON audit_log (occurred_at DESC);

-- -----------------------------------------------------------------------------
-- data_subject_request — ARCO requests (LFPDPPP)
-- -----------------------------------------------------------------------------
CREATE TABLE data_subject_request (
    id                uuid        PRIMARY KEY DEFAULT uuidv7(),
    user_id           uuid        NOT NULL REFERENCES app_user (id),
    request_type      text        NOT NULL
                                  CHECK (request_type IN ('ACCESS', 'RECTIFICATION', 'CANCELLATION', 'OPPOSITION')),
    status            text        NOT NULL DEFAULT 'RECEIVED'
                                  CHECK (status IN ('RECEIVED', 'IN_PROGRESS', 'RESOLVED', 'REJECTED')),
    requested_at      timestamptz NOT NULL DEFAULT now(),
    due_at            timestamptz NOT NULL,
    resolved_at       timestamptz NULL,
    resolution_notes  text        NULL,
    CONSTRAINT data_subject_request_due_after_request CHECK (due_at > requested_at),
    CONSTRAINT data_subject_request_resolved_consistency
        CHECK ((status IN ('RESOLVED', 'REJECTED')) = (resolved_at IS NOT NULL))
);
COMMENT ON TABLE  data_subject_request        IS 'ARCO requests: Access, Rectification, Cancellation, Opposition.';
COMMENT ON COLUMN data_subject_request.due_at IS 'Legal deadline (20 business days from requested_at), computed by the application.';

CREATE INDEX data_subject_request_open_idx ON data_subject_request (due_at) WHERE status IN ('RECEIVED', 'IN_PROGRESS');

COMMIT;
