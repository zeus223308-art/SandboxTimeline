(function () {
  var config = window.SandboxTimelinePurchaseConfig || {};
  var base = (config.vaultBaseUrl || "").replace(/\/+$/, "");
  var button = document.getElementById("buyPremiumBtn");

  if (!button || !base) {
    return;
  }

  button.href = base + "/v1/checkout/start";
  button.removeAttribute("aria-disabled");
})();
