const express = require("express");
const path = require("path");
const { config, assertStripeConfigured } = require("./config");
const {
  ensureDatabaseReady,
  findByHash,
  findByCheckoutSessionId,
  markRevealed
} = require("./db");
const {
  buildEntitledResponse,
  buildRevokedResponse,
  PRODUCT_METADATA_ID
} = require("./license");
const {
  createCheckoutSession,
  fulfillCheckoutSession,
  handleStripeWebhook
} = require("./stripe-handlers");
const { renderSuccessPage, renderCancelPage } = require("./pages");

async function createApp() {
  await ensureDatabaseReady();

  const app = express();

  app.get("/health", (_req, res) => {
    res.json({
      ok: true,
      service: "sandboxtimeline-license-vault",
      database: require("./db").usePostgres() ? "postgres" : "sqlite"
    });
  });

  function requireVaultSecret(req, res, next) {
    if (!config.licenseVaultSecret) {
      next();
      return;
    }

    const provided = (req.get("X-License-Vault-Secret") || "").trim();
    if (provided !== config.licenseVaultSecret) {
      res.status(401).json(buildRevokedResponse());
      return;
    }

    next();
  }

  app.post(
    "/v1/license/validate",
    express.json({ limit: "32kb" }),
    requireVaultSecret,
    async (req, res) => {
      const licenseKeyHash = (req.body?.license_key_hash || req.body?.LicenseKeyHash || "")
        .trim()
        .toLowerCase();
      const productMetadataId = (
        req.body?.product_metadata_id ||
        req.body?.ProductMetadataId ||
        ""
      ).trim();

      if (!licenseKeyHash) {
        res.status(400).json(buildRevokedResponse());
        return;
      }

      if (
        productMetadataId &&
        productMetadataId.toLowerCase() !== PRODUCT_METADATA_ID.toLowerCase()
      ) {
        res.status(403).json(buildRevokedResponse());
        return;
      }

      const record = await findByHash(licenseKeyHash);
      if (!record || record.status !== "active") {
        res.status(200).json(buildRevokedResponse());
        return;
      }

      if (record.expires_at) {
        const expiresAt = new Date(record.expires_at);
        if (!Number.isNaN(expiresAt.getTime()) && expiresAt.getTime() < Date.now()) {
          res.status(200).json(buildRevokedResponse());
          return;
        }
      }

      res.status(200).json(buildEntitledResponse(record));
    }
  );

  app.get("/v1/checkout/start", async (req, res) => {
    try {
      assertStripeConfigured();
      const email = (req.query.email || "").trim();
      const session = await createCheckoutSession(email);
      res.redirect(303, session.url);
    } catch (error) {
      console.error("Checkout start failed:", error);
      res.status(500).send(`Checkout could not be started: ${error.message}`);
    }
  });

  app.post("/v1/checkout/create-session", express.json(), async (req, res) => {
    try {
      assertStripeConfigured();
      const email = (req.body?.email || "").trim();
      const session = await createCheckoutSession(email);
      res.json({ url: session.url, id: session.id });
    } catch (error) {
      console.error("Checkout session failed:", error);
      res.status(500).json({ error: error.message });
    }
  });

  app.post(
    "/v1/webhooks/stripe",
    express.raw({ type: "application/json" }),
    async (req, res) => {
      const signature = req.get("stripe-signature");
      if (!signature) {
        res.status(400).send("Missing stripe-signature header.");
        return;
      }

      try {
        const eventType = await handleStripeWebhook(req.body, signature);
        res.json({ received: true, type: eventType });
      } catch (error) {
        console.error("Stripe webhook failed:", error);
        res.status(400).send(`Webhook Error: ${error.message}`);
      }
    }
  );

  app.get("/success", async (req, res) => {
    const sessionId = (req.query.session_id || "").trim();
    if (!sessionId) {
      res.status(400).send("Missing session_id.");
      return;
    }

    let record = await findByCheckoutSessionId(sessionId);
    if (!record) {
      try {
        await fulfillCheckoutSession({ id: sessionId });
        record = await findByCheckoutSessionId(sessionId);
      } catch (error) {
        console.error("Success page fulfillment failed:", error);
      }
    }

    if (!record) {
      res
        .status(404)
        .send(
          "License is not ready yet. Wait a few seconds and refresh, or check your email."
        );
      return;
    }

    await markRevealed(sessionId);

    res.setHeader("Content-Type", "text/html; charset=utf-8");
    res.send(renderSuccessPage(record));
  });

  app.get("/cancel", (_req, res) => {
    res.setHeader("Content-Type", "text/html; charset=utf-8");
    res.send(renderCancelPage());
  });

  app.get("/v1/license/reveal/:sessionId", async (req, res) => {
    const sessionId = (req.params.sessionId || "").trim();
    let record = await findByCheckoutSessionId(sessionId);

    if (!record) {
      try {
        await fulfillCheckoutSession({ id: sessionId });
        record = await findByCheckoutSessionId(sessionId);
      } catch (error) {
        res.status(404).json({ error: "License not found." });
        return;
      }
    }

    if (!record) {
      res.status(404).json({ error: "License not found." });
      return;
    }

    res.json({
      license_key: record.license_key_plain,
      expires_at: record.expires_at,
      status: record.status
    });
  });

  app.use(express.static(path.join(__dirname, "..", "public")));

  return app;
}

module.exports = { createApp };
