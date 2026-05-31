# AVEVA E3D / Marine — .NET Customisation

AVEVA™ E3D Design / Marine 의 .NET(C#) 커스터마이징 프로그램 모음.
모델 데이터베이스에서 **제일 하위 단위(leaf) 요소의 이름과 참조값(Ref)** 을 추출하는 도구로 시작합니다.

> ⚠️ **빌드·실행에는 AVEVA E3D/Marine 설치가 필요합니다.** 참조하는 `Aveva.Core.Database.dll`,
> `Aveva.E3D.Standalone.dll` 등은 제품에 포함되는 라이선스 어셈블리로, 이 저장소에는 포함되지 않습니다.
> 각 프로젝트의 `AvevaBinDir` 를 설치 폴더로 지정해 빌드하세요.

## 구성

| 폴더 | 내용 |
|------|------|
| [`E3dLeafExport/`](E3dLeafExport) | **1번 — 콘솔판.** Standalone 콘솔 EXE 로 leaf 이름+Ref 를 TXT 로 추출. `WebDemo/` 는 E3D 없이 로직을 확인하는 net10 데모. |
| [`E3dLeafWeb/`](E3dLeafWeb) | **2번 — 웹판(.NET 4.8).** 브라우저 UI 에서 두 모드 선택: ① 프로젝트 정보 입력(Standalone) / ② 현재 열린 모델(Addin). HttpListener 경량 서버. |

## 빠른 시작

- **콘솔판 데모 (AVEVA 불필요):**
  `cd E3dLeafExport/E3dLeafExport.Demo && dotnet run`
- **실제 빌드 (E3D 설치 PC):**
  - 콘솔: `E3dLeafExport/build-and-run.ps1`
  - 웹: `E3dLeafWeb/web-build-and-run.ps1`
- 환경 정보 수집: `E3dLeafExport/marine-probe.ps1` (설치경로·비트수·환경변수·프로젝트 목록)

## 기술

- .NET Framework **4.8** (AVEVA 어셈블리가 .NET Framework), 데모는 .NET 10
- 핵심 API: `DbElement.Members()` 재귀(멤버 0개 = leaf), `GetString(DbAttributeInstance.NAME)`,
  `ToString()`(Ref), `GetElementType().Name`(Type)
- 세션: `Standalone.Start/Open/Finish`, Addin 은 `CurrentElement.Element` + CAF `IAddin`

자세한 내용은 각 폴더의 `README_*.md` 참고.

---
참고: AVEVA 교육 매뉴얼(TG1031-CC3-C01) 기반. 매뉴얼 PDF 등 AVEVA 저작권 자료는 포함하지 않습니다.
