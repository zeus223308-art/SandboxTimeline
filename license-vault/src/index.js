const serverless = require("serverless-http");
const { createApp } = require("./createApp");

let app;
let handler;

module.exports = async (req, res) => {
  if (!app) {
    app = await createApp();
    handler = serverless(app);
  }

  return handler(req, res);
};
