namespace Firefly

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open System.Text.Json

/// Per-route request aggregate shown in the dashboard's Routes table.
type RouteStat = { Route: string; Count: int64; Errors: int64; AvgMs: float }

/// One segment of the memory bar meter (a GC generation).
type MemSegment = { Name: string; MB: float }

/// A point-in-time view of the running app's request and system metrics.
type DashboardSnapshot = {
    UptimeSeconds: float
    TotalRequests: int64
    ErrorCount: int64
    ErrorRate: float
    InFlight: int
    AvgMs: float
    P50Ms: float
    P95Ms: float
    P99Ms: float
    MemoryMB: float
    WorkingSetMB: float
    Gc0: int
    Gc1: int
    Gc2: int
    Threads: int
    Generations: MemSegment list
    Routes: RouteStat list
}

type private RouteAcc() =
    member val Count = 0L with get, set
    member val Errors = 0L with get, set
    member val Sum = 0.0 with get, set

/// Thread-safe in-process collector of request metrics. Fed by the dashboard
/// middleware; read via Snapshot(). One instance per dashboard mount.
type DashboardCollector() =
    let started = Stopwatch.GetTimestamp()
    let mutable total = 0L
    let mutable errors = 0L
    let mutable inFlight = 0
    let bufSize = 2048
    let buffer = Array.zeroCreate<float> bufSize
    let mutable writeIdx = 0
    let mutable filled = 0
    let mutable sumMs = 0.0
    let routes = ConcurrentDictionary<string, RouteAcc>()
    let gate = obj ()
    let genNames = [| "Gen 0"; "Gen 1"; "Gen 2"; "LOH"; "POH" |]

    let percentile (sorted: float[]) (p: float) =
        if sorted.Length = 0 then 0.0
        else
            let idx = int (Math.Round(p / 100.0 * float (sorted.Length - 1)))
            sorted.[max 0 (min (sorted.Length - 1) idx)]

    /// Record one completed request, attributed to a route key (e.g. "GET /x/:id").
    member _.Record(route: string, status: int, ms: float) =
        Interlocked.Increment(&total) |> ignore
        if status >= 500 then Interlocked.Increment(&errors) |> ignore
        let acc = routes.GetOrAdd(route, fun _ -> RouteAcc())
        lock gate (fun () ->
            sumMs <- sumMs + ms
            buffer.[writeIdx] <- ms
            writeIdx <- (writeIdx + 1) % bufSize
            if filled < bufSize then filled <- filled + 1
            acc.Count <- acc.Count + 1L
            if status >= 500 then acc.Errors <- acc.Errors + 1L
            acc.Sum <- acc.Sum + ms)

    member _.IncInFlight() = Interlocked.Increment(&inFlight) |> ignore
    member _.DecInFlight() = Interlocked.Decrement(&inFlight) |> ignore

    member _.Snapshot() : DashboardSnapshot =
        let sorted, count, sumLocal, routeStats =
            lock gate (fun () ->
                let rs =
                    [ for kv in routes ->
                        let a = kv.Value
                        { Route = kv.Key
                          Count = a.Count
                          Errors = a.Errors
                          AvgMs = if a.Count > 0L then a.Sum / float a.Count else 0.0 } ]
                Array.sub buffer 0 filled, filled, sumMs, rs)
        Array.sortInPlace sorted
        let total' = Interlocked.Read(&total)
        let errors' = Interlocked.Read(&errors)
        let uptime = float (Stopwatch.GetTimestamp() - started) / float Stopwatch.Frequency
        let genArr = GC.GetGCMemoryInfo().GenerationInfo.ToArray()
        let generations =
            genArr
            |> Array.mapi (fun i g ->
                { Name = (if i < genNames.Length then genNames.[i] else sprintf "Gen %d" i)
                  MB = float g.SizeAfterBytes / 1048576.0 })
            |> Array.filter (fun s -> s.MB > 0.0)
            |> Array.toList
        let workingSet =
            try float (Process.GetCurrentProcess().WorkingSet64) / 1048576.0 with _ -> 0.0
        { UptimeSeconds = uptime
          TotalRequests = total'
          ErrorCount = errors'
          ErrorRate = if total' = 0L then 0.0 else float errors' / float total'
          InFlight = Volatile.Read(&inFlight)
          AvgMs = if count = 0 then 0.0 else sumLocal / float count
          P50Ms = percentile sorted 50.0
          P95Ms = percentile sorted 95.0
          P99Ms = percentile sorted 99.0
          MemoryMB = float (GC.GetTotalMemory false) / 1048576.0
          WorkingSetMB = workingSet
          Gc0 = GC.CollectionCount 0
          Gc1 = GC.CollectionCount 1
          Gc2 = GC.CollectionCount 2
          Threads = ThreadPool.ThreadCount
          Generations = generations
          Routes = routeStats |> List.sortByDescending (fun r -> r.Count) |> List.truncate 10 }

[<RequireQualifiedAccess>]
module Dashboard =

    let private isIdSegment (s: string) =
        s.Length > 0
        && (s |> Seq.forall Char.IsDigit
            || (s.Length >= 8 && Guid.TryParse(s) |> fst))

    /// Collapse a concrete path into a route key by replacing id-like segments,
    /// e.g. `GET /contacts/42` -> `GET /contacts/:id`.
    let normalizeRoute (method: string) (path: string) =
        let segs = (if String.IsNullOrEmpty path then "/" else path).Split('/')
        let norm = segs |> Array.map (fun s -> if isIdSegment s then ":id" else s)
        method + " " + String.Join("/", norm)

    let private pageTemplate = """<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8">
<title>Firefly · Live Metrics</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
:root{
  color-scheme:dark;
  --bg:#080a0f;--panel:#0e1219;--panel2:#111722;--line:#1b2330;--line2:#232d3d;
  --fg:#eef1f6;--muted:#7e8aa0;--c1:#34d399;--c2:#60a5fa;--c3:#a78bfa;--c4:#fbbf24;--c5:#f87171;
  --num:ui-monospace,"SF Mono",SFMono-Regular,Menlo,monospace;
  --ui:-apple-system,BlinkMacSystemFont,"Segoe UI",Inter,system-ui,sans-serif;
}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font-family:var(--ui);-webkit-font-smoothing:antialiased}
header{padding:14px 28px;display:flex;align-items:center;gap:18px;border-bottom:1px solid var(--line);position:sticky;top:0;background:rgba(8,10,15,.72);backdrop-filter:blur(8px);z-index:5}
.brand{display:flex;align-items:center;gap:10px;font-weight:650;font-size:16px}
.brand .sep{color:var(--muted);font-weight:400}.brand .muted{color:var(--muted);font-weight:500}
.pulse{width:9px;height:9px;border-radius:50%;background:var(--c1);animation:p 1.8s infinite}
@keyframes p{0%{box-shadow:0 0 0 0 rgba(52,211,153,.5)}70%{box-shadow:0 0 0 9px rgba(52,211,153,0)}100%{box-shadow:0 0 0 0 rgba(52,211,153,0)}}
nav{display:flex;gap:4px}
.tab{padding:7px 14px;border-radius:9px;color:var(--muted);font-size:13px;font-weight:550;cursor:pointer;border:1px solid transparent;user-select:none}
.tab:hover{color:var(--fg)}
.tab.active{color:var(--fg);background:var(--panel2);border-color:var(--line)}
.up{margin-left:auto;color:var(--muted);font:500 13px/1 var(--num);font-variant-numeric:tabular-nums}
main{padding:22px 28px;max-width:1320px;margin:0 auto}
.charts{display:grid;grid-template-columns:1fr 1fr;gap:16px}
@media(max-width:820px){.charts{grid-template-columns:1fr}}
.panel{background:linear-gradient(180deg,var(--panel2),var(--panel));border:1px solid var(--line);border-radius:16px;padding:18px 20px;box-shadow:0 1px 0 rgba(255,255,255,.03) inset,0 12px 30px -18px rgba(0,0,0,.8)}
.phead{display:flex;align-items:baseline;gap:12px;margin-bottom:6px}
.ptitle{color:var(--muted);font-size:12px;text-transform:uppercase;letter-spacing:.1em;font-weight:600}
.pbig{margin-left:auto;font:650 30px/1 var(--num);font-variant-numeric:tabular-nums;letter-spacing:-.5px}
.legend{margin-left:auto;display:flex;gap:14px;color:var(--muted);font-size:12px;align-items:center}
.legend i{display:inline-block;width:9px;height:9px;border-radius:2px;margin-right:5px;vertical-align:middle}
.chart{position:relative;height:150px;margin-top:4px}
.chart svg{width:100%;height:100%;display:block;overflow:visible}
.cursor{position:absolute;top:0;bottom:0;width:1px;background:rgba(255,255,255,.18);pointer-events:none;opacity:0;transition:opacity .12s}
.tip{position:absolute;pointer-events:none;background:#0c111b;border:1px solid var(--line2);border-radius:9px;padding:7px 10px;font-size:12px;line-height:1.55;color:var(--fg);white-space:nowrap;transform:translate(-50%,-112%);opacity:0;transition:opacity .12s;box-shadow:0 10px 28px -10px rgba(0,0,0,.8);z-index:3}
.tip b{font-family:var(--num);font-weight:650;margin-left:2px}
.tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:14px;margin-top:16px}
.tile{background:linear-gradient(180deg,var(--panel2),var(--panel));border:1px solid var(--line);border-radius:14px;padding:15px 17px;transition:border-color .2s,transform .2s}
.tile:hover{border-color:var(--line2);transform:translateY(-1px)}
.tile .label{color:var(--muted);font-size:11px;text-transform:uppercase;letter-spacing:.08em;font-weight:600}
.tile .v{font:650 27px/1.1 var(--num);font-variant-numeric:tabular-nums;margin-top:9px;letter-spacing:-.5px}
.tile .v .u{font-size:13px;color:var(--muted);font-weight:500;margin-left:3px}
.tile .sub{color:var(--muted);font-size:12px;margin-top:5px}
.ok{color:var(--c1)}.bad{color:var(--c5)}
table{width:100%;border-collapse:collapse;font-size:13px}
thead th{text-align:left;color:var(--muted);font-size:11px;text-transform:uppercase;letter-spacing:.08em;font-weight:600;padding:0 14px 12px}
tbody td{padding:11px 14px;border-top:1px solid var(--line)}
tbody td.num{font-family:var(--num);font-variant-numeric:tabular-nums;text-align:right}
tbody tr:hover{background:rgba(255,255,255,.02)}
.route{font-family:var(--num)}
.meter{height:16px;border-radius:8px;background:#0c111b;border:1px solid var(--line);display:flex;overflow:hidden;margin-top:6px}
.meter .seg{height:100%}
.mlabels{display:flex;flex-wrap:wrap;gap:16px;margin-top:12px;color:var(--muted);font-size:12px}
.mlabels .k{display:inline-block;width:9px;height:9px;border-radius:2px;margin-right:6px;vertical-align:middle}
.mlabels b{color:var(--fg);font-family:var(--num);font-weight:600;margin-left:4px}
.sys{display:grid;grid-template-columns:1fr 1fr;gap:16px}
@media(max-width:820px){.sys{grid-template-columns:1fr}}
.mt{margin-top:16px}
</style></head>
<body>
<header>
  <span class="pulse"></span>
  <span class="brand">Firefly <span class="sep">/</span> <span class="muted">Live Metrics</span></span>
  <nav>
    <span class="tab active" id="tab-overview" onclick="tab('overview')">Overview</span>
    <span class="tab" id="tab-routes" onclick="tab('routes')">Routes</span>
    <span class="tab" id="tab-system" onclick="tab('system')">System</span>
  </nav>
  <span class="up" id="uptime"></span>
</header>
<main>
  <section id="sec-overview">
    <div class="charts">
      <div class="panel">
        <div class="phead"><span class="ptitle">Requests / sec</span><span class="pbig" id="rps">0</span></div>
        <div class="chart"><svg id="rpsChart" preserveAspectRatio="none"></svg><div class="cursor" id="rpsCur"></div><div class="tip" id="rpsTip"></div></div>
      </div>
      <div class="panel">
        <div class="phead"><span class="ptitle">Latency</span>
          <span class="legend"><span><i style="background:var(--c2)"></i>p50</span><span><i style="background:var(--c3)"></i>p95</span><span><i style="background:var(--c5)"></i>p99</span></span>
        </div>
        <div class="chart"><svg id="latChart" preserveAspectRatio="none"></svg><div class="cursor" id="latCur"></div><div class="tip" id="latTip"></div></div>
      </div>
    </div>
    <div class="tiles">
      <div class="tile"><div class="label">Total requests</div><div class="v" id="total">0</div></div>
      <div class="tile"><div class="label">Error rate</div><div class="v"><span id="errrate" class="ok">0%</span></div><div class="sub" id="errcount">0 errors</div></div>
      <div class="tile"><div class="label">In flight</div><div class="v" id="inflight">0</div></div>
      <div class="tile"><div class="label">Avg latency</div><div class="v"><span id="avg">0</span><span class="u">ms</span></div></div>
      <div class="tile"><div class="label">p50 / p95 / p99</div><div class="v" style="font-size:21px"><span id="pstat">0 / 0 / 0</span><span class="u">ms</span></div></div>
      <div class="tile"><div class="label">Threads</div><div class="v" id="threads2">0</div></div>
    </div>
  </section>

  <section id="sec-routes" style="display:none">
    <div class="panel">
      <div class="phead"><span class="ptitle">Top routes by requests</span></div>
      <table>
        <thead><tr><th>Route</th><th style="text-align:right">Requests</th><th style="text-align:right">Avg ms</th><th style="text-align:right">Errors</th></tr></thead>
        <tbody id="routesBody"><tr><td colspan="4" style="color:var(--muted)">waiting for traffic…</td></tr></tbody>
      </table>
    </div>
  </section>

  <section id="sec-system" style="display:none">
    <div class="sys">
      <div class="panel">
        <div class="phead"><span class="ptitle">Managed memory</span><span class="pbig" id="heap" style="font-size:24px">0</span><span class="u" style="color:var(--muted);font-size:13px">MB heap</span></div>
        <div class="meter" id="memMeter"></div>
        <div class="mlabels" id="memLabels"></div>
        <div class="mlabels mt"><span>Working set <b><span id="ws">0</span> MB</span><span>GC collections <b id="gc">0 / 0 / 0</b></span></div>
      </div>
      <div class="panel">
        <div class="phead"><span class="ptitle">Thread pool</span><span class="pbig" id="threads" style="font-size:24px">0</span><span class="u" style="color:var(--muted);font-size:13px">threads</span></div>
        <div class="meter" id="thrMeter"></div>
        <div class="mlabels mt"><span>Uptime <b id="uptime2">—</b></span></div>
      </div>
    </div>
  </section>
</main>
<script>
var MAX=90, rps=[], p50=[], p95=[], p99=[], charts=[], COLORS=['#34d399','#60a5fa','#a78bfa','#fbbf24','#f87171'];
function set(id,v){var e=document.getElementById(id);if(e)e.textContent=v;}
function push(a,v){a.push(v);if(a.length>MAX)a.shift();}
function esc(s){return String(s).replace(/[&<>"]/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c];});}
function fmtUptime(s){var h=Math.floor(s/3600),m=Math.floor(s%3600/60),x=Math.floor(s%60);return (h?h+'h ':'')+m+'m '+x+'s';}
function fmtMs(v){return v>=10?Math.round(v).toString():v>=1?v.toFixed(1):v.toFixed(2);}
function fmtN(n){return n.toString().replace(/\B(?=(\d{3})+(?!\d))/g,',');}
function tab(name){['overview','routes','system'].forEach(function(t){document.getElementById('sec-'+t).style.display=t===name?'':'none';document.getElementById('tab-'+t).className='tab'+(t===name?' active':'');});render();}
function svgPath(data,X,Y){var s='';for(var i=0;i<data.length;i++){s+=(i?'L':'M')+X(i).toFixed(1)+' '+Y(data[i]).toFixed(1)+' ';}return s.trim();}
function drawChart(c){
  var r=c.svg.parentNode.getBoundingClientRect(), w=r.width, h=r.height||150, pad=10;
  if(!w)return;
  var max=1; c.series.forEach(function(s){s.data.forEach(function(v){if(v>max)max=v;});}); max*=1.2;
  var X=function(i){return i/(MAX-1)*w;}, Y=function(v){return h-pad-(v/max)*(h-2*pad);};
  c._w=w; c._X=X; c._Y=Y;
  var p='<defs>';
  c.series.forEach(function(s,i){ if(s.fill) p+='<linearGradient id="grad-'+c.svg.id+'-'+i+'" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="'+s.color+'" stop-opacity="0.35"/><stop offset="95%" stop-color="'+s.color+'" stop-opacity="0"/></linearGradient>'; });
  p+='</defs>';
  for(var g=1;g<4;g++){var gy=(h*g/4).toFixed(1); p+='<line x1="0" y1="'+gy+'" x2="'+w+'" y2="'+gy+'" stroke="rgba(255,255,255,.06)" stroke-dasharray="3 3"/>';}
  p+='<text x="4" y="11" fill="#7e8aa0" font-size="10" font-family="var(--num)">'+c.fmtAxis(max/1.2)+'</text>';
  p+='<text x="4" y="'+(h-3)+'" fill="#7e8aa0" font-size="10">0</text>';
  c.series.forEach(function(s,i){
    if(s.data.length<2)return;
    var d=svgPath(s.data,X,Y);
    if(s.fill) p+='<path d="'+d+' L'+X(s.data.length-1).toFixed(1)+' '+h+' L'+X(0).toFixed(1)+' '+h+' Z" fill="url(#grad-'+c.svg.id+'-'+i+')"/>';
    p+='<path d="'+d+'" fill="none" stroke="'+s.color+'" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/>';
  });
  c.svg.setAttribute('viewBox','0 0 '+w+' '+h); c.svg.innerHTML=p;
}
function render(){charts.forEach(drawChart);}
function moveTip(c,ev){
  var n=c.series[0].data.length; if(n<2){hideTip(c);return;}
  var r=c.svg.parentNode.getBoundingClientRect(), mx=ev.clientX-r.left;
  var i=Math.round(mx/c._w*(MAX-1)); i=Math.max(0,Math.min(n-1,i));
  c.cur.style.left=c._X(i)+'px'; c.cur.style.opacity=1;
  c.tip.innerHTML=c.series.map(function(s){return '<span style="color:'+s.color+'">&#9679;</span> '+s.label+' <b>'+c.fmt(s.data[i])+'</b>';}).join('<br>');
  var ty=Math.min.apply(null,c.series.map(function(s){return c._Y(s.data[i]);}));
  c.tip.style.left=c._X(i)+'px'; c.tip.style.top=(ty-8)+'px'; c.tip.style.opacity=1;
}
function hideTip(c){c.cur.style.opacity=0; c.tip.style.opacity=0;}
function setupCharts(){
  charts=[
    {svg:document.getElementById('rpsChart'),cur:document.getElementById('rpsCur'),tip:document.getElementById('rpsTip'),fmt:function(v){return v.toFixed(1)+' req/s';},fmtAxis:function(v){return Math.round(v);},series:[{data:rps,color:'#34d399',label:'rps',fill:true}]},
    {svg:document.getElementById('latChart'),cur:document.getElementById('latCur'),tip:document.getElementById('latTip'),fmt:function(v){return fmtMs(v)+' ms';},fmtAxis:function(v){return fmtMs(v)+'ms';},series:[{data:p50,color:'#60a5fa',label:'p50'},{data:p95,color:'#a78bfa',label:'p95'},{data:p99,color:'#f87171',label:'p99'}]}
  ];
  charts.forEach(function(c){var wrap=c.svg.parentNode;wrap.addEventListener('mousemove',function(e){moveTip(c,e);});wrap.addEventListener('mouseleave',function(){hideTip(c);});});
  render();
}
function renderRoutes(rs){
  var tb=document.getElementById('routesBody');
  if(!rs||!rs.length){tb.innerHTML='<tr><td colspan="4" style="color:var(--muted)">waiting for traffic…</td></tr>';return;}
  tb.innerHTML=rs.map(function(r){return '<tr><td class="route">'+esc(r.route)+'</td><td class="num">'+fmtN(r.count)+'</td><td class="num">'+fmtMs(r.avgMs)+'</td><td class="num '+(r.errors>0?'bad':'')+'">'+r.errors+'</td></tr>';}).join('');
}
function renderMem(gens,heap,ws,gc){
  var total=0; gens.forEach(function(g){total+=g.mb;});
  var m=document.getElementById('memMeter'), l=document.getElementById('memLabels');
  m.innerHTML=gens.map(function(g,i){var w=total>0?(g.mb/total*100):0;return '<div class="seg" style="width:'+w+'%;background:'+COLORS[i%COLORS.length]+'"></div>';}).join('');
  l.innerHTML=gens.map(function(g,i){return '<span><i class="k" style="background:'+COLORS[i%COLORS.length]+'"></i>'+esc(g.name)+'<b>'+g.mb.toFixed(2)+' MB</b></span>';}).join('');
  set('heap',heap.toFixed(1)); set('ws',ws.toFixed(0)); set('gc',gc);
}
var es=new EventSource("__MOUNT__/stream");
es.onmessage=function(ev){var d=JSON.parse(ev.data);
  set('rps',d.requestsPerSec.toFixed(1));
  set('total',fmtN(d.totalRequests));
  var er=document.getElementById('errrate'); er.textContent=(d.errorRate*100).toFixed(1)+'%'; er.className=d.errorCount>0?'bad':'ok';
  set('errcount',fmtN(d.errorCount)+' errors');
  set('inflight',d.inFlight);
  set('avg',fmtMs(d.avgMs));
  set('pstat',fmtMs(d.p50Ms)+' / '+fmtMs(d.p95Ms)+' / '+fmtMs(d.p99Ms));
  set('threads',d.threads); set('threads2',d.threads);
  set('uptime','up '+fmtUptime(d.uptimeSeconds)); set('uptime2',fmtUptime(d.uptimeSeconds));
  document.getElementById('thrMeter').innerHTML='<div class="seg" style="width:'+Math.min(d.threads/64,1)*100+'%;background:#60a5fa"></div>';
  push(rps,d.requestsPerSec); push(p50,d.p50Ms); push(p95,d.p95Ms); push(p99,d.p99Ms);
  render();
  renderRoutes(d.routes);
  renderMem(d.generations,d.memoryMB,d.workingSetMB,d.gc0+' / '+d.gc1+' / '+d.gc2);
};
window.addEventListener('resize',render);
setupCharts();
</script>
</body></html>"""

    let private page (mount: string) = pageTemplate.Replace("__MOUNT__", mount)

    let private streamFn (collector: DashboardCollector) : SseWriter -> Request -> Task<unit> =
        fun writer req -> task {
            let ct = req.Raw.RequestAborted
            let mutable prevTotal = collector.Snapshot().TotalRequests
            let mutable prevTs = Stopwatch.GetTimestamp()
            while not ct.IsCancellationRequested do
                do! Task.Delay(1000, ct)
                let snap = collector.Snapshot()
                let now = Stopwatch.GetTimestamp()
                let dt = float (now - prevTs) / float Stopwatch.Frequency
                let rate = if dt > 0.0 then float (snap.TotalRequests - prevTotal) / dt else 0.0
                prevTotal <- snap.TotalRequests
                prevTs <- now
                let payload =
                    {| uptimeSeconds = snap.UptimeSeconds
                       requestsPerSec = rate
                       totalRequests = snap.TotalRequests
                       errorCount = snap.ErrorCount
                       errorRate = snap.ErrorRate
                       inFlight = snap.InFlight
                       avgMs = snap.AvgMs
                       p50Ms = snap.P50Ms
                       p95Ms = snap.P95Ms
                       p99Ms = snap.P99Ms
                       memoryMB = snap.MemoryMB
                       workingSetMB = snap.WorkingSetMB
                       gc0 = snap.Gc0
                       gc1 = snap.Gc1
                       gc2 = snap.Gc2
                       threads = snap.Threads
                       generations = snap.Generations |> List.map (fun g -> {| name = g.Name; mb = g.MB |})
                       routes = snap.Routes |> List.map (fun r -> {| route = r.Route; count = r.Count; avgMs = r.AvgMs; errors = r.Errors |}) |}
                do! writer.Data(JsonSerializer.Serialize payload)
        }

    /// Dashboard middleware using a caller-provided collector (handy for tests).
    let middlewareWith (collector: DashboardCollector) (mount: string) : Middleware =
        let streamPath = mount.TrimEnd('/') + "/stream"
        fun next req ->
            let path = req.Path
            if path = mount then
                task { return Response.html (page mount) }
            elif path = streamPath then
                (Sse.handler (streamFn collector)) req
            else
                task {
                    collector.IncInFlight()
                    let sw = Stopwatch.StartNew()
                    try
                        let! resp = next req
                        sw.Stop()
                        collector.Record(normalizeRoute req.Method path, resp.Status, sw.Elapsed.TotalMilliseconds)
                        return resp
                    finally
                        collector.DecInFlight()
                }

    /// Live metrics dashboard served at `mount` (page) and `mount/stream` (SSE).
    /// Enable with `App.dashboard "/dashboard"`. Protect the route in production.
    let middleware (mount: string) : Middleware =
        middlewareWith (DashboardCollector()) mount
