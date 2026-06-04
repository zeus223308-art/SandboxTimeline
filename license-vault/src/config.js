require("dotenv").config();

function resolveBaseUrl() {
  if (process.env.BASE_URL) {
    return process.env.BASE_URL.replace(/\/+$/, "");
  }

  if (process.env.VERCEL_URL) {
    return `https://${process.env.VERCEL_URL}`.replace(/\/+$/, "");
  }

  return "http://localhost:8787";
}

const config = {
  port: Number(process.env.PORT || 8787),
  baseUrl: resolveBaseUrl(),
  productMetadataId: process.env.PRODUCT_METADATA_ID || "prod_sandbox_timeline_premium_v1",
  licenseVaultSecret: (process.env.LICENSE_VAULT_SECRET || "").trim(),
  stripeSecretKey: (process.env.STRIPE_SECRET_KEY || "").trim(),
  stripeWebhookSecret: (process.env.STRIPE_WEBHOOK_SECRET || "").trim(),
  stripePriceId: (process.env.STRIPE_PRICE_ID || "").trim(),
  databasePath: process.env.DATABASE_PATH || "./data/licenses.db",
  smtp: {
    host: (process.env.SMTP_HOST || "").trim(),
    port: Number(process.env.SMTP_PORT || 587),
    secure: process.env.SMTP_SECURE === "true",
    user: (process.env.SMTP_USER || "").trim(),
    pass: (process.env.SMTP_PASS || "").trim(),
    from: (process.env.EMAIL_FROM || "Sandbox Timeline <noreply@localhost>").trim()
  }
};

function assertStripeConfigured() {
  if (!config.stripeSecretKey) {
    throw new Error("STRIPE_SECRET_KEY is not set.");
  }

  if (!config.stripePriceId) {
    throw new Error("STRIPE_PRICE_ID is not set.");
  }
}

module.exports = { config, assertStripeConfigured };
