# E3dLeafWeb — AVEVA Leaf Export 웹 버전 (.NET 4.8)

브라우저 UI에서 **두 모드를 선택**해 모델의 최하위(leaf) 요소 이름+Ref를 추출합니다.

- **① 프로젝트 정보 입력 (Standalone)** — 프로젝트/USER/PASSWORD/MDB 입력 → 직접 열어 추출
- **② 현재 열린 모델 (Addin)** — 실행 중인 E3D/Marine 세션의 현재요소(CE) 하위 추출 (로그인 불필요)

> ⚠️ 웹이라도 **백엔드 .NET 은 AVEVA 설치 PC에서 실행**됩니다. 브라우저는 같은 PC(기본) 또는 사내망 다른 PC(LAN 설정 시)에서 접속합니다.

---

## 구성

```
E3dLeafWeb/
├─ E3dLeafWeb.sln
├─ E3dLeafCore/              ← 공용: 웹서버(HttpListener)+UI+추출로직+AVEVA 연결
│   ├─ webui.html            ← 화면(임베드)
│   ├─ LeafWebServer.cs, WebUi.cs, Models.cs, Interfaces.cs
│   ├─ LeafCollector.cs, TextBuilder.cs, Dispatchers.cs
│   ├─ StandaloneModelProvider.cs   ← ①모드
│   └─ AddinModelProvider.cs        ← ②모드
├─ E3dLeafWeb.Standalone/    ← ①모드 호스트 (콘솔 exe)
│   ├─ Program.cs, App.config
├─ E3dLeafWeb.Addin/         ← ②모드 호스트 (E3D Addin dll)
│   └─ LeafExportAddin.cs
├─ web-build-and-run.ps1     ← ①모드 원클릭 빌드+실행
└─ README_웹버전.md
```

---

## A. ①모드 (Standalone 웹) — 가장 간단

E3D 설치 PC에서:
```powershell
cd <복사경로>\E3dLeafWeb
.\web-build-and-run.ps1      # AVEVA 경로/비트수 자동탐지 → 빌드 → 복사 → 실행
```
→ 콘솔에 `http://127.0.0.1:8731/` 출력. 브라우저로 접속 → ① 탭에서 PROJECT/USER/PASSWORD/MDB 입력 → **추출** → 표 + **TXT 다운로드**.

설정: `E3dLeafWeb.Standalone.exe.config`
- `PORT` (기본 8731), `BIND` = `localhost`(이 PC만) / `lan`(다른 PC도)
- `MODULE_NUMBER`, `projects_dir`, `AVEVA_DESIGN_USER`, `PMLLIB`, `프로젝트코드000` (AVEVA 환경변수 — `marine-probe.ps1` 출력 참고)

### LAN 개방 시 (BIND=lan)
`http://+:port/` 바인딩은 관리자 권한 또는 URL ACL 등록이 필요합니다. 관리자 PowerShell에서 한 번:
```powershell
netsh http add urlacl url=http://+:8731/ user=Everyone
New-NetFirewallRule -DisplayName "E3dLeafWeb 8731" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8731
```
다른 PC 브라우저에서 `http://<AVEVA PC IP>:8731/` 접속.

---

## B. ②모드 (Addin 웹) — E3D 켜둔 채 현재 모델에서

1. 빌드:
   ```powershell
   .\web-build-and-run.ps1 -NoRun     # 또는 VS에서 Release/x86 빌드
   ```
2. `E3dLeafWeb.Addin.dll` + `E3dLeafCore.dll` 을 **CAF_ADDINS_PATH** 폴더(예: `...\Data\Addins`)에 복사
3. 해당 모듈의 `<module>Addins.xml`(예: `DesignAddins.xml`)에 Addin 등록 (매뉴얼 6장 참고)
4. E3D/Marine 시작 → Addin 로드 시 웹서버 기동 → 브라우저에서 `http://127.0.0.1:8731/` → ② 탭 → **추출**
   - 포트/LAN 은 환경변수 `LEAFWEB_PORT`, `LEAFWEB_BIND=lan` 로 조정

> ②모드는 AVEVA DB 호출을 E3D UI 스레드로 마샬링합니다(WinForms Control.Invoke). 단일 세션 기준이라 동시 다발 요청은 직렬 처리됩니다.

---

## 빌드 전 설정

- **AVEVA 경로**: `E3dLeafCore.csproj` / `E3dLeafWeb.Addin.csproj` 의 `AvevaBinDir` (또는 `/p:AvevaBinDir="경로\"`). `web-build-and-run.ps1` 은 자동 탐지.
- **비트수**: 호스트 csproj `PlatformTarget` 를 E3D 비트수와 일치 (매뉴얼 x86; 64bit E3D면 x64). 스크립트는 자동 감지.
- **네임스페이스**: `Standalone`/`PdmsMessage`/`CurrentElement`/`IAddin` 의 네임스페이스가 설치 SDK 와 다르면 VS에서 `Ctrl+.` 로 using 추가.

---

## 검증 상태

- ✅ 전체(코어+Standalone+Addin) **컴파일 통과** (AVEVA/CAF 스텁 대비, 타입·시그니처 일관).
- ✅ 동일 UI/JSON 계약의 **웹 흐름**은 net10 데모(`Projects\E3dLeafExport\WebDemo`)로 실제 동작 확인(모드 전환·추출·표·TXT).
- ⏳ 실제 AVEVA 연결 빌드/실행은 **E3D/Marine 설치 PC에서** (이 PC엔 미설치).

---

## 동작 요약

| | ①Standalone | ②Addin |
|---|---|---|
| 호스트 | 콘솔 exe (AVEVA PC) | E3D Addin dll (E3D 안) |
| 로그인 | 웹에서 입력 | 불필요(현재 세션) |
| 시작점 | 전체 SITE / 지정요소 | CE / 지정요소 |
| 스레드 | 전용 STA 워커 | E3D UI 스레드 마샬 |
| 공통 | 같은 화면·같은 leaf 재귀·같은 TXT 출력 | |
