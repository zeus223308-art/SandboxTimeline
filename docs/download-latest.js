(function () {
  const repo = "zeus223308-art/SandboxTimeline";
  const preferredAssetName = "SandboxTimeline-win-x64.zip";
  const fallbackDownloadUrl =
    "https://github.com/zeus223308-art/SandboxTimeline/releases/latest/download/SandboxTimeline-win-x64.zip";

  const downloadBtn = document.getElementById("downloadLatestBtn");
  const downloadLink = document.getElementById("downloadLatestLink");
  const versionNote = document.getElementById("latestVersionNote");

  function applyDownloadUrl(downloadUrl, versionLabel) {
    if (downloadBtn) {
      downloadBtn.href = downloadUrl;
      downloadBtn.setAttribute("download", preferredAssetName);
      if (versionLabel) {
        downloadBtn.textContent = `최신 버전 다운로드 ${versionLabel} (.zip)`;
      }
    }

    if (downloadLink) {
      downloadLink.href = downloadUrl;
      if (versionLabel) {
        downloadLink.textContent = `최신 zip 직접 다운로드 ${versionLabel}`;
      }
    }

    if (versionNote && versionLabel) {
      versionNote.textContent = `GitHub 최신 릴리스: ${versionLabel}`;
    }
  }

  async function resolveLatestReleaseDownload() {
    try {
      const response = await fetch(`https://api.github.com/repos/${repo}/releases/latest`, {
        headers: { Accept: "application/vnd.github+json" }
      });

      if (!response.ok) {
        applyDownloadUrl(fallbackDownloadUrl, null);
        return;
      }

      const release = await response.json();
      const tag = release.tag_name || release.name || "";
      const asset = (release.assets || []).find(function (item) {
        return item.name === preferredAssetName;
      });

      if (asset && asset.browser_download_url) {
        applyDownloadUrl(asset.browser_download_url, tag);
        return;
      }

      applyDownloadUrl(fallbackDownloadUrl, tag || null);
    } catch {
      applyDownloadUrl(fallbackDownloadUrl, null);
    }
  }

  resolveLatestReleaseDownload();
})();
