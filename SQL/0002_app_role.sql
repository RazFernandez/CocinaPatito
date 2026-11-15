-- =============================================================================
-- SplitBill — least-privilege application role
-- Run once per cluster by an administrator. The password is NEVER stored in
-- this repository: set it out of band, e.g.
--     ALTER ROLE splitbill_app PASSWORD '<from secret store>';
-- or use certificate / IAM authentication in production.
-- The application connects as splitbill_app; migrations run as the owner role.
-- =============================================================================

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'splitbill_app') THEN
        CREATE ROLE splitbill_app LOGIN NOINHERIT NOCREATEDB NOCREATEROLE NOSUPERUSER;
    END IF;
END
$$;

GRANT USAGE ON SCHEMA splitbill TO splitbill_app;

-- Business tables: read/write, never DDL.
GRANT SELECT, INSERT, UPDATE ON
    splitbill.app_user,
    splitbill.privacy_consent,
    splitbill.refresh_token,
    splitbill.user_bank_account,
    splitbill.bill,
    splitbill.bill_participant,
    splitbill.webhook_event,
    splitbill.outbox_event,
    splitbill.data_subject_request
TO splitbill_app;

-- Ephemeral QR rows are the only business rows the application deletes.
GRANT SELECT, INSERT, DELETE ON splitbill.codi_qr TO splitbill_app;

-- Immutable facts: insert and read only.
GRANT SELECT, INSERT ON splitbill.payment, splitbill.audit_log TO splitbill_app;

-- Retention job needs to purge expired tokens.
GRANT DELETE ON splitbill.refresh_token TO splitbill_app;

-- Reference data: read only.
GRANT SELECT ON splitbill.currency TO splitbill_app;

-- Hardening: nothing executable beyond the trigger functions (invoked implicitly).
REVOKE ALL ON SCHEMA public FROM splitbill_app;
