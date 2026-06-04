async function initDatabase() {
  // No-op until Vercel Postgres is connected.
}

async function findByHash() {
  return null;
}

async function findByCheckoutSessionId() {
  return null;
}

async function findBySubscriptionId() {
  return null;
}

async function updateSubscriptionStatus() {}

async function markRevealed() {}

async function upsertLicenseFromStripe() {
  throw new Error(
    "POSTGRES_URL is not configured. In Vercel: Storage → Postgres → Connect to this project."
  );
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
