const { config } = require("./config");
const { createApp } = require("./createApp");

createApp().then((app) => {
  app.listen(config.port, () => {
    console.log(`License Vault listening on ${config.baseUrl}`);
    console.log(`Validate API: POST ${config.baseUrl}/v1/license/validate`);
    console.log(`Checkout: GET ${config.baseUrl}/v1/checkout/start`);
  });
});
