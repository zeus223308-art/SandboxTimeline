# Sandbox Timeline

Lightweight Windows 11 system recovery tool with VSS snapshots, one-click rollback, and automated Windows Sandbox isolation for untrusted executables.

## Requirements

- Windows 11 (or Windows 10 22H2+)
- .NET 8 SDK
- Administrator privileges (enforced via manifest + `Program.cs` UAC relaunch)
- Volume Shadow Copy Service enabled
- Windows Sandbox (optional, for executable isolation)

## Build

```powershell
cd C:\Users\codib\SandboxTimeline; dotnet build -c Debug; .\bin\Debug\net8.0-windows\win-x64\SandboxTimeline.exe
```

**Important:** The project is **x64-only** (`PlatformTarget=x64`). Running a 32-bit build causes `REGDB_E_CLASSNOTREG` for VSS COM.

`dotnet run`은 `dotnet.exe`로 호스트되어 UAC manifest가 적용되지 않을 수 있습니다. 빌드 후 **SandboxTimeline.exe**를 실행하면 UAC가 자동으로 표시됩니다.

## Tray & global hotkey

- **X 버튼**: 창이 닫히지 않고 시스템 트레이로 최소화됩니다.
- **트레이 더블 클릭 / 열기**: 메인 UI 복원
- **트레이 종료**: 앱 완전 종료
- **Ctrl+Z (전역)**: 백그라운드에서도 메인 창을 화면 중앙에 표시하고 타임 슬라이더에 포커스

Place a 256×256 `icon.ico` in `assets\icon.ico` before building the installer.

## Installer (Inno Setup)

1. Install [Inno Setup 6](https://jrsoftware.org/isinfo.php)
2. Publish the app (command above)
3. Open `installer\SandboxTimeline.iss` in Inno Setup Compiler and compile

Output: `dist\SandboxTimeline-Setup-1.0.0.exe`

## License Vault (Stripe → license key → app)

The desktop app does **not** talk to Stripe directly. Use the Node server in `license-vault/`:

- Stripe Checkout + Webhook
- Success page + optional email with the license key
- `POST /v1/license/validate` for the Windows app

See [license-vault/README.md](license-vault/README.md) for setup (Stripe CLI, `.env`, deploy).

Point the app at your server:

```powershell
$env:SANDBOXTIMELINE_LICENSE_VAULT_URL = "https://api.yourdomain.com/v1/license/validate"
$env:SANDBOXTIMELINE_LICENSE_VAULT_SECRET = "same-as-LICENSE_VAULT_SECRET-on-server"
```

## License webhook (legacy note)

Set your validation endpoint in `LicenseManager.cs` or pass a custom URL when constructing `LicenseManager`. Expected JSON:

```json
{
  "valid": true,
  "premium": true,
  "trial_sandbox": false,
  "expires_at": "2027-01-01T00:00:00Z"
}
```

## Project layout

```
SandboxTimeline/
  SandboxTimeline.csproj
  Program.cs, App.xaml, MainWindow.xaml
  Services/   — VSS, snapshots, sandbox, license
  Models/, Native/
  installer/SandboxTimeline.iss
```

## Security note

This tool modifies registry and user profile files during rollback. Test on a non-production VM first. Full OS imaging is not replaced by VSS alone; rollback targets registry exports and user-profile paths mirrored from the shadow copy.
