using System.Text;
using System.Text.Json;

// ============================================================================
//  E3dLeafWeb.Demo  —  웹 UI 데모 (E3D/Marine 불필요, 가짜 데이터)
//
//  실배포(net48)와 동일한 화면/흐름:
//    - 두 모드 선택: ① 프로젝트 정보 입력(Standalone)  ② 현재 열린 모델(Addin)
//    - POST /api/extract 로 추출 → 표 + TXT 다운로드
//
//  실배포 버전에서는 ExtractFake(...) 자리에:
//    standalone → Standalone.Start/Open 후 DbElement 재귀
//    addin      → CurrentElement.Element 부터 DbElement 재귀
//  로 바뀌고, 나머지(웹/표/다운로드)는 그대로입니다.
// ============================================================================

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

// 이 데모는 두 모드 모두 가짜로 지원함을 알림
app.MapGet("/api/capabilities", () => Results.Json(new
{
    host = "demo",
    modes = new[] { "standalone", "addin" },
    note = "DEMO: 가짜 데이터. 실제 호스트는 자신이 지원하는 모드만 advertise 합니다."
}));

// 추출 API
app.MapPost("/api/extract", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body, Encoding.UTF8);
    var json = await reader.ReadToEndAsync();
    ExtractRequest r;
    try { r = JsonSerializer.Deserialize<ExtractRequest>(json, jsonOpts) ?? new ExtractRequest(); }
    catch { r = new ExtractRequest(); }

    var mode = string.IsNullOrWhiteSpace(r.mode) ? "standalone" : r.mode.ToLowerInvariant();

    // (데모) 입력 검증 흉내
    if (mode == "standalone")
    {
        if (string.IsNullOrWhiteSpace(r.project) || string.IsNullOrWhiteSpace(r.user) ||
            string.IsNullOrWhiteSpace(r.password) || string.IsNullOrWhiteSpace(r.mdb))
        {
            return Results.Json(new { ok = false, error = "PROJECT / USER / PASSWORD / MDB 를 모두 입력하세요." });
        }
    }

    var leaves = ExtractFake(mode, r.startElement);
    var text = BuildText(mode, r, leaves);

    return Results.Json(new
    {
        ok = true,
        mode,
        count = leaves.Count,
        rows = leaves,
        text
    });
});

// 메인 페이지
app.MapGet("/", () => Results.Content(Html, "text/html; charset=utf-8"));

app.Run("http://127.0.0.1:5099");


// ---------- 가짜 모델 / 추출 (실배포에서 AVEVA 호출로 교체) -------------------
static List<LeafRow> ExtractFake(string mode, string startElement)
{
    int rc = 12345600;
    Func<string, string, LeafRow> leaf = (type, name) =>
        new LeafRow { Type = type, Name = name ?? "", Reference = "=" + (rc++) + "/" + (rc++ % 1000) };

    // Addin 모드면 "현재 열린 모델(CE 하위)"인 척 일부만, Standalone 모드면 전체인 척
    var rows = new List<LeafRow>();

    // EQUIPMENT 하위 프리미티브
    rows.Add(leaf("BOX", ""));
    rows.Add(leaf("CYLI", ""));
    rows.Add(leaf("CYLI", "/PUMP-101-NOZZLE-SEAT"));
    rows.Add(leaf("DISH", ""));
    rows.Add(leaf("NOZZ", "/TANK-201-N1"));

    if (mode == "standalone")
    {
        // PIPING 까지 전체
        rows.Add(leaf("ELBO", ""));
        rows.Add(leaf("TEE", ""));
        rows.Add(leaf("FLAN", ""));
        rows.Add(leaf("GASK", ""));
        // HULL(선체) 예시 (Marine)
        rows.Add(leaf("PLAT", "/BLK01-PNL-A-PLATE"));
        rows.Add(leaf("STIF", ""));
    }

    // startElement 지정 시 이름에 반영(데모)
    if (!string.IsNullOrWhiteSpace(startElement))
        rows = rows.Where(x => true).ToList(); // 데모: 필터 흉내(전체 유지)

    return rows;
}

static string BuildText(string mode, ExtractRequest r, List<LeafRow> leaves)
{
    var sb = new StringBuilder();
    sb.AppendLine("# AVEVA Leaf Export (WEB DEMO)");
    sb.AppendLine("# Generated : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    sb.AppendLine("# Mode      : " + mode);
    sb.AppendLine("# Project   : " + (mode == "standalone" ? r.project : "(current session)"));
    sb.AppendLine("# Start     : " + (string.IsNullOrWhiteSpace(r.startElement) ? "(all)" : r.startElement));
    sb.AppendLine("# Count     : " + leaves.Count);
    sb.AppendLine("#");
    sb.AppendLine("Type\tName\tReference");
    foreach (var l in leaves)
        sb.AppendLine(l.Type + "\t" + l.Name + "\t" + l.Reference);
    return sb.ToString();
}

// ---------- DTO ----------
class ExtractRequest
{
    public string mode { get; set; } = "standalone";
    public string project { get; set; } = "";
    public string user { get; set; } = "";
    public string password { get; set; } = "";
    public string mdb { get; set; } = "";
    public int moduleNumber { get; set; } = 78;
    public string startElement { get; set; } = "";
}
class LeafRow
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Reference { get; set; } = "";
}

// ---------- 임베드 HTML (실배포 net48 와 동일하게 재사용) ----------
partial class Program
{
    const string Html = """
<!DOCTYPE html>
<html lang="ko">
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>AVEVA Leaf Export</title>
<style>
  :root{--bg:#0f1216;--card:#171c22;--line:#2a323c;--txt:#e7edf3;--mut:#9aa7b4;--acc:#4aa3ff;--ok:#36c08a;--warn:#e0b341;}
  *{box-sizing:border-box} body{margin:0;background:var(--bg);color:var(--txt);font:14px/1.5 "Segoe UI",system-ui,sans-serif}
  .wrap{max-width:980px;margin:0 auto;padding:24px}
  h1{font-size:20px;margin:0 0 4px} .sub{color:var(--mut);margin:0 0 18px}
  .badge{display:inline-block;background:#3a2c10;color:var(--warn);border:1px solid #6b5418;border-radius:6px;padding:1px 8px;font-size:12px;margin-left:8px}
  .tabs{display:flex;gap:8px;margin:0 0 16px}
  .tab{flex:1;cursor:pointer;border:1px solid var(--line);background:var(--card);border-radius:10px;padding:14px;text-align:center}
  .tab.active{border-color:var(--acc);box-shadow:0 0 0 1px var(--acc) inset}
  .tab b{display:block;font-size:15px} .tab span{color:var(--mut);font-size:12px}
  .tab.disabled{opacity:.45;cursor:not-allowed}
  .card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:18px;margin-bottom:16px}
  label{display:block;color:var(--mut);font-size:12px;margin:10px 0 4px}
  input{width:100%;background:#0e1318;border:1px solid var(--line);color:var(--txt);border-radius:8px;padding:9px 11px}
  .grid{display:grid;grid-template-columns:1fr 1fr;gap:10px}
  .row{display:flex;gap:10px;align-items:center;margin-top:14px}
  button{background:var(--acc);color:#04111f;border:0;border-radius:9px;padding:11px 18px;font-weight:700;cursor:pointer}
  button.sec{background:#222b34;color:var(--txt);border:1px solid var(--line)}
  button:disabled{opacity:.5;cursor:not-allowed}
  .hint{color:var(--mut);font-size:12px}
  table{width:100%;border-collapse:collapse;margin-top:8px;font-size:13px}
  th,td{border-bottom:1px solid var(--line);padding:7px 9px;text-align:left}
  th{color:var(--mut);font-weight:600} td.mono{font-family:Consolas,monospace}
  .empty{color:var(--mut)} .count{color:var(--ok);font-weight:700}
  .err{color:#ff7a7a}
  .hidden{display:none}
</style>
</head>
<body>
<div class="wrap">
  <h1>AVEVA Leaf Export <span class="badge" id="hostBadge">…</span></h1>
  <p class="sub">샘플 모델의 <b>제일 하위 단위(leaf)</b> 이름과 Ref 를 추출합니다. 모드를 선택하세요.</p>

  <div class="tabs">
    <div class="tab active" id="tabStandalone" onclick="setMode('standalone')">
      <b>① 프로젝트 정보 입력</b><span>Standalone — 프로젝트/MDB 직접 열기</span>
    </div>
    <div class="tab" id="tabAddin" onclick="setMode('addin')">
      <b>② 현재 열린 모델</b><span>Addin — 실행 중인 세션의 CE 하위</span>
    </div>
  </div>

  <!-- Standalone 입력 -->
  <div class="card" id="panelStandalone">
    <div class="grid">
      <div><label>PROJECT (코드)</label><input id="project" value="TRA"/></div>
      <div><label>MDB</label><input id="mdb" value="MAC-MDB"/></div>
      <div><label>USER</label><input id="user" value="SYSTEM"/></div>
      <div><label>PASSWORD</label><input id="password" type="password" value=""/></div>
      <div><label>MODULE NUMBER (78=Model)</label><input id="moduleNumber" value="78"/></div>
      <div><label>START ELEMENT (선택, 비우면 전체 SITE)</label><input id="startA" placeholder="/SITE-EQUIPMENT-AREA01"/></div>
    </div>
  </div>

  <!-- Addin 입력 -->
  <div class="card hidden" id="panelAddin">
    <p class="hint">현재 E3D/Marine 세션의 <b>현재요소(CE)</b> 기준으로 하위 leaf 를 추출합니다. 로그인/환경변수 입력이 필요 없습니다.</p>
    <label>START ELEMENT (선택, 비우면 CE)</label><input id="startB" placeholder="현재요소(CE) 사용"/>
  </div>

  <div class="row">
    <button id="runBtn" onclick="run()">추출</button>
    <button class="sec" id="dlBtn" onclick="download()" disabled>TXT 다운로드</button>
    <span class="hint" id="status"></span>
  </div>

  <div class="card" style="margin-top:16px">
    <div id="result" class="empty">아직 추출 전입니다.</div>
  </div>
</div>

<script>
let MODE = "standalone";
let CAPS = ["standalone","addin"];
let lastText = "", lastCount = 0;

async function init(){
  try{
    const c = await (await fetch("/api/capabilities")).json();
    CAPS = c.modes || CAPS;
    document.getElementById("hostBadge").textContent = (c.host==="demo") ? "DEMO (가짜데이터)" : ("host: "+c.host);
  }catch(e){ document.getElementById("hostBadge").textContent="?"; }
  // 지원하지 않는 모드 탭 비활성화
  if(!CAPS.includes("standalone")) document.getElementById("tabStandalone").classList.add("disabled");
  if(!CAPS.includes("addin")) document.getElementById("tabAddin").classList.add("disabled");
  setMode(CAPS[0] || "standalone");
}
function setMode(m){
  if(!CAPS.includes(m)){ return; }
  MODE = m;
  document.getElementById("tabStandalone").classList.toggle("active", m==="standalone");
  document.getElementById("tabAddin").classList.toggle("active", m==="addin");
  document.getElementById("panelStandalone").classList.toggle("hidden", m!=="standalone");
  document.getElementById("panelAddin").classList.toggle("hidden", m!=="addin");
}
function val(id){ const e=document.getElementById(id); return e?e.value:""; }
async function run(){
  const btn=document.getElementById("runBtn"); btn.disabled=true;
  setStatus("추출 중...","");
  const body = {
    mode: MODE,
    project: val("project"), user: val("user"), password: val("password"),
    mdb: val("mdb"), moduleNumber: parseInt(val("moduleNumber")||"78"),
    startElement: MODE==="standalone" ? val("startA") : val("startB")
  };
  try{
    const res = await (await fetch("/api/extract",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)})).json();
    if(!res.ok){ setStatus(res.error||"실패","err"); document.getElementById("dlBtn").disabled=true; renderEmpty(); btn.disabled=false; return; }
    lastText=res.text; lastCount=res.count;
    setStatus("완료","");
    renderTable(res);
    document.getElementById("dlBtn").disabled=false;
  }catch(e){ setStatus("오류: "+e,"err"); }
  btn.disabled=false;
}
function renderEmpty(){ document.getElementById("result").innerHTML='<span class="empty">결과 없음</span>'; }
function renderTable(res){
  let h = '<div><span class="count">'+res.count+'</span> 개 leaf ('+res.mode+')</div>';
  h += '<table><thead><tr><th>Type</th><th>Name</th><th>Reference</th></tr></thead><tbody>';
  for(const r of res.rows){
    h += '<tr><td>'+esc(r.type)+'</td><td>'+(r.name? esc(r.name):'<span class="empty">(이름없음)</span>')+'</td><td class="mono">'+esc(r.reference)+'</td></tr>';
  }
  h += '</tbody></table>';
  document.getElementById("result").innerHTML = h;
}
function download(){
  const blob = new Blob([lastText], {type:"text/plain;charset=utf-8"});
  const a=document.createElement("a"); a.href=URL.createObjectURL(blob);
  a.download="E3D_Leaf_Export_"+new Date().toISOString().slice(0,19).replace(/[:T]/g,"")+".txt";
  a.click();
}
function setStatus(t,c){ const s=document.getElementById("status"); s.textContent=t; s.className="hint "+(c||""); }
function esc(s){ return (s||"").replace(/[&<>]/g,m=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[m])); }
init();
</script>
</body>
</html>
""";
}
