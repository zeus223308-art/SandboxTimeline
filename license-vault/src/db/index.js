const { config } = require("../config");

function usePostgres() {
  return Boolean(
    process.env.POSTGRES_URL ||
      process.env.sandboxtimeline_POSTGRES_URL ||
      process.env.SANDBOXTIMELINE_POSTGRES_URL
  );
}

const impl = usePostgres() ? require("./postgres") : require("./sqlite");

let readyPromise;

async function ensureDatabaseReady() {
  if (!readyPromise) {
    readyPromise = (async () => {
      if (usePostgres()) {
        await impl.initDatabase();
        return;
      }

      impl.initDatabase(config.databasePath);
    })();
  }

  return readyPromise;
}

module.exports = {
  usePostgres,
  ensureDatabaseReady,
  findByHash: (...args) => impl.findByHash(...args),
  findByCheckoutSessionId: (...args) => impl.findByCheckoutSessionId(...args),
  findBySubscriptionId: (...args) => impl.findBySubscriptionId(...args),
  updateSubscriptionStatus: (...args) => impl.updateSubscriptionStatus(...args),
  markRevealed: (...args) => impl.markRevealed(...args),
  upsertLicenseFromStripe: (...args) => impl.upsertLicenseFromStripe(...args)
};
