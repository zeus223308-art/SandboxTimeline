# License Vault — Render에 “밖에” 올리기 (약 10분)

저는 당신의 Render/Stripe 계정에 로그인할 수 없습니다. 아래는 **클릭만으로 공개 URL**을 만드는 방법입니다.

완료 후 주소 예: `https://sandboxtimeline-license-vault.onrender.com`

---

## 1. GitHub에 코드가 있어야 함

`license-vault` 폴더가 이미 `zeus223308-art/SandboxTimeline` 저장소에 push 되어 있어야 합니다.

```powershell
cd C:\Users\codib\SandboxTimeline
git add license-vault docs
git commit -m "Add license vault server and Render deploy config."
git push origin master
```

---

## 2. Render 가입

1. https://render.com 가입 (GitHub 연동 권장)
2. **New +** → **Web Service**
3. **Build and deploy from a Git repository** → GitHub `SandboxTimeline` 연결

---

## 3. 서비스 설정

| 항목 | 값 |
|------|-----|
| **Root Directory** | `license-vault` |
| **Runtime** | Node |
| **Build Command** | `npm install` |
| **Start Command** | `npm start` |
| **Plan** | Free |

**Advanced → Add Disk** (무료 플랜에서 DB 유지용):

- Mount Path: `/var/data`
- Size: 1 GB

---

## 4. Environment Variables (Environment)

Render 대시보드 → **Environment** 에 추가:

| Key | Value |
|-----|--------|
| `BASE_URL` | 배포 후 URL (아래 5번에서 확인 후 **수정**) |
| `STRIPE_SECRET_KEY` | `sk_test_...` 또는 Live `sk_live_...` |
| `STRIPE_PRICE_ID` | `price_...` |
| `STRIPE_WEBHOOK_SECRET` | 6번 Stripe Webhook에서 받은 `whsec_...` |
| `LICENSE_VAULT_SECRET` | 긴 랜덤 문자열 (앱 `SANDBOXTIMELINE_LICENSE_VAULT_SECRET`과 동일) |
| `DATABASE_PATH` | `/var/data/licenses.db` |
| `PRODUCT_METADATA_ID` | `prod_sandbox_timeline_premium_v1` |

SMTP(이메일)는 나중에 추가 가능.

첫 배포 시 `BASE_URL`은 임시로 `https://서비스이름.onrender.com` 넣고, 배포 후 **Settings → URL** 과 정확히 맞추세요.

---

## 5. Deploy

**Create Web Service** → 빌드 완료까지 2~5분.

브라우저에서 열기:

- `https://YOUR-SERVICE.onrender.com/health` → `{"ok":true,...}`

이 URL이 곧 **BASE_URL** 입니다.

---

## 6. Stripe Webhook (필수)

Stripe Dashboard → **Developers → Webhooks → Add endpoint**

| | |
|--|--|
| **Endpoint URL** | `https://YOUR-SERVICE.onrender.com/v1/webhooks/stripe` |
| **Events** | `checkout.session.completed`, `customer.subscription.updated`, `customer.subscription.deleted`, `invoice.payment_failed` |

생성 후 **Signing secret** `whsec_...` 복사 → Render Environment `STRIPE_WEBHOOK_SECRET` → **Save** → **Manual Deploy** (재배포).

---

## 7. GitHub Pages 구매 버튼 연결

`docs/purchase-config.js` 수정:

```js
window.SandboxTimelinePurchaseConfig = {
  vaultBaseUrl: "https://YOUR-SERVICE.onrender.com"
};
```

commit + push → 1~2분 후 사이트 **프리미엄 구매** 버튼이 공개 Checkout으로 연결됩니다.

---

## 8. Windows 앱 연결

사용자 PC 또는 설치 안내에:

```powershell
setx SANDBOXTIMELINE_LICENSE_VAULT_URL "https://YOUR-SERVICE.onrender.com/v1/license/validate"
setx SANDBOXTIMELINE_LICENSE_VAULT_SECRET "Render에 넣은 LICENSE_VAULT_SECRET과 동일"
```

(새 터미널/재로그인 후 앱 실행)

또는 앱 기본 URL을 바꾸려면 `ProductionLicenseConfiguration.DefaultProductionVaultEndpointUrl` 수정 후 새 zip 릴리스.

---

## 무료 플랜 참고

- 15분 미사용 시 **슬립** → 첫 요청이 느릴 수 있음
- SQLite는 **Disk** 마운트 필수 (`DATABASE_PATH=/var/data/licenses.db`)

---

## 체크리스트

- [ ] `/health` OK
- [ ] `/v1/checkout/start` → Stripe 결제 화면
- [ ] 테스트 결제 후 `/success` 에 라이선스 키
- [ ] 앱에 키 입력 → 프리미엄 ON
