namespace Firefly

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open System.Text.Json

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
    Gc0: int
    Gc1: int
    Gc2: int
    Threads: int
}

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
    let gate = obj ()

    let percentile (sorted: float[]) (p: float) =
        if sorted.Length = 0 then 0.0
        else
            let idx = int (Math.Round(p / 100.0 * float (sorted.Length - 1)))
            sorted.[max 0 (min (sorted.Length - 1) idx)]

    /// Record one completed request.
    member _.Record(status: int, ms: float) =
        Interlocked.Increment(&total) |> ignore
        if status >= 500 then Interlocked.Increment(&errors) |> ignore
        lock gate (fun () ->
            sumMs <- sumMs + ms
            buffer.[writeIdx] <- ms
            writeIdx <- (writeIdx + 1) % bufSize
            if filled < bufSize then filled <- filled + 1)

    member _.IncInFlight() = Interlocked.Increment(&inFlight) |> ignore
    member _.DecInFlight() = Interlocked.Decrement(&inFlight) |> ignore

    member _.Snapshot() : DashboardSnapshot =
        let sorted, count, sumLocal =
            lock gate (fun () -> Array.sub buffer 0 filled, filled, sumMs)
        Array.sortInPlace sorted
        let total' = Interlocked.Read(&total)
        let errors' = Interlocked.Read(&errors)
        let uptime = float (Stopwatch.GetTimestamp() - started) / float Stopwatch.Frequency
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
          Gc0 = GC.CollectionCount 0
          Gc1 = GC.CollectionCount 1
          Gc2 = GC.CollectionCount 2
          Threads = ThreadPool.ThreadCount }

[<RequireQualifiedAccess>]
module Dashboard =

    let private pageTemplate = """<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8">
<title>Firefly Dashboard</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
:root{color-scheme:dark}
*{box-sizing:border-box}
body{margin:0;background:#0b0e14;color:#e6e6e6;font:14px/1.5 ui-monospace,SFMono-Regular,Menlo,monospace}
header{padding:20px 28px;border-bottom:1px solid #1c2230;display:flex;align-items:center;gap:12px}
header h1{margin:0;font-size:18px;font-weight:600}
header .dot{width:9px;height:9px;border-radius:50%;background:#3ddc84;box-shadow:0 0 8px #3ddc84}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:14px;padding:24px 28px}
.card{background:#11151f;border:1px solid #1c2230;border-radius:12px;padding:16px 18px}
.card .label{color:#7d8799;font-size:11px;text-transform:uppercase;letter-spacing:.08em}
.card .value{font-size:26px;font-weight:600;margin-top:6px}
.card .sub{color:#7d8799;font-size:12px;margin-top:2px}
.wide{grid-column:1/-1}
canvas{width:100%;height:80px;display:block;margin-top:8px}
.err .value{color:#ff6b6b}
</style></head>
<body>
<header><span class="dot"></span><h1>Firefly &middot; Live Metrics</h1><span class="sub" id="uptime" style="color:#7d8799"></span></header>
<div class="grid">
  <div class="card wide"><div class="label">Requests / sec</div><div class="value" id="rps">0</div><canvas id="spark" width="600" height="80"></canvas></div>
  <div class="card"><div class="label">Total requests</div><div class="value" id="total">0</div></div>
  <div class="card err"><div class="label">Error rate</div><div class="value" id="errrate">0%</div><div class="sub" id="errcount">0 errors</div></div>
  <div class="card"><div class="label">In flight</div><div class="value" id="inflight">0</div></div>
  <div class="card"><div class="label">Latency p50</div><div class="value" id="p50">0<span class="sub"> ms</span></div></div>
  <div class="card"><div class="label">Latency p95</div><div class="value" id="p95">0<span class="sub"> ms</span></div></div>
  <div class="card"><div class="label">Latency p99</div><div class="value" id="p99">0<span class="sub"> ms</span></div></div>
  <div class="card"><div class="label">Avg latency</div><div class="value" id="avg">0<span class="sub"> ms</span></div></div>
  <div class="card"><div class="label">Memory</div><div class="value" id="mem">0<span class="sub"> MB</span></div></div>
  <div class="card"><div class="label">GC (0/1/2)</div><div class="value" id="gc">0/0/0</div></div>
  <div class="card"><div class="label">Threads</div><div class="value" id="threads">0</div></div>
</div>
<script>
var hist=[],MAX=120;
function fmtUptime(s){var h=Math.floor(s/3600),m=Math.floor(s%3600/60),x=Math.floor(s%60);return h+'h '+m+'m '+x+'s';}
function set(id,v){var e=document.getElementById(id);if(e)e.textContent=v;}
function draw(){var c=document.getElementById('spark'),x=c.getContext('2d'),w=c.width,h=c.height;x.clearRect(0,0,w,h);if(hist.length<2)return;var mx=Math.max.apply(null,hist)||1;x.beginPath();for(var i=0;i<hist.length;i++){var px=i/(MAX-1)*w,py=h-hist[i]/mx*(h-6)-3;i?x.lineTo(px,py):x.moveTo(px,py);}x.strokeStyle='#3ddc84';x.lineWidth=2;x.stroke();}
var es=new EventSource("__MOUNT__/stream");
es.onmessage=function(ev){var d=JSON.parse(ev.data);
  set('rps',d.requestsPerSec.toFixed(1));
  set('total',d.totalRequests);
  set('errrate',(d.errorRate*100).toFixed(1)+'%');
  set('errcount',d.errorCount+' errors');
  set('inflight',d.inFlight);
  set('p50',Math.round(d.p50Ms));set('p95',Math.round(d.p95Ms));set('p99',Math.round(d.p99Ms));
  set('avg',d.avgMs.toFixed(1));
  set('mem',d.memoryMB.toFixed(1));
  set('gc',d.gc0+'/'+d.gc1+'/'+d.gc2);
  set('threads',d.threads);
  set('uptime','up '+fmtUptime(d.uptimeSeconds));
  hist.push(d.requestsPerSec);if(hist.length>MAX)hist.shift();draw();
};
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
                       gc0 = snap.Gc0
                       gc1 = snap.Gc1
                       gc2 = snap.Gc2
                       threads = snap.Threads |}
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
                        collector.Record(resp.Status, sw.Elapsed.TotalMilliseconds)
                        return resp
                    finally
                        collector.DecInFlight()
                }

    /// Live metrics dashboard served at `mount` (page) and `mount/stream` (SSE).
    /// Enable with `App.dashboard "/dashboard"`. Protect the route in production.
    let middleware (mount: string) : Middleware =
        middlewareWith (DashboardCollector()) mount
