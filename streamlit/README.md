# AVEVA Marine — Leaf Export (Streamlit 웹 UI)

올드한 WinForms 대신 **브라우저 기반 모던 UI**. 파이썬 Streamlit 으로 호스팅하고,
실제 추출은 .NET 엔진 **`E3dLeafCli.exe`** 가 담당합니다.

```
[브라우저] ──HTTP──> [streamlit app.py] ──subprocess──> [E3dLeafCli.exe (.NET)]
                                                          │
                                          AM/PDMS 환경 자동감지 + 세션 로그인 + leaf 추출
                                                          │
                                          결과 JSON 파일  <──┘   (PDMS 가 콘솔에 배너를
                                                                  찍어서 stdout 대신 파일로 주고받음)
```

## 필요 조건 (AVEVA Marine PC)

1. **AVEVA Marine OH12.1.SP5** 설치 + **AM 실행·프로젝트 로그인 상태**
   (환경 자동감지가 실행 중인 AM 프로세스에서 projects_dir·USER·MDB 를 읽습니다)
2. **Visual Studio / MSBuild** (E3dLeafCli.exe 빌드용) — 최초 1회
3. **Python 3** (python.org 또는 MS Store) — streamlit 호스팅용
   - ⚠ 회사 정책상 파이썬/모듈 설치가 막혀 있으면 IT 승인이 필요할 수 있습니다.

## 실행 (가장 쉬운 방법)

`streamlit\run-streamlit.cmd` **더블클릭**. 자동으로:
1. `E3dLeafCli.exe` 빌드 → AVEVA bin 폴더로 복사
2. streamlit 미설치 시 설치
3. 브라우저에서 웹앱 열기

## 수동 실행

```powershell
# 1) .NET 엔진 빌드 (AVEVA bin 으로 복사됨)
powershell -ExecutionPolicy Bypass -File ..\E3dLeafCli\build-cli.ps1

# 2) streamlit 설치 (최초 1회)
python -m pip install --user streamlit

# 3) 웹앱 실행
python -m streamlit run app.py
```

## 사용 순서 (웹 화면)

1. 왼쪽 사이드바 **[🔍 AM 환경 자동 감지]** 클릭
   → 실행 중인 AM 에서 **PROJECT / USER / MDB** 후보를 읽어 폼에 자동 입력
   → "감지된 전체 환경변수" 펼치면 AM 이 설정한 모든 변수 확인 가능
2. PROJECT(드롭다운) / USER / PASSWORD / MDB / MODULE / **시작 요소** 확인·수정
3. **[🚀 추출 실행]** → leaf 표 표시 + **TXT 다운로드**

## E3dLeafCli.exe 위치를 못 찾을 때

`app.py` 는 아래 순서로 exe 를 찾습니다:
1. 환경변수 `LEAF_CLI`
2. `app.py` 옆 `E3dLeafCli.exe`
3. 빌드 출력 폴더(`E3dLeafCli\...\bin\...`)
4. `C:\AVEVA\Marine\OH12.1.SP5\E3dLeafCli.exe`

직접 지정하려면:
```powershell
$env:LEAF_CLI = "C:\AVEVA\Marine\OH12.1.SP5\E3dLeafCli.exe"
python -m streamlit run app.py
```

## CLI 단독 사용 (디버그용)

```bat
REM 환경 감지 (결과를 detect.json 으로)
E3dLeafCli.exe detect-env --result detect.json

REM 추출
E3dLeafCli.exe extract --project SN2661 --user 02564 --password "" ^
  --mdb MYMDB --module 78 --start /SITE-XXX --result out.json
```

## USER/MDB 가 자동으로 안 채워질 때

USER·MDB 는 AM 이 환경변수로 노출하지 않을 수도 있습니다.
이 경우 사이드바 **"감지된 전체 환경변수"** 표를 열어 값을 확인하고
직접 입력하세요. (자동감지는 `PDMSUSER/USER/USERNAME`, `*MDB*` 등 흔한 키를 추적합니다.)
어떤 키에 들어있는지 알려주시면 자동 채움 규칙을 추가하겠습니다.
