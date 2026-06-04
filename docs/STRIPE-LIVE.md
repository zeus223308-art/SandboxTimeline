# Stripe Live(실결제) 전환 체크리스트

테스트가 끝났을 때만 진행하세요.

## Stripe Dashboard (Live mode)

1. 상단 **Test mode 끄기** (Live)
2. **Product / Price** Live용 확인 (`price_...`)
3. **Developers → API keys** → `sk_live_...` 복사
4. **Webhooks → Add endpoint** (Live)
   - URL: `https://license-vault-gules.vercel.app/v1/webhooks/stripe`
   - 이벤트 4개: `checkout.session.completed`, `customer.subscription.updated`, `customer.subscription.deleted`, `invoice.payment_failed`
   - `whsec_...` (Live) 복사

## Vercel (license-vault)

Environment Variables **Production** 업데이트:

| 변수 | 값 |
|------|-----|
| `STRIPE_SECRET_KEY` | `sk_live_...` |
| `STRIPE_PRICE_ID` | Live `price_...` |
| `STRIPE_WEBHOOK_SECRET` | Live `whsec_...` |
| `BASE_URL` | `https://license-vault-gules.vercel.app` |

→ **Redeploy**

## 확인

- Live 카드로 소액 테스트 결제
- success 페이지에 라이선스 키
- Stripe Event deliveries **200**
