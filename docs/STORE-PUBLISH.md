# Sandbox Timeline — Microsoft Store 배포

Store 전용 빌드(`STORE_DISTRIBUTION`)는 **Microsoft Store 구독**으로 프리미엄을 판매합니다. Stripe / license-vault는 사용하지 않습니다.

---

## 1. Partner Center (당신이 할 일)

1. https://partner.microsoft.com → **개인 개발자** 등록 (무료)
2. **Reserve app name** — `Package.appxmanifest`의 `Identity Name`과 **동일**하게 맞출 것
3. **Pricing** → Subscription **$3.99/month**
4. **Add-on / subscription** 생성 → **Store ID** 복사 (예: `PremiumMonthly`)
5. 앱 **ProductId** (`9N...`) 발급 후 `StoreLicenseConfiguration.DefaultStoreListingUri` 또는 환경 변수 `SANDBOXTIMELINE_STORE_LISTING_URI`에 반영
6. **정산** → 한국 은행 계좌 등록
7. **개인정보 처리방침** URL — `docs/PRIVACY.md`를 GitHub Pages에 게시

`Package.appxmanifest`에서 Partner Center 값으로 교체:

| 필드 | Partner Center에서 |
|------|---------------------|
| `Identity Name` | Reserved name |
| `Identity Publisher` | 인증서 주체 (Publisher ID) |
| `Identity Version` | `Major.Minor.Build.0` (예: 1.0.6.0) |

---

## 2. Store ID 설정 (코드)

`Services/Store/StoreLicenseConfiguration.cs`:

```csharp
public const string DefaultPremiumStoreId = "PremiumMonthly"; // ← Partner Center Store ID
public const string DefaultStoreListingUri = "https://apps.microsoft.com/detail/9NXXXXXXXX";
```

또는 빌드/런타임 환경 변수:

- `SANDBOXTIMELINE_STORE_PREMIUM_ID`
- `SANDBOXTIMELINE_STORE_LISTING_URI`

---

## 3. MSIX 빌드

**Visual Studio 2022** (Desktop development + MSIX Packaging):

1. `SandboxTimeline.sln` 열기
2. 구성 **Release | x64**
3. **SandboxTimeline.Package** 우클릭 → **Publish** → **Create App Packages**
4. Partner Center 연동 또는 `.msixupload` 생성

**PowerShell** (Visual Studio MSIX 도구 필요):

```powershell
.\build-store.ps1
```

출력: `dist\Store\`

로컬 테스트: 생성된 `.msix` 더블클릭 설치 → 앱에서 **Microsoft Store에서 구독**

---

## 4. 제출 체크리스트

- [ ] MSIX / msixupload 업로드
- [ ] 스크린샷, 설명 (한/영)
- [ ] 관리자(UAC) 필요 이유 설명 (VSS 스냅샷·복원)
- [ ] Windows 11 Home — 샌드박스 가드 불가 명시
- [ ] 개인정보 처리방침 URL
- [ ] Certification 제출

---

## 5. 개발 vs Store 빌드

| 빌드 | 프리미엄 |
|------|----------|
| **Debug** (`dotnet run`) | `DEBUG-PREMIUM-TEST` 키 (개발용) |
| **Release + StoreDistribution** | Microsoft Store 구독만 |
| **Release zip** (구 방식) | Store 빌드 아님 — Store 배포만 사용 권장 |

---

## 6. GitHub Pages

`docs/index.html` — 다운로드/구매 링크를 **Microsoft Store**로 안내합니다.
