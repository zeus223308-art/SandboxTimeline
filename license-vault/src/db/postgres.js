const { sql } = require("@vercel/postgres");
const { hashLicenseKey } = require("../license");

async function initDatabase() {
  await sql`
    CREATE TABLE IF NOT EXISTS licenses (
      id SERIAL PRIMARY KEY,
      license_key_hash TEXT NOT NULL UNIQUE,
      license_key_plain TEXT NOT NULL,
      product_metadata_id TEXT NOT NULL,
      customer_email TEXT,
      stripe_customer_id TEXT,
      stripe_subscription_id TEXT,
      stripe_checkout_session_id TEXT,
      status TEXT NOT NULL DEFAULT 'active',
      expires_at TIMESTAMPTZ,
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      revealed_on_success BOOLEAN NOT NULL DEFAULT FALSE
    )
  `;

  await sql`
    CREATE INDEX IF NOT EXISTS idx_licenses_subscription
      ON licenses (stripe_subscription_id)
  `;

  await sql`
    CREATE INDEX IF NOT EXISTS idx_licenses_checkout_session
      ON licenses (stripe_checkout_session_id)
  `;
}

async function findByHash(licenseKeyHash) {
  const result = await sql`
    SELECT * FROM licenses WHERE license_key_hash = ${licenseKeyHash} LIMIT 1
  `;
  return result.rows[0] || null;
}

async function findByCheckoutSessionId(checkoutSessionId) {
  const result = await sql`
    SELECT * FROM licenses
    WHERE stripe_checkout_session_id = ${checkoutSessionId}
    LIMIT 1
  `;
  return result.rows[0] || null;
}

async function findBySubscriptionId(subscriptionId) {
  const result = await sql`
    SELECT * FROM licenses
    WHERE stripe_subscription_id = ${subscriptionId}
    LIMIT 1
  `;
  return result.rows[0] || null;
}

async function updateSubscriptionStatus(subscriptionId, status, expiresAt) {
  await sql`
    UPDATE licenses
    SET status = ${status},
        expires_at = ${expiresAt},
        updated_at = NOW()
    WHERE stripe_subscription_id = ${subscriptionId}
  `;
}

async function markRevealed(checkoutSessionId) {
  await sql`
    UPDATE licenses
    SET revealed_on_success = TRUE, updated_at = NOW()
    WHERE stripe_checkout_session_id = ${checkoutSessionId}
  `;
}

async function upsertLicenseFromStripe(record) {
  const licenseKeyHash = hashLicenseKey(record.licenseKeyPlain);
  const existing = await findByHash(licenseKeyHash);

  if (existing) {
    await sql`
      UPDATE licenses
      SET
        customer_email = COALESCE(${record.customerEmail}, customer_email),
        stripe_customer_id = COALESCE(${record.stripeCustomerId}, stripe_customer_id),
        stripe_subscription_id = COALESCE(${record.stripeSubscriptionId}, stripe_subscription_id),
        stripe_checkout_session_id = COALESCE(${record.stripeCheckoutSessionId}, stripe_checkout_session_id),
        status = ${record.status},
        expires_at = COALESCE(${record.expiresAt}, expires_at),
        updated_at = NOW()
      WHERE license_key_hash = ${licenseKeyHash}
    `;
    return findByHash(licenseKeyHash);
  }

  await sql`
    INSERT INTO licenses (
      license_key_hash,
      license_key_plain,
      product_metadata_id,
      customer_email,
      stripe_customer_id,
      stripe_subscription_id,
      stripe_checkout_session_id,
      status,
      expires_at
    ) VALUES (
      ${licenseKeyHash},
      ${record.licenseKeyPlain},
      ${record.productMetadataId},
      ${record.customerEmail},
      ${record.stripeCustomerId},
      ${record.stripeSubscriptionId},
      ${record.stripeCheckoutSessionId},
      ${record.status},
      ${record.expiresAt}
    )
  `;

  return findByHash(licenseKeyHash);
}

module.exports = {
  initDatabase,
  findByHash,
  findByCheckoutSessionId,
  findBySubscriptionId,
  updateSubscriptionStatus,
  markRevealed,
  upsertLicenseFromStripe
};
