# License Vault — Vercel (유료 플랜) 배포

이미 **Vercel 유료**를 쓰고 계시면 Render 없이 여기서 License Vault를 올릴 수 있습니다.

---

## 왜 Postgres가 필요한가

Vercel 서버리스는 **파일(SQLite)이 유지되지 않습니다.**  
Vercel 대시보드에서 **Postgres(Neon)** 스토리지를 연결하면 `POSTGRES_URL`이 자동 주입됩니다.

---

## 1. Vercel 프로젝트 만들기

1. https://vercel.com → **Add New → Project**
2. GitHub `SandboxTimeline` import
3. **Root Directory:** `license-vault` ← 중요
4. Framework: **Other** (또는 자동 감지)

---

## 2. Postgres 연결

프로젝트 → **Storage** → **Create Database** → **Postgres** (Neon)

연결하면 환경 변수 `POSTGRES_URL` 이 자동 추가됩니다.

---

## 3. Environment Variables

| Key | Value |
|-----|--------|
| `STRIPE_SECRET_KEY` | `sk_live_...` 또는 테스트 `sk_test_...` |
| `STRIPE_PRICE_ID` | `price_...` |
| `STRIPE_WEBHOOK_SECRET` | Stripe Webhook `whsec_...` |
| `LICENSE_VAULT_SECRET` | 긴 랜덤 문자열 |
| `PRODUCT_METADATA_ID` | `prod_sandbox_timeline_premium_v1` |
| `BASE_URL` | 배포 후 `https://프로젝트.vercel.app` (슬래시 없음) |

`BASE_URL`을 비우면 `VERCEL_URL`로 자동 설정되지만, **커스텀 도메인** 쓰면 `BASE_URL`에 커스텀 도메인을 넣으세요.

---

## 4. Deploy

Deploy 후 확인:

- `https://YOUR-PROJECT.vercel.app/health`

---

## 5. Stripe Webhook

**Developers → Webhooks → Add endpoint**

```
https://YOUR-PROJECT.vercel.app/v1/webhooks/stripe
```

이벤트:

- `checkout.session.completed`
- `customer.subscription.updated`
- `customer.subscription.deleted`
- `invoice.payment_failed`

`whsec_...` → Vercel Environment → Redeploy

---

## 6. GitHub Pages 구매 버튼

`docs/purchase-config.js`:

```js
window.SandboxTimelinePurchaseConfig = {
  vaultBaseUrl: "https://YOUR-PROJECT.vercel.app"
};
```

push 후 **프리미엄 구매** 버튼이 Vercel Checkout으로 연결됩니다.

---

## 7. Windows 앱

```powershell
setx SANDBOXTIMELINE_LICENSE_VAULT_URL "https://YOUR-PROJECT.vercel.app/v1/license/validate"
setx SANDBOXTIMELINE_LICENSE_VAULT_SECRET "Vercel의 LICENSE_VAULT_SECRET과 동일"
```

---

## github.io와 함께 쓰기

| 주소 | 역할 |
|------|------|
| `zeus223308-art.github.io/SandboxTimeline` | 소개 + zip 다운로드 |
| `YOUR-PROJECT.vercel.app` | 결제 + 라이선스 키 + 앱 검증 API |

**같은 Vercel 계정에서 프로젝트 2개** (Pages용 + license-vault) 로 운영하면 됩니다.

---

## 로컬 개발

Postgres 없이 PC에서만:

```powershell
cd license-vault
npm install
npm run dev
```

→ SQLite `data/licenses.db` 사용 (`POSTGRES_URL` 없을 때)
