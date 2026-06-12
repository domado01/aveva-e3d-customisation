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


# ---------- UI ----------
st.title("🛠️ AVEVA Marine — Leaf Export")
st.caption("최하위(leaf) 요소 추출 + 3D 뷰 ADD. 연결에 필요한 모든 설정은 [⚙️ 설정/연결] 탭에 모았습니다.")

ss = st.session_state
for _k, _v in {
    "projects": [], "detected": {}, "am_items": [], "am_windows": [], "am_allprojects": [],
    "am_env_pid": None, "am_win_pid": None, "sel_win_key": None,
    "cmd_class": "WindowsForms10.Window.8.app.0.34f5582_r33_ad1", "am_children": [],
}.items():
    ss.setdefault(_k, _v)

SESSION_FILE = r"C:\Users\Public\Documents\am_session.txt"
CE_FILE = r"C:\Users\Public\Documents\am_current_element.txt"


def detect_am():
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
        st.error(d.get("error", "감지 실패. AM 실행/권한 확인."))
    return d


def load_am_session():
    if not os.path.isfile(SESSION_FILE):
        st.warning("am_session.txt 가 없습니다. AM 명령창에서 am-session.pmlmac 를 먼저 실행하세요.")
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
    st.success("AM 세션 불러옴 → PROJECT=%s · USER=%s · MDB=%s · CE=%s"
               % (kv.get("PROJECT") or "-", kv.get("USER") or "-", kv.get("MDB") or "-", kv.get("CE") or "-"))


def addin_fill_session():
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
        st.success("애드인 세션 → PROJECT=%s · USER=%s · MDB=%s · CE=%s"
                   % (d.get("project") or "-", d.get("user") or "-", d.get("mdb") or "-", d.get("ce") or "-"))
    else:
        st.error(d.get("error"))


def show_result(res):
    if res.get("ok"):
        rows = res.get("rows", [])
        st.success("✅ 부재(leaf) %d 개 추출 완료" % res.get("count", len(rows)))
        if rows:
            st.dataframe(rows, use_container_width=True, height=480)
        fpath = res.get("file", "")
        if fpath and os.path.isfile(fpath):
            with open(fpath, "rb") as f:
                st.download_button("⬇️ 결과 TXT 다운로드", f.read(), file_name=os.path.basename(fpath),
                                   mime="text/plain", use_container_width=True)
        if fpath:
            st.caption("저장 경로: %s" % fpath)
    else:
        st.error("실패: %s" % res.get("error", "알 수 없는 오류"))
        if res.get("stdout") or res.get("stderr"):
            with st.expander("프로그램 출력(stdout/stderr)"):
                if res.get("stdout"):
                    st.code(res["stdout"], language="text")
                if res.get("stderr"):
                    st.code(res["stderr"], language="text")


def run_extract(start_value, spinner):
    proj_val = ss.get("_proj_val", "") or ss.get("f_project", "")
    user = ss.get("f_user", "")
    password = ss.get("f_password", "")
    mdb = ss.get("f_mdb", "")
    module = ss.get("f_module", "78")
    if not (proj_val and user and mdb):
        st.error("PROJECT / USER / MDB 를 [⚙️ 설정/연결] 탭에서 먼저 채우세요.")
        return
    with st.spinner(spinner):
        res = run_cli(["extract", "--project", proj_val, "--user", user, "--password", password,
                       "--mdb", mdb, "--module", module or "78", "--start", start_value])
    show_result(res)


tab_setup, tab_run, tab_monitor = st.tabs(["⚙️ 설정 / 연결", "🚀 추출 · ADD", "🩺 모니터링 / 진단"])

# ======================= 설정 / 연결 =======================
with tab_setup:
    _cli = find_cli()
    st.subheader("연결 상태 (한눈에)")
    _checks = [
        ("E3dLeafCli.exe", bool(_cli), _cli or "없음 — build-cli.ps1 로 빌드 필요"),
        ("애드인(API)", addin_available(), "연결됨" if addin_available() else "미연결 — standalone 으로도 사용 가능"),
        ("AM 감지", bool(ss.get("am_windows")),
         ("AM창 %d개 / 작업pid %s" % (len(ss.get("am_windows", [])), ss.get("am_win_pid"))) if ss.get("am_windows") else "아래 [AM 감지] 클릭"),
        ("PROJECT", bool(ss.get("f_project")), ss.get("f_project") or "미설정"),
        ("USER", bool(ss.get("f_user")), ss.get("f_user") or "직접 입력 필요(로그인 계정)"),
        ("MDB", bool(ss.get("f_mdb")), ss.get("f_mdb") or "직접 입력 필요(로그인 MDB)"),
    ]
    st.dataframe([{"항목": n, "상태": "✅" if ok else "⛔", "내용": d} for n, ok, d in _checks],
                 use_container_width=True)
    if bool(_cli) and ss.get("f_project") and ss.get("f_user") and ss.get("f_mdb"):
        st.success("추출 준비 완료 → [🚀 추출 · ADD] 탭에서 실행하세요.")
    else:
        st.info("PROJECT/USER/MDB 를 채우면 추출 가능합니다. 아래에서 감지·자동입력 또는 직접 입력하세요.")

    st.divider()
    st.subheader("① AM 감지 / 작업 대상 선택")
    if st.button("🔄 AM 감지 (새로고침)", use_container_width=True, key="set_refresh"):
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
        else:
            ss["am_env_pid"] = None
        ss["sel_win_key"] = None
        if not windows and not items:
            st.warning("실행 중인 AM 을 찾지 못했습니다. AM 로그인/권한 확인. %s" % (r.get("error", "") or ""))

    _windows = ss.get("am_windows", [])
    _allp = ss.get("am_allprojects", [])
    if _windows:
        _wl = []
        for w in _windows:
            t = w.get("title") or ("pid " + str(w.get("pid")))
            if len(t) > 50:
                t = t[:50] + "…"
            _wl.append("%s  [프로젝트: %s]" % (t, w.get("project") or "미상"))
        _wi = st.selectbox("작업할 AM 창", list(range(len(_windows))), format_func=lambda i: _wl[i], key="am_win_idx")
        _sw = _windows[_wi]
        ss["am_win_pid"] = _sw.get("pid")
        _wp = _sw.get("project") or ""
        _key = "%s|%s" % (_sw.get("pid"), _sw.get("title"))
        if ss.get("sel_win_key") != _key:
            ss["sel_win_key"] = _key
            _ordered = ([_wp] if _wp else []) + [c for c in _allp if c != _wp]
            ss["projects"] = _ordered if _ordered else _allp
            if _wp:
                ss["f_project"] = _wp
            if ss.get("env_user"):
                ss["f_user"] = ss["env_user"]
            if ss.get("env_mdb"):
                ss["f_mdb"] = ss["env_mdb"]
        if _wp:
            st.success("선택 AM 창 → 프로젝트 **%s** (pid %s)" % (_wp, _sw.get("pid")))
        else:
            st.warning("창 제목/명령줄에서 프로젝트 코드 못 찾음. 아래 PROJECT 에서 직접 선택. (후보: %s)" % (", ".join(_allp) or "-"))
        if _sw.get("cmdline"):
            st.caption("명령줄: %s" % _sw["cmdline"][:200])
    else:
        ss["am_win_pid"] = None
        st.info("[🔄 AM 감지] 를 눌러 실행 중인 AM 을 감지하세요.")

    st.divider()
    st.subheader("② 값 자동 입력")
    _b1, _b2, _b3 = st.columns(3)
    if _b1.button("프로세스 환경 추정", use_container_width=True, key="set_detect",
                  help="실행 AM 환경에서 PROJECT/USER/MDB 추정(USER/MDB는 부정확할 수 있음)"):
        detect_am()
    if _b2.button("PML 세션 불러오기", use_container_width=True, key="set_pml",
                  help="AM 에서 am-session.pmlmac 실행 후 클릭"):
        load_am_session()
    if _b3.button("애드인 세션 불러오기", use_container_width=True, key="set_addin",
                  help="애드인 연결 시 가장 정확"):
        addin_fill_session()

    st.divider()
    st.subheader("③ 접속 정보 (추출/로그인)")
    _projects = ss.get("projects", [])
    _c1, _c2, _c3 = st.columns(3)
    if _projects:
        _cur = ss.get("f_project", _projects[0])
        _idx = _projects.index(_cur) if _cur in _projects else 0
        _proj_val = _c1.selectbox("PROJECT", _projects, index=_idx, key="sel_project")
    else:
        _proj_val = _c1.text_input("PROJECT", key="f_project")
    ss["_proj_val"] = _proj_val
    _c2.text_input("USER", key="f_user")
    _c3.text_input("PASSWORD (없으면 비움)", type="password", key="f_password")
    _c4, _c5, _c6 = st.columns(3)
    _c4.text_input("MDB", key="f_mdb")
    _c5.text_input("MODULE_NUMBER", value=ss.get("f_module", "78"), key="f_module")
    _c6.text_input("시작 요소 (빈칸/전체 = 모델 전체)", value=ss.get("f_start", "전체"), key="f_start",
                   help="특정 요소만: /SITE-XXX 또는 =123/456. 비우면 전체")

    st.divider()
    with st.expander("④ ADD 명령창 class · 애드인 등록 · 경로"):
        st.write("**ADD 대상 명령창 class:** `%s`" % (ss.get("cmd_class") or "(미지정)"))
        _cm = st.text_input("명령창 class 직접 입력/수정", value=ss.get("cmd_class", ""), key="cmd_class_set")
        if st.button("class 저장", key="set_savecls"):
            ss["cmd_class"] = _cm.strip()
            st.success("저장됨: %s" % ss["cmd_class"])
        st.markdown("---")
        st.write("**애드인(API):** %s" % ("연결됨" if addin_available() else "미연결 — E3dLeafAddin 빌드+등록 필요(ADDIN_README)"))
        st.write("**E3dLeafCli.exe:** %s" % (_cli or "없음"))
        st.write("**공용 폴더:** %s" % PUB_DIR)

# ======================= 추출 · ADD =======================
with tab_run:
    _proj_val = ss.get("_proj_val") or ss.get("f_project", "")
    _user = ss.get("f_user", "")
    _mdb = ss.get("f_mdb", "")
    st.caption("대상: PROJECT=%s · USER=%s · MDB=%s · 시작=%s  (변경은 [⚙️ 설정/연결] 탭)"
               % (_proj_val or "-", _user or "-", _mdb or "-", ss.get("f_start", "전체")))

    if addin_available():
        st.subheader("🔌 애드인 (AM 내부 API)")
        st.caption("세션값 채우기는 [⚙️ 설정/연결] 탭의 [애드인 세션 불러오기] 를 사용하세요.")
        _a2, _a3 = st.columns(2)
        if _a2.button("현재요소 하위 추출", use_container_width=True, key="run_ad_ext"):
            with st.spinner("애드인 추출 중..."):
                show_result(addin_call("extract", start="CE", timeout=180))
        if _a3.button("선택요소 3D ADD", use_container_width=True, key="run_ad_add"):
            _d = addin_call("add", arg="CE", timeout=20)
            if _d.get("ok"):
                st.success("✅ ADD 전송: %s" % _d.get("sent", "ADD CE"))
            else:
                st.error(_d.get("error"))
        st.divider()

    st.subheader("② 추출 실행 (standalone)")
    if st.button("🚀 추출 실행", type="primary", use_container_width=True, key="run_extract_btn",
                 disabled=not (_proj_val and _user and _mdb)):
        run_extract(ss.get("f_start", "전체"), "PDMS 세션 시작 → 로그인 → leaf 추출 중...")
    if not (_proj_val and _user and _mdb):
        st.caption("※ PROJECT/USER/MDB 를 [⚙️ 설정/연결] 탭에서 채우면 활성화됩니다.")

    st.divider()
    st.subheader("③ 현재 선택요소 하위 추출 / 3D 뷰 ADD")
    _cc1, _cc2 = st.columns([1, 3])
    if _cc1.button("🔄 AM 현재요소 불러오기", use_container_width=True, key="run_ce_load"):
        if os.path.isfile(CE_FILE):
            try:
                ss["f_ce"] = open(CE_FILE, "r", encoding="utf-8").read().strip()
                st.success("불러옴: %s" % ss["f_ce"])
            except OSError as e:
                st.error("읽기 실패: %s" % e)
        else:
            st.warning("CE 파일 없음. AM 에서 am-export-ce.pmlmac 실행 또는 오른쪽에 직접 입력.")
    _ce = _cc2.text_input("현재 선택 요소 (Name 또는 Ref)", key="f_ce")

    if st.button("📌 선택 요소 하위 모든 부재 이름 추출", use_container_width=True, key="run_ce_ext",
                 disabled=not (_proj_val and _user and _mdb and _ce)):
        run_extract(_ce, "선택 요소(%s) 하위 추출 중..." % _ce)

    if st.button("🖼️ 선택 요소를 3D 뷰에 ADD (실행)", use_container_width=True, key="run_add_btn",
                 disabled=not ss.get("am_windows")):
        _addcmd = ("ADD %s" % _ce) if _ce else "ADD CE"
        with st.spinner("AM 명령창에 '%s' 전송 중..." % _addcmd):
            _r = run_cli(["am-exec", "--cmd", _addcmd, "--project", _proj_val or "",
                          "--wndclass", ss.get("cmd_class", "")])
        if _r.get("ok"):
            st.success("✅ AM 에 전송: %s — 3D 뷰 확인" % _addcmd)
        else:
            st.error("전송 실패: %s" % _r.get("error", ""))
            st.caption("확실한 방법: AM 에서 am-add-ce.pmlmac 실행. 또는 아래 한 줄 직접 입력:")
            st.code(_addcmd, language="text")

    with st.expander("🛠️ 고급: AM 명령창 컨트롤 진단 (ADD 안 들어갈 때)"):
        if st.button("AM 자식창 목록 보기", key="run_diag", disabled=not ss.get("am_win_pid")):
            _d = run_cli(["am-windows", "--project", _proj_val or ""])
            ss["am_children"] = _d.get("children", []) if _d.get("ok") else []
        _ch = ss.get("am_children", [])
        if _ch:
            st.dataframe([{"class": c.get("class"), "text": c.get("text"), "handle": c.get("handle")} for c in _ch],
                         use_container_width=True, height=240)
            _classes = sorted({c.get("class") for c in _ch if c.get("class")})
            _cur = ss.get("cmd_class")
            _idx = _classes.index(_cur) if _cur in _classes else 0
            _pick = st.selectbox("명령창 class 선택", _classes, index=_idx, key="cmd_class_pick")
            if st.button("✅ 이 class 저장", key="run_savecls"):
                ss["cmd_class"] = _pick
                st.success("저장됨: %s" % _pick)

# ======================= 모니터링 / 진단 =======================
with tab_monitor:
    _m1, _m2 = st.columns([1, 3])
    if _m1.button("🔄 새로고침", key="mon_refresh"):
        _rerun()
    _auto = _m2.checkbox("자동 새로고침(5초)", value=False, key="mon_auto")

    st.markdown("**핵심 상태**")
    _cli2 = find_cli()
    st.dataframe([
        {"항목": "E3dLeafCli.exe", "값": _cli2 or "(없음)", "상태": "OK" if _cli2 else "없음"},
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
        st.caption("로그 없음.")
    if st.button("로그 비우기", key="mon_clearlog"):
        try:
            open(LOG_FILE, "w").close()
            st.success("로그 비움")
        except OSError as _e:
            st.error(str(_e))

    st.markdown("**자가 진단**")
    _s1, _s2 = st.columns(2)
    if _s1.button("CLI list-am 실행", key="mon_cli"):
        st.json(run_cli(["list-am"]))
    if _s2.button("애드인 session 호출", key="mon_addin"):
        st.json(addin_call("session", timeout=15))

    if st.checkbox("응답·세션 파일 내용 보기", key="mon_showfiles"):
        if finfo(ADDIN_RESP).get("exists"):
            st.caption("leaf_resp.json")
            st.code(read_text(ADDIN_RESP, 4000), language="json")
        if finfo(SESSION_FILE).get("exists"):
            st.caption("am_session.txt")
            st.code(read_text(SESSION_FILE, 2000), language="text")

    if _auto:
        time.sleep(5)
        _rerun()
