# Sandbox Timeline — 진행 상황 (자동 저장)

마지막 갱신: 2026-06-04 · 버전 **1.0.6** · 커밋 `28083f1` (master)  
**상태: 일시 중단 — 다음은 Partner Center 가입부터**

---

## 결정 사항

| 항목 | 선택 |
|------|------|
| 판매 채널 | **Microsoft Store만** |
| 프리미엄 가격 | **$3.99/월** (Store 구독) |
| Stripe / Wise / license-vault | **사용 안 함** |
| 정산 | Partner Center → **한국 계좌** |

---

## 완료된 작업

- [x] Store 결제 API 연동 (`Services/Store/`)
- [x] Release Store 빌드: Vault/라이선스 키 대신 Store 구독
- [x] UI: 「Microsoft Store에서 구독」 버튼
- [x] MSIX 패키징 프로젝트 (`SandboxTimeline.Package/`)
- [x] `build-store.ps1`, `docs/STORE-PUBLISH.md`, `docs/PRIVACY.md`
- [x] GitHub Pages → Store 안내 (`docs/index.html`)
- [x] git commit + push (master)

---

## 당신이 할 일 (순서)

1. **Partner Center** https://partner.microsoft.com  
   - 개발자 등록 · 앱 이름 예약 (`SandboxTimeline`)  
   - 구독 **$3.99/월** · Store ID (예: `PremiumMonthly`)  
   - ProductId `9N...` · 한국 정산 계좌  

2. **코드 3곳** (ProductId/Store ID 받은 뒤)  
   - `Services/Store/StoreLicenseConfiguration.cs`  
   - `docs/purchase-config.js`  
   - `SandboxTimeline.Package/Package.appxmanifest` (Name, Publisher)  

3. **Visual Studio 2022** → `SandboxTimeline.Package` Publish → `.msixupload`  

4. **제출**: 스크린샷, 개인정보 URL, Certification  

상세: `docs/STORE-PUBLISH.md`

---

## 개발 참고

| 용도 | 명령/경로 |
|------|-----------|
| 일반 Debug | `dotnet run` · 키 `DEBUG-PREMIUM-TEST` |
| Store Release publish | `.\build-store.ps1` (MSIX는 VS Publish) |
| MSIX 프로젝트 | `SandboxTimeline.Package/` |
| 로그 | `C:\ProgramData\SandboxTimeline\startup.log` |

---

## 링크

- Repo: https://github.com/zeus223308-art/SandboxTimeline  
- Pages: https://zeus223308-art.github.io/SandboxTimeline/  
- Store (placeholder): https://apps.microsoft.com/store/detail/sandbox-timeline  

---

## 환경 (참고)

- PC: Windows 11 **Home** → 샌드박스 가드 비활성 정상  
- Wise: 가입·검증 대기 중이었으나 **Store 전환으로 불필요**  
- Stripe: Country US — **Store만 쓰므로 Live 전환 안 함**  

이 파일은 세션 체크포인트입니다. Partner Center ID를 받으면 위 「코드 3곳」을 갱신하세요.
