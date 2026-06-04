const { createApp } = require("./createApp");

let appInstance;
let appReady;

async function getApp() {
  if (!appReady) {
    appReady = createApp().then((app) => {
      appInstance = app;
      return app;
    });
  }

  return appReady;
}

module.exports = async (req, res) => {
  const app = await getApp();
  return app(req, res);
};
