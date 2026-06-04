# Sandbox Timeline 설치 가이드 (Windows)

## 1. 다운로드

- **최신 zip:** https://github.com/zeus223308-art/SandboxTimeline/releases/latest/download/SandboxTimeline-win-x64.zip
- 또는 https://zeus223308-art.github.io/SandboxTimeline/

## 2. 압축 해제 (중요)

**OneDrive / 바탕화면 동기화 폴더는 사용하지 마세요.**

권장 경로:

```
C:\Apps\SandboxTimeline\
```

1. zip 전체를 위 폴더에 풀기
2. `SandboxTimeline.exe` 가 있는 폴더에서 실행

## 3. 첫 실행

1. `SandboxTimeline.exe` **우클릭 → 관리자 권한으로 실행**
2. SmartScreen 파란 화면 → **추가 정보 → 실행**
3. UAC(관리자) → **예**
4. 웰컴 가이드 확인

## 4. 프리미엄 (구매한 경우)

1. 웹에서 **프리미엄 구매** 또는 https://license-vault-gules.vercel.app/v1/checkout/start
2. 결제 후 표시되는 **라이선스 키** 복사
3. 앱 → **라이선스 키 입력** → 확인

앱 v1.0.5+ 는 별도 환경 변수 없이 Vault에 자동 연결됩니다.

## 5. Windows 11 Home

- **타임라인·스냅샷·되돌리기:** 사용 가능
- **샌드박스 가드:** Home 에디션에서는 버튼 비활성 (Pro 이상 필요)

## 6. 문제 해결

| 증상 | 조치 |
|------|------|
| 실행 후 바로 꺼짐 | OneDrive 밖 폴더에 설치, v1.0.4+ zip 사용 |
| 로그 확인 | `C:\ProgramData\SandboxTimeline\startup.log` |
| 프리미엄 실패 | 인터넷 연결 후 키 재입력 |
