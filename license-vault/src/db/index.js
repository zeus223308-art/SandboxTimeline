const { config } = require("../config");

function hasPostgresUrl() {
  return Boolean(
    process.env.POSTGRES_URL ||
      process.env.sandboxtimeline_POSTGRES_URL ||
      process.env.SANDBOXTIMELINE_POSTGRES_URL
  );
}

function usePostgres() {
  return hasPostgresUrl();
}

function loadImpl() {
  if (usePostgres()) {
    return require("./postgres");
  }

  if (process.env.VERCEL) {
    return require("./vercel-stub");
  }

  return require("./sqlite");
}

const impl = loadImpl();

let readyPromise;

async function ensureDatabaseReady() {
  if (!readyPromise) {
    readyPromise = (async () => {
      if (usePostgres()) {
        await impl.initDatabase();
        return;
      }

      if (process.env.VERCEL) {
        await impl.initDatabase();
        return;
      }

      impl.initDatabase(config.databasePath);
    })();
  }

  return readyPromise;
}

function isDatabaseConfigured() {
  return usePostgres() || !process.env.VERCEL;
}

module.exports = {
  usePostgres,
  isDatabaseConfigured,
  ensureDatabaseReady,
  findByHash: (...args) => impl.findByHash(...args),
  findByCheckoutSessionId: (...args) => impl.findByCheckoutSessionId(...args),
  findBySubscriptionId: (...args) => impl.findBySubscriptionId(...args),
  updateSubscriptionStatus: (...args) => impl.updateSubscriptionStatus(...args),
  markRevealed: (...args) => impl.markRevealed(...args),
  upsertLicenseFromStripe: (...args) => impl.upsertLicenseFromStripe(...args)
};
