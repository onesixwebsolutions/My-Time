-- DayGrid migration 0003 — a distinct "disabled by an administrator" account state.
--
-- Before this migration an admin "lock" was stored as lockout_end = DateTimeOffset.MaxValue, the
-- same column Identity uses for the brute-force lockout — so a password reset (which ends a
-- brute-force lockout) also silently undid an admin lock. is_disabled is only ever changed by the
-- admin lock/unlock endpoints. Runs inside one transaction (SchemaMigrator).

ALTER TABLE users ADD COLUMN is_disabled bool NOT NULL DEFAULT false;

-- Carry existing admin locks over: they were written as lockout_end = 9999-12-31 (MaxValue).
UPDATE users SET is_disabled = true WHERE lockout_end >= TIMESTAMPTZ '9999-01-01 00:00:00+00';
