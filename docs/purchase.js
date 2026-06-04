(function () {
  var config = window.SandboxTimelinePurchaseConfig || {};
  var storeUrl = (config.storeListingUrl || "").trim();
  var buyButton = document.getElementById("buyPremiumBtn");
  var downloadBtn = document.getElementById("downloadLatestBtn");

  if (buyButton && storeUrl) {
    buyButton.href = storeUrl;
    buyButton.textContent = "Microsoft Store에서 설치 · 프리미엄";
    buyButton.removeAttribute("aria-disabled");
  }

  if (downloadBtn && storeUrl) {
    downloadBtn.textContent = "Microsoft Store에서 설치";
    downloadBtn.href = storeUrl;
    downloadBtn.removeAttribute("download");
  }
})();
