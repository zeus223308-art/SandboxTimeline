# Sandbox Timeline — License Vault

Stripe Checkout → Webhook → **라이선스 키 발급**(성공 페이지 + 이메일) → Windows 앱 **Vault 검증 API**.

## 사용자가 키를 받는 곳

1. **결제 직후** — `https://YOUR-VAULT-HOST/success?session_id=...` (Checkout success URL)
2. **이메일** — SMTP 설정 시 구매자 메일로 동일 키 발송
3. **앱** — 받은 키를 Sandbox Timeline **라이선스 입력란**에 붙여넣기

## API (Windows 앱과 동일)

| Method | Path | 설명 |
|--------|------|------|
| POST | `/v1/license/validate` | 앱이 호출 (본문: `license_key_hash`, `product_metadata_id`) |
| GET | `/v1/checkout/start` | Stripe Checkout으로 리다이렉트 (웹사이트 구매 버튼) |
| POST | `/v1/webhooks/stripe` | Stripe Webhook 수신 |

검증 성공 응답 예:

```json
{
  "valid": true,
  "premium": true,
  "product_metadata_id": "prod_sandbox_timeline_premium_v1",
  "expires_at": "2027-06-04T00:00:00.000Z",
  "subscription_id": "sub_...",
  "customer_id": "cus_..."
}
```

## 빠른 시작 (로컬)

### 1. Stripe Dashboard

1. **Product** + **Recurring Price** 생성 (예: 월 구독)
2. **Developers → API keys** → `sk_test_...` 복사
3. **Price ID** (`price_...`) 복사

### 2. 환경 변수

```powershell
cd C:\Users\codib\SandboxTimeline\license-vault
copy .env.example .env
# .env 편집: STRIPE_SECRET_KEY, STRIPE_PRICE_ID, LICENSE_VAULT_SECRET, BASE_URL
npm install
```

### 3. Stripe Webhook (로컬)

터미널 1:

```powershell
npm run dev
```

터미널 2:

```powershell
stripe listen --forward-to localhost:8787/v1/webhooks/stripe
```

출력된 `whsec_...` 를 `.env`의 `STRIPE_WEBHOOK_SECRET`에 넣고 서버 재시작.

### 4. 테스트 결제

브라우저: http://localhost:8787/v1/checkout/start  

카드: `4242 4242 4242 4242` (Stripe 테스트)

결제 후 **성공 페이지에 라이선스 키**가 표시됩니다.

### 5. Windows 앱 연결

앱 실행 전 (또는 시스템 환경 변수):

```powershell
$env:SANDBOXTIMELINE_LICENSE_VAULT_URL = "http://localhost:8787/v1/license/validate"
$env:SANDBOXTIMELINE_LICENSE_VAULT_SECRET = "your-LICENSE_VAULT_SECRET-from-env"
```

배포 시에는 `ProductionLicenseConfiguration.DefaultProductionVaultEndpointUrl` 을 실제 HTTPS URL로 바꾸거나, 위 환경 변수로 덮어씁니다.

## 프로덕션 배포 (공개 URL)

**Vercel 유료 플랜** → **[DEPLOY-VERCEL.md](./DEPLOY-VERCEL.md)** (Postgres 스토리지 연결, `vercel.json` 포함)

**Render 무료/저렴** → **[DEPLOY-RENDER.md](./DEPLOY-RENDER.md)**

1. **HTTPS** 공개 URL (Render, Fly.io, Railway, Azure, VPS 등)
2. `BASE_URL=https://api.yourdomain.com`
3. Stripe **Live** 키 + Live Webhook → `https://api.yourdomain.com/v1/webhooks/stripe`
4. Checkout **success URL**은 자동으로 `BASE_URL/success?session_id=...`
5. GitHub Pages `docs/purchase-config.js` 에 Vault URL 설정 (구매 버튼)

## 이메일 (선택)

`.env`에 SMTP 설정 시 결제 완료 Webhook에서 키 메일 발송.

- Gmail: 앱 비밀번호 + `SMTP_HOST=smtp.gmail.com`, `SMTP_PORT=587`
- SendGrid / Resend SMTP도 동일 방식

SMTP를 비우면 **성공 페이지만**으로 키 전달.

## 보안

- `LICENSE_VAULT_SECRET` — 앱과 서버에 동일하게 설정 (검증 API 보호)
- Stripe **Webhook secret** 필수 (`whsec_...`)
- DB 파일 `data/licenses.db` 에 키 원문 저장 → 서버 디스크 접근 권한 제한

## 데이터

SQLite `data/licenses.db` — 키 해시, Stripe subscription ID, 상태(`active` / `revoked` / `past_due`).

환불/구독 취소 시 `customer.subscription.deleted` → 앱에서 프리미엄 해제.
