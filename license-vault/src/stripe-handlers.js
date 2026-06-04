const Stripe = require("stripe");
const { config } = require("./config");
const {
  generateLicenseKey,
  PRODUCT_METADATA_ID
} = require("./license");
const {
  upsertLicenseFromStripe,
  findBySubscriptionId,
  updateSubscriptionStatus
} = require("./db");
const { sendLicenseKeyEmail } = require("./email");

function getStripe() {
  return new Stripe(config.stripeSecretKey);
}

async function createCheckoutSession(customerEmail) {
  const stripe = getStripe();

  const session = await stripe.checkout.sessions.create({
    mode: "subscription",
    line_items: [
      {
        price: config.stripePriceId,
        quantity: 1
      }
    ],
    success_url: `${config.baseUrl}/success?session_id={CHECKOUT_SESSION_ID}`,
    cancel_url: `${config.baseUrl}/cancel`,
    allow_promotion_codes: true,
    billing_address_collection: "auto",
    customer_email: customerEmail || undefined,
    metadata: {
      product_metadata_id: PRODUCT_METADATA_ID
    },
    subscription_data: {
      metadata: {
        product_metadata_id: PRODUCT_METADATA_ID
      }
    }
  });

  return session;
}

function mapSubscriptionStatus(stripeStatus) {
  if (stripeStatus === "active" || stripeStatus === "trialing") {
    return "active";
  }

  if (stripeStatus === "past_due") {
    return "past_due";
  }

  return "revoked";
}

function formatStripePeriodEnd(subscription) {
  const periodEnd = subscription?.current_period_end;
  if (!periodEnd) {
    return null;
  }

  return new Date(periodEnd * 1000).toISOString();
}

async function fulfillCheckoutSession(session) {
  const stripe = getStripe();
  const fullSession = await stripe.checkout.sessions.retrieve(session.id, {
    expand: ["subscription", "customer"]
  });

  if (fullSession.payment_status !== "paid" && fullSession.status !== "complete") {
    return null;
  }

  const subscription =
    typeof fullSession.subscription === "string"
      ? await stripe.subscriptions.retrieve(fullSession.subscription)
      : fullSession.subscription;

  const customerId =
    typeof fullSession.customer === "string"
      ? fullSession.customer
      : fullSession.customer?.id;

  const customerEmail =
    fullSession.customer_details?.email ||
    fullSession.customer_email ||
    null;

  const licenseKey = generateLicenseKey();
  const expiresAt = subscription ? formatStripePeriodEnd(subscription) : null;

  const record = await upsertLicenseFromStripe({
    licenseKeyPlain: licenseKey,
    productMetadataId: PRODUCT_METADATA_ID,
    customerEmail,
    stripeCustomerId: customerId || null,
    stripeSubscriptionId: subscription?.id || null,
    stripeCheckoutSessionId: fullSession.id,
    status: "active",
    expiresAt
  });

  try {
    await sendLicenseKeyEmail({
      to: customerEmail,
      licenseKey,
      expiresAt
    });
  } catch (error) {
    console.error("License email failed:", error.message);
  }

  return record;
}

async function syncSubscription(subscription) {
  const status = mapSubscriptionStatus(subscription.status);
  const expiresAt = formatStripePeriodEnd(subscription);
  const existing = await findBySubscriptionId(subscription.id);

  if (existing) {
    await updateSubscriptionStatus(subscription.id, status, expiresAt);
    return existing;
  }

  const customerId =
    typeof subscription.customer === "string"
      ? subscription.customer
      : subscription.customer?.id;

  const licenseKey = generateLicenseKey();
  return await upsertLicenseFromStripe({
    licenseKeyPlain: licenseKey,
    productMetadataId: PRODUCT_METADATA_ID,
    customerEmail: null,
    stripeCustomerId: customerId || null,
    stripeSubscriptionId: subscription.id,
    stripeCheckoutSessionId: null,
    status,
    expiresAt
  });
}

async function handleStripeWebhook(rawBody, signature) {
  const stripe = getStripe();
  const event = stripe.webhooks.constructEvent(
    rawBody,
    signature,
    config.stripeWebhookSecret
  );

  switch (event.type) {
    case "checkout.session.completed": {
      await fulfillCheckoutSession(event.data.object);
      break;
    }
    case "customer.subscription.updated": {
      await syncSubscription(event.data.object);
      break;
    }
    case "customer.subscription.deleted": {
      const subscription = event.data.object;
      await updateSubscriptionStatus(
        subscription.id,
        "revoked",
        formatStripePeriodEnd(subscription)
      );
      break;
    }
    case "invoice.payment_failed": {
      const invoice = event.data.object;
      if (invoice.subscription) {
        const subscriptionId =
          typeof invoice.subscription === "string"
            ? invoice.subscription
            : invoice.subscription.id;
        await updateSubscriptionStatus(subscriptionId, "past_due", null);
      }
      break;
    }
    default:
      break;
  }

  return event.type;
}

module.exports = {
  createCheckoutSession,
  fulfillCheckoutSession,
  handleStripeWebhook
};
