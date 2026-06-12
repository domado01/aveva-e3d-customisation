# -*- coding: utf-8 -*-
"""
AVEVA Marine - Leaf Export (Streamlit 웹 UI)

E3dLeafCli.exe (.NET) 를 호출해서:
  1) 실행 중인 AM/PDMS 환경 자동 감지 (PROJECT/USER/MDB 후보 + 전체 env)
  2) 지정 시작요소 하위의 leaf(최하위) 요소 Name/Ref 추출 → 표 + TXT 다운로드
PDMS 가 stdout 에 배너를 찍으므로, exe 결과는 JSON 파일로 주고받는다.

실행:  streamlit run app.py      (또는 run-streamlit.cmd 더블클릭)
"""
import json
import os
import subprocess
import tempfile
import time
import uuid
from datetime import datetime

import streamlit as st

st.set_page_config(page_title="AVEVA Marine Leaf Export", page_icon="🛠️", layout="wide")

# ===== 공용 로그/진단 (모니터링) =====
PUB_DIR = r"C:\Users\Public\Documents"
LOG_FILE = os.path.join(PUB_DIR, "leaf_log.txt")


def applog(msg):
    """웹/실행 동작을 공용 로그에 남긴다(모니터링 패널이 읽음). 실패해도 무시."""
    try:
        with open(LOG_FILE, "a", encoding="utf-8") as f:
            f.write("%s [web] %s\n" % (datetime.now().strftime("%Y-%m-%d %H:%M:%S"), msg))
    except OSError:
        pass


def finfo(path):
    """파일 존재/수정시각/크기 요약."""
    try:
        if not os.path.isfile(path):
            return {"exists": False}
        stt = os.stat(path)
        return {"exists": True, "mtime": datetime.fromtimestamp(stt.st_mtime).strftime("%Y-%m-%d %H:%M:%S"),
                "size": stt.st_size}
    except OSError as e:
        return {"exists": False, "error": str(e)}


def read_text(path, limit=8000):
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            return f.read()[-limit:]
    except OSError as e:
        return "(읽기 실패: %s)" % e


def _rerun():
    try:
        st.rerun()
    except Exception:
        try:
            st.experimental_rerun()
        except Exception:
            pass


def find_cli():
    """E3dLeafCli.exe 위치를 찾는다 (환경변수 > 빌드가 남긴 경로파일 > 후보경로 순)."""
    env = os.environ.get("LEAF_CLI")
    if env and os.path.isfile(env):
        return env
    here = os.path.dirname(os.path.abspath(__file__))
    # build-cli.ps1 이 AVEVA bin 의 exe 경로를 여기에 적어둔다
    ptr = os.path.join(here, "leaf-cli-path.txt")
    if os.path.isfile(ptr):
        try:
            p = open(ptr, "r", encoding="utf-8").read().strip()
            if p and os.path.isfile(p):
                return p
        except OSError:
            pass
    candidates = [
        os.path.join(here, "E3dLeafCli.exe"),
        os.path.join(here, "..", "E3dLeafCli", "E3dLeafCli", "bin", "x86", "Release", "net48", "E3dLeafCli.exe"),
        os.path.join(here, "..", "E3dLeafCli", "E3dLeafCli", "bin", "x86", "Debug", "net48", "E3dLeafCli.exe"),
        r"C:\AVEVA\Marine\OH12.1.SP5\E3dLeafCli.exe",
    ]
    for c in candidates:
        if os.path.isfile(c):
            return os.path.abspath(c)
    return ""


def run_cli(args):
    """exe 를 호출하고 --result JSON 파일을 읽어 dict 반환."""
    exe = find_cli()
    if not exe:
        return {"ok": False, "error": "E3dLeafCli.exe 를 찾을 수 없습니다. LEAF_CLI 환경변수로 경로를 지정하거나 빌드 후 app.py 옆에 두세요."}
    tmp = tempfile.NamedTemporaryFile(suffix=".json", delete=False)
    tmp.close()
    cmd = [exe] + list(args)
    if args:
        if args[0] in ("detect-env", "extract"):
            p = st.session_state.get("am_env_pid")
            if p:
                cmd += ["--pid", str(p)]
        elif args[0] in ("am-exec", "am-windows"):
            p = st.session_state.get("am_win_pid")
            if p:
                cmd += ["--pid", str(p)]
    cmd += ["--result", tmp.name]
    mode = args[0] if args else "?"
    try:
        proc = subprocess.run(cmd, capture_output=True, timeout=600,
                              cwd=os.path.dirname(exe))  # exe 옆 attlib.dat 등 사용
        out = (proc.stdout or b"").decode("utf-8", "replace")
        err = (proc.stderr or b"").decode("utf-8", "replace")
        applog("cli %s rc=%s" % (mode, proc.returncode))
        try:
            with open(tmp.name, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception as e:
            applog("cli %s 결과파싱실패 rc=%s: %s" % (mode, proc.returncode, e))
            return {"ok": False,
                    "error": "결과를 읽지 못함 (exit=%s): %s" % (proc.returncode, e),
                    "stdout": out[-2000:], "stderr": err[-2000:], "exit": proc.returncode}
    except subprocess.TimeoutExpired:
        applog("cli %s 시간초과" % mode)
        return {"ok": False, "error": "시간 초과(10분). 세션이 응답하지 않습니다."}
    except Exception as e:
        applog("cli %s 실행실패: %s" % (mode, e))
        return {"ok": False, "error": "실행 실패: %s" % e}
    finally:
        try:
            os.unlink(tmp.name)
        except OSError:
            pass


# ===== 애드인 파일 브리지 (AM 내부 API) =====
ADDIN_DIR = r"C:\Users\Public\Documents"
ADDIN_REQ = os.path.join(ADDIN_DIR, "leaf_req.txt")
ADDIN_RESP = os.path.join(ADDIN_DIR, "leaf_resp.json")
ADDIN_STATUS = os.path.join(ADDIN_DIR, "leaf_addin_status.txt")


def addin_available():
    return os.path.isfile(ADDIN_STATUS)


def addin_call(cmd, timeout=60, **kwargs):
    """요청 파일을 쓰고 응답 JSON 을 폴링해서 반환. 애드인이 AM UI 스레드에서 처리."""
    rid = uuid.uuid4().hex
    lines = ["id=%s" % rid, "cmd=%s" % cmd]
    for k, v in kwargs.items():
        lines.append("%s=%s" % (k, v))
    try:
        os.remove(ADDIN_RESP)
    except OSError:
        pass
    try:
        with open(ADDIN_REQ, "w", encoding="utf-8") as f:
            f.write("\n".join(lines))
    except OSError as ex:
        return {"ok": False, "error": "요청 파일을 쓰지 못함: %s" % ex}
    applog("addin %s 요청(id=%s)" % (cmd, rid[:8]))
    deadline = time.time() + timeout
    while time.time() < deadline:
        if os.path.isfile(ADDIN_RESP):
            try:
                data = json.load(open(ADDIN_RESP, "r", encoding="utf-8"))
            except Exception:
                time.sleep(0.2)
                continue
            if data.get("id") == rid or "id" not in data:
                return data
        time.sleep(0.25)
    return {"ok": False, "error": "애드인 응답 시간초과(%ds). AM 에 E3dLeafAddin 이 로드됐는지 확인하세요." % timeout}


def show_result(res):
    if res.get("ok"):
        rows = res.get("rows", [])
        st.success("✅ 부재(leaf) %d 개 추출 완료" % res.get("count", len(rows)))
        if rows:
            st.dataframe(rows, use_container_width=True, height=480)
        fpath = res.get("file", "")
        if fpath and os.path.isfile(fpath):
            with open(fpath, "rb") as f:
                st.download_button("⬇️ 결과 TXT 다운로드", f.read(),
                                   file_name=os.path.basename(fpath), mime="text/plain",
                                   use_container_width=True)
        if fpath:
            st.caption("저장 경로: %s" % fpath)
    else:
        st.error("추출 실패: %s" % res.get("error", "알 수 없는 오류"))


# ---------- UI ----------
st.title("🛠️ AVEVA Marine — Leaf Export")
st.caption("최하위(leaf) 모델 요소의 Name / Ref 를 추출합니다. AM/PDMS 가 실행·로그인된 상태에서 사용하세요.")

ss = st.session_state
ss.setdefault("projects", [])
ss.setdefault("detected", {})
ss.setdefault("am_items", [])
ss.setdefault("am_windows", [])
ss.setdefault("am_allprojects", [])
ss.setdefault("am_env_pid", None)
ss.setdefault("am_win_pid", None)
ss.setdefault("sel_win_key", None)
ss.setdefault("cmd_class", "WindowsForms10.Window.8.app.0.34f5582_r33_ad1")
ss.setdefault("am_children", [])

# ===== 애드인(권장: AM 내부 API) =====
if addin_available():
    st.success("🔌 E3dLeafAddin 연결됨 — AM 내부 API 사용(키 입력/창 제어 불필요, 가장 안정적).")
else:
    st.warning("🔌 E3dLeafAddin 미연결 — AM 에 애드인을 등록하면(동봉 ADDIN_README) 세션/추출/ADD 가 "
               "키 입력 없이 정확히 동작합니다. 등록 전에는 아래 ②③ standalone 방식으로 사용하세요.")

with st.expander("🔌 애드인 기능 (세션 · 추출 · ADD)", expanded=addin_available()):
    aa1, aa2, aa3 = st.columns(3)
    if aa1.button("세션 불러오기", use_container_width=True, key="ad_sess"):
        d = addin_call("session", timeout=20)
        if d.get("ok"):
            pj = d.get("project") or ""
            if pj:
                if pj not in ss.get("projects", []):
                    ss["projects"] = [pj] + [c for c in ss.get("projects", []) if c != pj]
                ss["f_project"] = pj
            if d.get("user"):
                ss["f_user"] = d["user"]
            if d.get("mdb"):
                ss["f_mdb"] = d["mdb"]
            if d.get("ce"):
                ss["f_ce"] = d["ce"]
            st.success("세션 → PROJECT=%s · USER=%s · MDB=%s · CE=%s"
                       % (d.get("project") or "-", d.get("user") or "-", d.get("mdb") or "-", d.get("ce") or "-"))
        else:
            st.error(d.get("error"))
    if aa2.button("현재요소 하위 추출", use_container_width=True, key="ad_extract"):
        with st.spinner("애드인에서 추출 중..."):
            d = addin_call("extract", start="CE", timeout=180)
        show_result(d)
    if aa3.button("선택요소 3D뷰 ADD", use_container_width=True, key="ad_add"):
        d = addin_call("add", arg="CE", timeout=20)
        if d.get("ok"):
            st.success("✅ ADD 전송: %s — AM 3D 뷰를 확인하세요." % d.get("sent", "ADD CE"))
        else:
            st.error(d.get("error"))


def detect_am():
    """실행 중(마지막 활성) AM 환경을 읽어 PROJECT/USER/MDB 를 폼에 자동 채운다."""
    with st.spinner("실행 중인 AM/PDMS 환경을 읽는 중..."):
        d = run_cli(["detect-env"])
    if d.get("ok"):
        ss["detected"] = d
        ss["projects"] = d.get("projects", [])
        if d.get("project"):
            ss["f_project"] = d["project"]
        elif d.get("projects"):
            ss["f_project"] = d["projects"][0]
        if d.get("user"):
            ss["f_user"] = d["user"]
        if d.get("mdb"):
            ss["f_mdb"] = d["mdb"]
        st.success("감지 완료: %s (PROJECT/USER/MDB 자동 입력)" % (d.get("proc") or "AVEVA process"))
    else:
        st.error(d.get("error", "감지 실패. AM 이 실행 중인지, 권한(같은 사용자/관리자)인지 확인하세요."))
    return d


SESSION_FILE = r"C:\Users\Public\Documents\am_session.txt"


def load_am_session():
    """AM 명령창에서 am-session.pmlmac 가 만든 파일을 읽어 ②③ 를 채운다."""
    if not os.path.isfile(SESSION_FILE):
        st.warning("am_session.txt 가 없습니다. AM 명령창에서 am-session.pmlmac 를 먼저 실행하세요. "
                   "( $m /<압축푼경로>/streamlit/am-session.pmlmac )")
        return
    kv = {}
    try:
        for line in open(SESSION_FILE, "r", encoding="utf-8", errors="replace"):
            if "=" in line:
                k, v = line.split("=", 1)
                kv[k.strip().upper()] = v.strip()
    except OSError as e:
        st.error("읽기 실패: %s" % e)
        return
    proj = kv.get("PROJECT", "")
    if proj:
        if proj not in ss.get("projects", []):
            ss["projects"] = [proj] + [c for c in ss.get("projects", []) if c != proj]
        ss["f_project"] = proj
    if kv.get("USER"):
        ss["f_user"] = kv["USER"]
    if kv.get("MDB"):
        ss["f_mdb"] = kv["MDB"]
    if kv.get("CE"):
        ss["f_ce"] = kv["CE"]
    st.success("AM 세션 불러옴 → PROJECT=%s · USER=%s · MDB=%s · 현재요소=%s"
               % (kv.get("PROJECT") or "-", kv.get("USER") or "-", kv.get("MDB") or "-", kv.get("CE") or "-"))
    blanks = [k for k in ("PROJECT", "USER", "MDB") if not kv.get(k)]
    if blanks:
        st.caption("빈 값(%s)은 AM PML 에서 못 읽은 것입니다. ②에서 직접 입력하거나, "
                   "am-session 실행 시 화면에 출력된 줄을 알려주시면 PML 을 맞추겠습니다." % ", ".join(blanks))


with st.sidebar:
    st.header("① AM 환경 감지")
    st.write("실행 중인 AM/PDMS 에서 프로젝트·USER·MDB 를 읽어와 자동으로 채웁니다.")
    if st.button("🔍 AM 환경 자동 감지", use_container_width=True):
        detect_am()

    det = ss.get("detected", {})
    if det:
        st.metric("projects_dir", det.get("projectsDir", "") or "-")
        st.write("**프로젝트 후보:**", ", ".join(det.get("projects", [])) or "-")
        with st.expander("감지된 전체 환경변수 (USER/MDB 확인용)"):
            env = det.get("env", {})
            if env:
                st.dataframe(
                    [{"name": k, "value": v} for k, v in sorted(env.items())],
                    use_container_width=True, height=300)
            else:
                st.write("환경변수를 읽지 못했습니다.")

st.subheader("🖥️ 실행 중인 AM / 프로젝트")
st.caption("감지된 AM 창을 고르면 그 창의 프로젝트로 ②③ 가 동작합니다. "
           "창 제목/명령줄에서 프로젝트를 못 찾으면 ② PROJECT 에서 직접 선택하세요.")
if st.button("🔄 새로고침 (AM·프로젝트 감지)", use_container_width=True):
    with st.spinner("실행 중인 AM 검색 중..."):
        r = run_cli(["list-am"])
    items = r.get("items", []) if r.get("ok") else []
    windows = r.get("windows", []) if r.get("ok") else []
    ss["am_items"] = items
    ss["am_windows"] = windows
    ss["am_allprojects"] = sorted({c for a in items for c in a.get("projects", []) if c})
    if items:
        env_item = sorted(items, key=lambda a: -len(a.get("projects", [])))[0]
        ss["am_env_pid"] = env_item["pid"]
        ss["env_user"] = env_item.get("user", "")
        ss["env_mdb"] = env_item.get("mdb", "")
        ss["detected"] = {"projectsDir": env_item.get("projectsDir", ""),
                          "projects": ss["am_allprojects"], "env": {}, "proc": env_item.get("name", "")}
    else:
        ss["am_env_pid"] = None
    ss["sel_win_key"] = None  # 자동채움 재적용
    if not windows and not items:
        st.warning("실행 중인 AM 을 찾지 못했습니다. AM 로그인 / 같은 사용자(또는 관리자) 실행을 확인하세요. %s"
                   % (r.get("error", "") or ""))

windows = ss.get("am_windows", [])
allp = ss.get("am_allprojects", [])
if windows:
    wlabels = []
    for w in windows:
        t = w.get("title") or ("pid " + str(w.get("pid")))
        if len(t) > 50:
            t = t[:50] + "…"
        wlabels.append("%s  [프로젝트: %s]" % (t, w.get("project") or "미상"))
    widx = st.selectbox("감지된 AM 창 (작업 대상)", list(range(len(windows))),
                        format_func=lambda i: wlabels[i], key="am_win_idx")
    selwin = windows[widx]
    ss["am_win_pid"] = selwin.get("pid")
    winproj = selwin.get("project") or ""
    # 선택 창이 바뀌면 ② 자동 채움 (선택창 프로젝트 우선)
    key = "%s|%s" % (selwin.get("pid"), selwin.get("title"))
    if ss.get("sel_win_key") != key:
        ss["sel_win_key"] = key
        ordered = ([winproj] if winproj else []) + [c for c in allp if c != winproj]
        ss["projects"] = ordered if ordered else allp
        if winproj:
            ss["f_project"] = winproj
        if ss.get("env_user"):
            ss["f_user"] = ss["env_user"]
        if ss.get("env_mdb"):
            ss["f_mdb"] = ss["env_mdb"]
    if winproj:
        st.success("선택한 AM 창 → 프로젝트 **%s** (pid %s). ②③ 가 이 프로젝트/창으로 동작합니다."
                   % (winproj, selwin.get("pid")))
    else:
        st.warning("이 AM 창의 제목/명령줄에서 프로젝트 코드를 못 찾았습니다. 아래 ② PROJECT 에서 직접 선택하세요. "
                   "(후보: %s)" % (", ".join(allp) or "-"))
    with st.expander("감지 상세 (선택한 창의 제목·명령줄)"):
        st.write("pid: %s" % selwin.get("pid"))
        st.write("제목: %s" % (selwin.get("title") or "-"))
        if selwin.get("cmdline"):
            st.code(selwin["cmdline"], language="text")
        st.caption("전체 프로젝트 후보(참고): %s" % (", ".join(allp) or "-"))
else:
    ss["am_win_pid"] = None
    st.info("[🔄 새로고침] 을 눌러 실행 중인 AM 을 감지하세요. (AM 창이 최소화면 복원하세요)")

st.divider()
st.subheader("② 추출 설정")
bcol1, bcol2 = st.columns(2)
if bcol1.button("🔄 마지막 AM 정보 자동입력", use_container_width=True,
                help="실행 중 AM 의 프로세스 환경에서 PROJECT/USER/MDB 추정(부정확할 수 있음)."):
    detect_am()
if bcol2.button("🔗 AM 세션 불러오기 (PML·정확)", use_container_width=True,
                help="AM 명령창에서 am-session.pmlmac 실행 후 누르면 PROJECT/USER/MDB/현재요소를 정확히 채웁니다."):
    load_am_session()
c1, c2, c3 = st.columns(3)
projects = ss.get("projects", [])
if projects:
    cur = ss.get("f_project", projects[0])
    idx = projects.index(cur) if cur in projects else 0
    project = c1.selectbox("PROJECT", projects, index=idx, key="sel_project")
else:
    project = c1.text_input("PROJECT", key="f_project")
user = c2.text_input("USER", key="f_user")
password = c3.text_input("PASSWORD (없으면 비움)", type="password", key="f_password")

c4, c5, c6 = st.columns(3)
mdb = c4.text_input("MDB", key="f_mdb")
module = c5.text_input("MODULE_NUMBER", value=ss.get("f_module", "78"), key="f_module")
start = c6.text_input("시작 요소 (빈칸/전체 = 모델 전체)", value=ss.get("f_start", "전체"), key="f_start",
                      help="특정 요소만 뽑으려면 /SITE-XXX 또는 =123/456 입력. 비워두거나 '전체'면 모델 전체를 대상으로 합니다.")

proj_val = project if projects else ss.get("f_project", "")

def run_extract(start_value, spinner):
    if not (proj_val and user and mdb):
        st.error("PROJECT / USER / MDB 를 먼저 채우세요. (사이드바 [AM 환경 자동 감지])")
        return
    with st.spinner(spinner):
        res = run_cli([
            "extract", "--project", proj_val, "--user", user, "--password", password,
            "--mdb", mdb, "--module", module or "78", "--start", start_value,
        ])
    if res.get("ok"):
        rows = res.get("rows", [])
        st.success("✅ 부재(leaf) %d 개 추출 완료" % res.get("count", len(rows)))
        if rows:
            st.dataframe(rows, use_container_width=True, height=480)
        fpath = res.get("file", "")
        if fpath and os.path.isfile(fpath):
            with open(fpath, "rb") as f:
                st.download_button("⬇️ 결과 TXT 다운로드", f.read(),
                                   file_name=os.path.basename(fpath),
                                   mime="text/plain", use_container_width=True)
        st.caption("저장 경로: %s" % fpath)
    else:
        st.error("추출 실패: %s" % res.get("error", "알 수 없는 오류"))
        st.info("AM 이 실행·로그인된 상태인지, PROJECT/USER/MDB 값이 맞는지 확인하세요. "
                "먼저 사이드바의 'AM 환경 자동 감지'를 눌러 값을 채우면 로그인 성공률이 올라갑니다.")


if st.button("🚀 추출 실행", type="primary", use_container_width=True,
             disabled=not (proj_val and user and mdb)):
    run_extract(start, "PDMS 세션 시작 → 로그인 → leaf 추출 중... (수 분 소요될 수 있음)")

# ─────────────────────────────────────────────────────────────
st.divider()
st.subheader("③ AM 현재 선택 요소 하위 추출")
st.caption("실행 중인(마지막 활성) AM Explorer 에서 선택한 요소(CE) 하위의 모든 부재 이름을 뽑습니다.")
st.info("선택 상태는 AM 내부 정보라 외부에서 직접 못 읽습니다. AM 명령창에서 `am-export-ce.pmlmac` 를 한 번 실행하면 "
        "현재 선택 요소가 파일로 저장되고, 아래 [AM 현재요소 불러오기] 로 자동 입력됩니다. "
        "(또는 AM Explorer 에서 본 Ref/Name 을 오른쪽에 직접 입력)")

CE_FILE = r"C:\Users\Public\Documents\am_current_element.txt"
cc1, cc2 = st.columns([1, 3])
if cc1.button("🔄 AM 현재요소 불러오기", use_container_width=True):
    if os.path.isfile(CE_FILE):
        try:
            ss["f_ce"] = open(CE_FILE, "r", encoding="utf-8").read().strip()
            st.success("불러옴: %s" % ss["f_ce"])
        except OSError as e:
            st.error("읽기 실패: %s" % e)
    else:
        st.warning("CE 파일이 없습니다. AM 명령창에서 am-export-ce.pmlmac 를 먼저 실행하거나, 오른쪽에 직접 입력하세요.")
ce = cc2.text_input("현재 선택 요소 (Name 또는 Ref, 예: =123/456 또는 /SITE-XXX)", key="f_ce")

if st.button("📌 선택 요소 하위 모든 부재 이름 추출", use_container_width=True,
             disabled=not (proj_val and user and mdb and ce)):
    run_extract(ce, "선택 요소(%s) 하위 추출 중..." % ce)

# 선택 요소를 선택한 AM 의 3D 뷰에 실제로 ADD (am-exec: 그 AM 창에 명령 전송)
if st.button("🖼️ 선택 요소를 3D 뷰에 ADD (실행)", use_container_width=True,
             disabled=not ss.get("am_windows")):
    add_cmd = ("ADD %s" % ce) if ce else "ADD CE"
    with st.spinner("프로젝트 '%s' 의 AM 명령창에 '%s' 전송 중..." % (proj_val or "?", add_cmd)):
        r = run_cli(["am-exec", "--cmd", add_cmd, "--project", proj_val or "",
                     "--wndclass", ss.get("cmd_class", "")])
    if r.get("ok"):
        st.success("✅ AM 에 전송 완료: %s  → 선택한 프로젝트(%s)의 AM 3D 뷰를 확인하세요." % (add_cmd, proj_val or ""))
    else:
        st.error("전송 실패: %s" % r.get("error", ""))
        st.caption("수동 대안: AM 명령창에 아래 한 줄을 직접 입력하세요. (또는 am-add-ce.pmlmac 실행)")
        st.code(add_cmd, language="text")
if not ss.get("am_windows"):
    st.caption("※ 먼저 위 [🔄 새로고침] 으로 AM 창을 감지하면 ADD 버튼이 활성화됩니다. "
               "ADD 는 선택한 PROJECT 의 창 제목을 찾아 그 AM 에 실행합니다.")
st.caption("ADD 가 전달이 안 되면(키 입력이 AM 명령창에 안 들어가면): 확실한 방법은 "
           "AM 에서 am-add-ce.pmlmac 실행( = ADD CE ). 아래 진단으로 명령창 컨트롤을 알려주시면 "
           "웹 버튼이 그 컨트롤에 직접 입력하도록 맞추겠습니다.")
st.caption("현재 ADD 대상 명령창 class: `%s`" % (ss.get("cmd_class") or "(미지정)"))
with st.expander("🛠️ 고급: AM 명령창 컨트롤 지정 (ADD 가 안 들어갈 때)"):
    st.write("ADD 키 입력이 들어갈 **명령창 컨트롤의 class** 를 지정합니다. "
             "아래에서 목록을 보고 명령창에 해당하는 class 를 고르세요.")
    if st.button("AM 자식창 목록 보기", disabled=not ss.get("am_win_pid")):
        d = run_cli(["am-windows", "--project", proj_val or ""])
        ss["am_children"] = d.get("children", []) if d.get("ok") else []
        if not ss["am_children"]:
            st.warning("자식창을 찾지 못했습니다. am-add-ce.pmlmac 로 ADD 하세요.")
    ch = ss.get("am_children", [])
    if ch:
        st.dataframe([{"class": c.get("class"), "text": c.get("text"), "handle": c.get("handle")} for c in ch],
                     use_container_width=True, height=260)
        classes = sorted({c.get("class") for c in ch if c.get("class")})
        cur = ss.get("cmd_class")
        idx = classes.index(cur) if cur in classes else 0
        pick = st.selectbox("명령창 컨트롤 class 선택", classes, index=idx, key="cmd_class_pick")
        if st.button("✅ 이 class 를 ADD 명령창으로 저장"):
            ss["cmd_class"] = pick
            st.success("저장됨: %s" % pick)
    man = st.text_input("또는 class 직접 입력", value=ss.get("cmd_class", ""), key="cmd_class_manual")
    if st.button("직접 입력값 저장"):
        ss["cmd_class"] = man.strip()
        st.success("저장됨: %s" % ss["cmd_class"])


# ===== 🩺 모니터링 / 진단 (오류 대응) =====
st.divider()
with st.expander("🩺 모니터링 / 진단 (오류 대응)", expanded=False):
    mc1, mc2 = st.columns([1, 3])
    if mc1.button("🔄 새로고침", key="mon_refresh"):
        _rerun()
    auto = mc2.checkbox("자동 새로고침(5초)", value=False, key="mon_auto")

    st.markdown("**핵심 상태**")
    _cli = find_cli()
    st.dataframe([
        {"항목": "E3dLeafCli.exe", "값": _cli or "(없음)", "상태": "OK" if _cli else "없음"},
        {"항목": "애드인", "값": ADDIN_STATUS, "상태": "연결됨" if addin_available() else "미연결"},
        {"항목": "AM env_pid", "값": str(ss.get("am_env_pid")), "상태": ""},
        {"항목": "AM 창 pid", "값": str(ss.get("am_win_pid")), "상태": ""},
        {"항목": "PROJECT/USER/MDB",
         "값": "%s / %s / %s" % (ss.get("f_project"), ss.get("f_user"), ss.get("f_mdb")), "상태": ""},
        {"항목": "명령창 class", "값": ss.get("cmd_class", ""), "상태": ""},
    ], use_container_width=True)

    st.markdown("**브리지 / 세션 파일**")
    _files = {"애드인 상태": ADDIN_STATUS, "요청(req)": ADDIN_REQ, "응답(resp)": ADDIN_RESP,
              "세션(session)": SESSION_FILE, "로그(log)": LOG_FILE}
    _frows = []
    for _label, _p in _files.items():
        _i = finfo(_p)
        _frows.append({"파일": _label, "존재": "O" if _i.get("exists") else "X",
                       "수정시각": _i.get("mtime", "-"), "크기": _i.get("size", "-"), "경로": _p})
    st.dataframe(_frows, use_container_width=True)

    st.markdown("**최근 로그**")
    if os.path.isfile(LOG_FILE):
        st.code(read_text(LOG_FILE, 6000), language="text")
    else:
        st.caption("로그 없음(아직 동작 기록 없음). CLI/애드인/웹 동작 시 기록됩니다.")
    if st.button("로그 비우기", key="mon_clearlog"):
        try:
            open(LOG_FILE, "w").close()
            st.success("로그 비움")
        except OSError as _e:
            st.error(str(_e))

    st.markdown("**자가 진단 (원시 결과)**")
    sc1, sc2 = st.columns(2)
    if sc1.button("CLI list-am 실행", key="mon_selftest_cli"):
        st.json(run_cli(["list-am"]))
    if sc2.button("애드인 session 호출", key="mon_selftest_addin"):
        st.json(addin_call("session", timeout=15))

    if st.checkbox("응답(resp)·세션 파일 내용 보기", key="mon_showfiles"):
        if finfo(ADDIN_RESP).get("exists"):
            st.caption("leaf_resp.json")
            st.code(read_text(ADDIN_RESP, 4000), language="json")
        if finfo(SESSION_FILE).get("exists"):
            st.caption("am_session.txt")
            st.code(read_text(SESSION_FILE, 2000), language="text")

    if auto:
        time.sleep(5)
        _rerun()
