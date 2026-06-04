const fs = require("fs");
const path = require("path");
const Database = require("better-sqlite3");
const { hashLicenseKey } = require("../license");

let db;

function initDatabase(databasePath) {
  const directory = path.dirname(databasePath);
  fs.mkdirSync(directory, { recursive: true });

  db = new Database(databasePath);
  db.pragma("journal_mode = WAL");

  db.exec(`
    CREATE TABLE IF NOT EXISTS licenses (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      license_key_hash TEXT NOT NULL UNIQUE,
      license_key_plain TEXT NOT NULL,
      product_metadata_id TEXT NOT NULL,
      customer_email TEXT,
      stripe_customer_id TEXT,
      stripe_subscription_id TEXT,
      stripe_checkout_session_id TEXT,
      status TEXT NOT NULL DEFAULT 'active',
      expires_at TEXT,
      created_at TEXT NOT NULL DEFAULT (datetime('now')),
      updated_at TEXT NOT NULL DEFAULT (datetime('now')),
      revealed_on_success INTEGER NOT NULL DEFAULT 0
    );

    CREATE INDEX IF NOT EXISTS idx_licenses_subscription
      ON licenses (stripe_subscription_id);

    CREATE INDEX IF NOT EXISTS idx_licenses_checkout_session
      ON licenses (stripe_checkout_session_id);
  `);
}

function insertLicense(record) {
  db.prepare(`
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
      @license_key_hash,
      @license_key_plain,
      @product_metadata_id,
      @customer_email,
      @stripe_customer_id,
      @stripe_subscription_id,
      @stripe_checkout_session_id,
      @status,
      @expires_at
    )
  `).run(record);
}

async function findByHash(licenseKeyHash) {
  return db
    .prepare(`SELECT * FROM licenses WHERE license_key_hash = ? LIMIT 1`)
    .get(licenseKeyHash);
}

async function findByCheckoutSessionId(checkoutSessionId) {
  return db
    .prepare(
      `SELECT * FROM licenses WHERE stripe_checkout_session_id = ? LIMIT 1`
    )
    .get(checkoutSessionId);
}

async function findBySubscriptionId(subscriptionId) {
  return db
    .prepare(
      `SELECT * FROM licenses WHERE stripe_subscription_id = ? LIMIT 1`
    )
    .get(subscriptionId);
}

async function updateSubscriptionStatus(subscriptionId, status, expiresAt) {
  db.prepare(`
    UPDATE licenses
    SET status = ?, expires_at = ?, updated_at = datetime('now')
    WHERE stripe_subscription_id = ?
  `).run(status, expiresAt, subscriptionId);
}

async function markRevealed(checkoutSessionId) {
  db.prepare(`
    UPDATE licenses
    SET revealed_on_success = 1, updated_at = datetime('now')
    WHERE stripe_checkout_session_id = ?
  `).run(checkoutSessionId);
}

async function upsertLicenseFromStripe(record) {
  const licenseKeyHash = hashLicenseKey(record.licenseKeyPlain);
  const existing = await findByHash(licenseKeyHash);

  if (existing) {
    db.prepare(`
      UPDATE licenses
      SET
        customer_email = COALESCE(?, customer_email),
        stripe_customer_id = COALESCE(?, stripe_customer_id),
        stripe_subscription_id = COALESCE(?, stripe_subscription_id),
        stripe_checkout_session_id = COALESCE(?, stripe_checkout_session_id),
        status = ?,
        expires_at = COALESCE(?, expires_at),
        updated_at = datetime('now')
      WHERE license_key_hash = ?
    `).run(
      record.customerEmail,
      record.stripeCustomerId,
      record.stripeSubscriptionId,
      record.stripeCheckoutSessionId,
      record.status,
      record.expiresAt,
      licenseKeyHash
    );

    return findByHash(licenseKeyHash);
  }

  insertLicense({
    license_key_hash: licenseKeyHash,
    license_key_plain: record.licenseKeyPlain,
    product_metadata_id: record.productMetadataId,
    customer_email: record.customerEmail,
    stripe_customer_id: record.stripeCustomerId,
    stripe_subscription_id: record.stripeSubscriptionId,
    stripe_checkout_session_id: record.stripeCheckoutSessionId,
    status: record.status,
    expires_at: record.expiresAt
  });

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
