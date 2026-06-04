function escapeHtml(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function renderSuccessPage(record) {
  const key = escapeHtml(record.license_key_plain);
  const expires = record.expires_at
    ? `<p class="muted">구독 갱신일(UTC): ${escapeHtml(record.expires_at)}</p>`
    : "";

  return `<!DOCTYPE html>
<html lang="ko">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>결제 완료 — Sandbox Timeline</title>
  <style>
    body { font-family: "Segoe UI", sans-serif; background: #0f1419; color: #e7ecf3; margin: 0; padding: 2rem; }
    .card { max-width: 560px; margin: 0 auto; background: #1a2332; border-radius: 12px; padding: 1.5rem 2rem; box-shadow: 0 8px 32px rgba(0,0,0,.35); }
    h1 { font-size: 1.35rem; margin-top: 0; }
    .key { font-family: Consolas, monospace; font-size: 1.1rem; background: #0b1020; padding: 1rem; border-radius: 8px; word-break: break-all; user-select: all; }
    .muted { color: #9aa8bc; font-size: 0.9rem; }
    button { margin-top: 1rem; background: #3b82f6; color: #fff; border: 0; padding: 0.6rem 1rem; border-radius: 8px; cursor: pointer; }
    ol { padding-left: 1.2rem; }
  </style>
</head>
<body>
  <div class="card">
    <h1>프리미엄 라이선스가 발급되었습니다</h1>
    <p>아래 키를 Sandbox Timeline 앱 <strong>라이선스 키 입력</strong>란에 붙여넣으세요.</p>
    <p class="key" id="licenseKey">${key}</p>
    ${expires}
    <button type="button" onclick="copyKey()">키 복사</button>
    <p class="muted">이메일을 설정해 두었다면 같은 키가 메일로도 전송됩니다.</p>
    <ol class="muted">
      <li>Sandbox Timeline 실행 (관리자 권한)</li>
      <li>라이선스 섹션 펼치기 → 키 입력 → 확인</li>
    </ol>
  </div>
  <script>
    function copyKey() {
      const text = document.getElementById('licenseKey').textContent.trim();
      navigator.clipboard.writeText(text).then(function () { alert('복사되었습니다.'); });
    }
  </script>
</body>
</html>`;
}

function renderCancelPage() {
  return `<!DOCTYPE html>
<html lang="ko">
<head>
  <meta charset="UTF-8" />
  <title>결제 취소</title>
  <style>
    body { font-family: "Segoe UI", sans-serif; background: #0f1419; color: #e7ecf3; padding: 2rem; }
  </style>
</head>
<body>
  <h1>결제가 취소되었습니다</h1>
  <p>다시 시도하려면 구매 페이지로 돌아가 Checkout을 시작하세요.</p>
</body>
</html>`;
}

module.exports = { renderSuccessPage, renderCancelPage };
