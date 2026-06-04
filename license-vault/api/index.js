const serverless = require("serverless-http");
const { createApp } = require("../src/createApp");

let app;
let handler;

module.exports = async (req, res) => {
  if (!app) {
    app = await createApp();
    handler = serverless(app, {
      binary: false
    });
  }

  return handler(req, res);
};
