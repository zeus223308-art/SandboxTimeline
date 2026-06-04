const crypto = require("crypto");

const PRODUCT_METADATA_ID = "prod_sandbox_timeline_premium_v1";

function normalizeLicenseKey(licenseKey) {
  return (licenseKey || "").trim();
}

function hashLicenseKey(licenseKey) {
  const normalized = normalizeLicenseKey(licenseKey);
  if (!normalized) {
    return "";
  }

  return crypto.createHash("sha256").update(normalized, "utf8").digest("hex").toLowerCase();
}

function generateLicenseKey() {
  const chunk = () => crypto.randomBytes(2).toString("hex").toUpperCase();
  return `ST-${chunk()}-${chunk()}-${chunk()}-${chunk()}`;
}

function buildEntitledResponse(record) {
  return {
    valid: true,
    premium: true,
    product_metadata_id: record.product_metadata_id || PRODUCT_METADATA_ID,
    expires_at: record.expires_at || null,
    subscription_id: record.subscription_id || null,
    customer_id: record.customer_id || null
  };
}

function buildRevokedResponse() {
  return {
    valid: false,
    premium: false,
    product_metadata_id: PRODUCT_METADATA_ID
  };
}

module.exports = {
  PRODUCT_METADATA_ID,
  normalizeLicenseKey,
  hashLicenseKey,
  generateLicenseKey,
  buildEntitledResponse,
  buildRevokedResponse
};
