# Plan: Redesign PingTracer Route Monitoring & Data Storage

## TL;DR
Replace PingTracer's single-shot traceroute + independent ping loops with continuous parallel traceroute monitoring, and replace the fixed-size PingLog class circular buffer with a struct-based, time-series storage system that supports variable-size collections, time-span retention, disk persistence, and server-side aggregation for the web client.

---

## Notes

- The project source code is in the `PingTest` folder.  The `PingTracer2` folder is provided for reference.  `PingTracer2` is an aborted prototype including some of the new design concepts.

## Phase 1: Core Data Model (no dependencies)

### Step 1.1 — Create `PingRecord` struct
Create `PingTest/Storage/PingRecord.cs`. A compact value type replacing `PingLog`:
- `uint RelativeTimeMs` — **time when ping was sent**, as milliseconds offset from segment start (supports ~49 days per segment)
- `ushort RoundTripMs` — RTT/status code using sentinels:
  - `0xFFFE` = pending (sent, no reply yet)
  - `0xFFFF` = timed out / no response
  - `0..65533` = successful RTT in milliseconds
- Total: **6 bytes per record** vs current `PingLog` class at **~30+ bytes** (object header + DateTime + short + enum on x64)
- `ContinuousRouteMonitor` must create records at send-dispatch time with `RoundTripMs = 0xFFFE`, then update the same record when reply/timeout is known

### Step 1.2 — Create `HopTimeSeries` class
Create `PingTest/Storage/HopTimeSeries.cs`. Represents the time-series data for a specific (hopNumber, IP address) pair during a contiguous monitoring period:
- `readonly byte HopNumber`
- `readonly IPAddress Address`
- `string Hostname` — nullable; resolved asynchronously via reverse DNS
- `readonly DateTime StartTimeUtc` — when this hop/address pair first appeared
- `DateTime? EndTimeUtc` — when it stopped appearing (null = still active)
- `ChunkedSeriesBuffer<PingRecord> Records` — custom chunked append-only buffer (`List<PingRecord[]>` or `LinkedList<PingRecord[]>` with fixed-size chunks, e.g. 4K records/chunk)
- `bool IsActive` - Property computed by checking EndTimeUtc
- `DateTime LastRecordedTimestampUtc` — last accepted timestamp, used to enforce nondecreasing order
- Address assignment behavior: when `Address` is set, immediately queue background reverse DNS lookup; update `Hostname` on completion and fire `HostnameUpdated` event
- Method: `BeginPendingRecord(DateTime sentTimestampUtc)` — computes relative send time and appends a pending record (`0xFFFE`), returning a `RecordHandle` (chunk index + slot index)
- Method: `CompleteRecord(RecordHandle handle, ushort rttOrTimeoutCode)` — updates/replaces the previously-inserted pending record in place when reply/timeout is known
- Ordering rule for `BeginPendingRecord`: if `sentTimestampUtc < LastRecordedTimestampUtc` (clock moved backwards), clamp to `LastRecordedTimestampUtc` before append so ordering remains valid for binary search
- Out-of-order completion rule: replies may complete in any order; updates use `RecordHandle`, so completion order cannot reorder timestamps
- Method: `QueryRange(DateTime start, DateTime end)` — binary search over chunk index + in-chunk offset for efficient range queries
- Method: `AggregateRange(DateTime start, DateTime end, int maxPoints)` — returns downsampled data (min/max/avg per bucket) when record count exceeds maxPoints
- Method: `PruneOlderThan(DateTime cutoff)` — prune by dropping whole front chunks when fully expired; only partial-trim the first surviving chunk
- Event: `HostnameUpdated(HopTimeSeries series)`

### Step 1.3 — Create `RouteSnapshot` class
Create `PingTest/Storage/RouteSnapshot.cs`. Represents the known route at a point in time:
- `DateTime TimestampUtc`
- `HopTimeSeries[] Hops` — indexed by hop number (0-based); null entries for non-responding hops; each non-null entry is a reference to the same `HopTimeSeries` object held in the corresponding `HopHistory` in `MonitoringSession.HopData`
- Method: `DiffFrom(RouteSnapshot previous)` — returns list of changes (hop#, old address, new address), comparing the `Address` property of the `HopTimeSeries` entries in each `Hops` array (null-safe, treating null as "no response")

### Step 1.4 — Create `HopHistory` class
Create `PingTest/Storage/HopHistory.cs`. Holds the full chronological history of time-series data for a single hop position:
- `byte HopNumber` — 0-based hop index (mirrors the array position in `MonitoringSession.HopData` for convenience)
- `List<HopTimeSeries> Series` — ordered list of time-series entries for this hop; each entry represents a distinct period during which the hop responded from a specific address. Later entries start after earlier entries end. Entries are never removed except by `PruneOlderThan`, never reused once closed.
- Property: `HopTimeSeries? ActiveSeries` — returns `Series[^1]` if `Series.Count > 0 && Series[^1].IsActive`, else null
- Thread safety: callers must lock on the `HopHistory` instance when adding to or reading `Series` in a context where concurrent modification is possible

### Step 1.5 — Create `MonitoringSession` class
Create `PingTest/Storage/MonitoringSession.cs`. Top-level container for all data collected while monitoring a target:
- `IPAddress TargetAddress`
- `string DisplayName`
- `DateTime StartTimeUtc`
- `HopHistory[] HopData` — fixed-length array of 255 `HopHistory` objects (one per possible hop, 0-based); initialized eagerly at construction time so the array itself never changes and no locking is needed to access a slot by index
- `List<RouteSnapshot> RouteHistory` — history of route changes
- `RouteSnapshot CurrentRoute` — latest known route
- Method: `RecordTraceResult(TraceRouteHostResult[] results, DateTime timestamp)` — processes a completed traceroute cycle:
  1. Builds a new `RouteSnapshot` (with `HopTimeSeries[]` entries, not raw addresses) from results
  2. Diffs against `CurrentRoute` using `DiffFrom()` to detect address changes per hop
  3. For each hop result, looks up `HopData[hopNumber].Series`; if the last entry's `Address` matches the current result and `IsActive == true`, reuse that series; otherwise create a new `HopTimeSeries` for the current address and append it to the list. **If a hop's address reverts to a previously-seen address, a new `HopTimeSeries` is still created** — historical (closed) series are never reopened or reused
  4. Sets `EndTimeUtc` on the active `HopTimeSeries` (the last entry in `HopData[hop].Series`) for each hop that did not appear in the current trace results
  5. Fires `RouteChanged` event if route differs from `CurrentRoute`
- Method: `GetActiveHops()` — returns the last `Series` entry for each `HopHistory` in `HopData` where that last entry's `IsActive == true`
- Method: `PruneOlderThan(TimeSpan maxAge)` — performs two pruning operations:
  1. **HopTimeSeries pruning**: walks every `HopHistory.Series` list across all 255 `HopData` entries; calls `HopTimeSeries.PruneOlderThan()` on each; removes fully-emptied `HopTimeSeries` objects from the `Series` list. The `HopHistory` objects themselves and the `HopData` array are never resized or nulled.
  2. **RouteSnapshot pruning**: scans `RouteHistory` and removes snapshots older than the cutoff, **except it always retains the single most-recent snapshot that predates the cutoff** (i.e. the last snapshot whose `TimestampUtc` < cutoff is kept, because it describes the route state at the start of the retained time window). All older snapshots before that one are removed.
- Method: `GetAggregatedData(DateTime start, DateTime end, int maxPointsPerHop)` — iterates all 255 `HopHistory` entries in `HopData`, walks each `HopHistory.Series` list, and includes any `HopTimeSeries` whose `[StartTimeUtc, EndTimeUtc?]` overlaps `[start, end]`
- Event: `RouteChanged(RouteSnapshot oldRoute, RouteSnapshot newRoute)`
- Event: `TraceResultRecorded(DateTime timestamp)` — fired after each traceroute cycle is processed

---

## Phase 2: Route Monitoring Engine (depends on Phase 1)

### Step 2.1 — Adapt `RouteTracerMethodD` for the new architecture
Modify `PingTest/TraceRoute/RouteTracerMethodD.cs` so each dispatched ping carries correlation metadata (hop + send timestamp + `RecordHandle`) and completion callbacks can update the exact pending record created at send time. Keep the existing `Action<TraceRouteHostResult>` callback and propagate `RecordHandle` so `MonitoringSession` can call `CompleteRecord()` for the right entry even when replies arrive out of order.

### Step 2.2 — Create `ContinuousRouteMonitor` class
Create `PingTest/Services/ContinuousRouteMonitor.cs`. Replaces the current PingSession's ping loop and is inspired by PingTracer2's `RouteMonitor`:
- `IPAddress TargetAddress`
- `MonitoringSession Session` — the storage backend
- `int IntervalMs` — configurable ping interval (dynamically changeable)
- `byte MaxHops` — adaptive, starts at a default (e.g. 30), adjusts based on responses (shrinks when destination found early, expands when not found)
- `bool IsRunning`
- Uses `System.Threading.Timer` for scheduling (like PingTracer2's RouteMonitor)
- Each timer tick:
  1. For each hop ping being dispatched, immediately call `HopTimeSeries.BeginPendingRecord(sentTime)` and attach returned `RecordHandle` to the ping token
  2. Calls `RouteTracerMethodD.TraceRoute()` to launch all pings in parallel
  3. On each callback, call `HopTimeSeries.CompleteRecord(handle, rttOrTimeout)` to finalize the pending record in place
  4. When `Task.WhenAll` completes, passes route/topology results to `MonitoringSession.RecordTraceResult()`
- Adaptive hop count logic (from PingTracer2's RouteMonitor):
  - Shrink `MaxHops` when destination responds at a lower TTL
  - Expand `MaxHops` (+10) when destination not found
  - Cap at 255
- Destination liveness probe for dead-zone routes:
  - Track `consecutiveDestinationMisses`
  - If misses reach threshold (default 10 cycles), send one extra high-TTL probe ping (default TTL 128) on every trace cycle until destination responds
  - High-TTL probe is for liveness discovery and is not throttled out by hop-throttling rules
  - If high-TTL probe responds from destination, mark destination as reachable and continue normal TTL narrowing to discover likely route length
  - If high-TTL probe also fails repeatedly, continue at a reduced probe cadence and surface destination-unreachable status without unbounded expansion costs
- Supports overlapping trace cycles by design to maintain consistent sampling interval even when prior cycles are still in-flight
- Definition: a "cycle" means one timer tick that dispatches a set of hop pings for that moment in time
- Correlation requirement: `RecordHandle` is sufficient for per-record correctness; no separate cycle ID is required
- Method: `Start()`, `Stop()`, `Dispose()`
- Event: `PingRecordCompleted` — raised immediately on each ping completion so PingSession can push realtime updates to WebSocket clients
- Optional internal callback: end-of-tick completion may be used for route bookkeeping only, but is not exposed as a websocket data stream

### Step 2.3 — Port `TraceThrottler` from PingTracer2
Copy `PingTracer2/TraceRoute/TraceThrottler.cs` to `PingTest/Services/TraceThrottler.cs` and adapt for the PingTracer namespace. This throttles pings to unresponsive hops, reducing wasted bandwidth. Wire into `ContinuousRouteMonitor` to skip hops that haven't responded recently (configurable timeout, e.g. 10 seconds).

---

## Phase 3: Refactor PingSession (depends on Phase 2)

### Step 3.1 — Rewrite `PingSession` to use `ContinuousRouteMonitor`
Modify `PingTest/Services/PingSession.cs`:
- Remove `PingTarget` class entirely (replaced by `MonitoringSession` + `HopTimeSeries`)
- Remove `BackgroundWorker` + `pinger_PingCompleted` callback loop
- Remove circular buffer logic, `ClearNextOffset`, etc.
- Instead, `PingSession.Start()` creates a `ContinuousRouteMonitor` and a `MonitoringSession`
- DNS resolution still happens at startup, but now we resolve the target address and pass it to `ContinuousRouteMonitor` (individual hop addresses are discovered per-cycle)
- For multi-host configurations (comma-separated addresses without traceroute), create one `ContinuousRouteMonitor` per target address
- Events adapted:
  - `TargetAdded` → replaced by `HopDiscovered(HopTimeSeries)` (fired when a new hop/address pair first appears)
  - `TargetRemoved` → replaced by `HopDeactivated(byte hop, IPAddress addr)` (fired when a hop/address stops appearing)
  - `PingResultReceived` → replaced by `PingRecordCompleted(...)` (fired immediately per ping completion for smooth graph animation)
  - `RouteChanged(RouteSnapshot old, RouteSnapshot new)` — new event, bubbled from MonitoringSession
  - No cycle-summary event in public API; realtime updates and route-change events are sufficient
- Retain: `StatusChanged`, `LogCreated`, `SessionStopped`, `SetPingDelay()`
- Add: periodic pruning timer that calls `MonitoringSession.PruneOlderThan()` based on configured retention

### Step 3.2 — Update `Settings.cs`
Modify `PingTest/Settings.cs`:
- Remove or deprecate `cacheSize` (no longer using fixed-size circular buffers)
- Add `TimeSpan dataRetentionPeriod` (default: 1 day; options: 1 day, 3 days, 10 days)
- Add `int maxHopsDefault` (default: 30)
- Add `int destinationProbeMissThreshold` (default: 10)
- Add `byte destinationProbeTtl` (default: 128)
- Add `int destinationProbeIntervalCycles` (default: 1 while unresolved; configurable for reduced cadence)
- Add `int diskFlushIntervalSeconds` (default: 10)
- Add `string dataDirectory` (default: `{SettingsFolderPath}/data/`)

---

## Phase 4: WebSocket API & Client Protocol (depends on Phase 3)

### Step 4.1 — Redesign WebSocket protocol in `PingWebSocketHandler`
Modify `PingTest/Services/PingWebSocketHandler.cs`. New message types:

**Server → Client (binary frames, not JSON):**
- `routeUpdate` frame — sent on connect and when route changes
- `hostnameUpdated` frame — sent when async reverse DNS lookup completes
- `pingUpdate` frame — sent immediately for each ping completion (primary realtime stream for smooth graph animation)
- `aggregatedData` frame — response to data query
- Transport decision: use a custom compact binary frame format with fixed message type byte and packed numeric fields; server and client ship together so schema lockstep is acceptable

**Client → Server (new actions):**
- `{ action: "queryData", targetAddress, startTime, endTime, maxPoints }` — request aggregated data for graph rendering (replaces `requestPingData`)
- Retain: `selectConfig`, `start`, `stop`, `setPingRate`, `getConfigurations`, `saveConfig`, `deleteConfig`
- Remove: `requestPingData` (replaced by `queryData`)

### Step 4.2 — Implement server-side aggregation endpoint
In `PingWebSocketHandler` or a new controller, handle `queryData`:
1. Look up the `MonitoringSession` for the requested target
2. For each `HopTimeSeries` in every `HopHistory.Series` list whose `[StartTimeUtc, EndTimeUtc?]` overlaps `[start, end]`, call `AggregateRange(start, end, maxPoints)`
3. Return aggregated data per overlapping hop/address series with fields: timestamp, min RTT, max RTT, avg RTT, packet loss percentage, plus series metadata (`hop`, `address`, `hostname`, `seriesStartUtc`, `seriesEndUtc`)
4. Use binary search within each HopTimeSeries for O(log n) range lookup

### Step 4.3 — Update initial state sync
Modify `SendInitialState()`:
- Instead of sending `targetAdded` + `pingBulk` per target, send:
  - `routeUpdate` with current route topology
  - Client then issues `queryData` for its visible viewport

---

## Phase 5: Disk Persistence (parallel with Phase 4)

### Step 5.1 — Design binary file format
Create `PingTest/Storage/PingDataFile.cs`:

The file is a **pure sequential block-append log**. No block is ever modified after it is written; new data is always appended to the end. This is the only design that is compatible with continuous flushing when multiple hop series are interleaved over time.

**File structure:**
```
[File Header]  (written once when the file is first created)
  Magic:            4 bytes  "PTRC"
  Version:          2 bytes  (uint16)
  TargetAddressLen: 1 byte (4 = IPv4, 16 = IPv6; 0 = no response)
  TargetAddress:    N bytes
  SessionStartUtc:  8 bytes  (uint64, Unix timestamp ms)

[Blocks]  (appended continuously during the session)
  Each block begins with:
    BlockType:   1 byte   (see types below)
    BlockLength: 4 bytes  (uint32, byte count of block body; allows unknown types to be skipped)
  Followed by the block body.

Block types:
  0x01  HopTimeSeriesStartBlock
          HopNumber:      1 byte
          TimestampUtc:   8 bytes  (uint64, Unix ms; this is StartTimeUtc for the new HopTimeSeries,
                                    and also the implicit EndTimeUtc for the previous series at this hop)
          AddrLen:        1 byte (4 = IPv4, 16 = IPv6; 0 = no response)
          Addr:           N bytes

  0x02  ReverseDnsResultBlock
          AddrLen:        1 byte + N bytes  (the IP address the hostname was resolved from)
          HostnameLen:    2 bytes  (uint16)
          Hostname:       HostnameLen bytes  (UTF-8)

  0x03  PingDataBlock
          HopNumber:      1 byte
          TimeOffset:     4 bytes (uint32 millisecond offset from TimestampUtc of most recent   
                                   HopTimeSeriesStartBlock with matching HopNumber)
          RecordCount:    2 bytes  (uint16; max 65535 records per block)
          Records:        RecordCount × 4 bytes  (uint16 RelativeTimeMs and uint16 RoundTripMs) (max 65535ms spanned per block)
```

**Key design properties:**
- All blocks are written during batched flushes which happen periodically on a configurable interval (default 10 seconds)
- `HopTimeSeriesStartBlock` is emitted whenever a new `HopTimeSeries` is created (address change detected, or a new hop appears). It implicitly closes the previous series for that hop — no explicit deactivation block is needed for address-change transitions.
- `ReverseDnsResultBlock` is emitted in the next batched flush after a DNS lookup completes.  On read, the Hostname is assigned to all `HopTimeSeries` with a matching IP address which do not already have a Hostname known.  The IP to Hostname mapping is stored and applied to future `HopTimeSeries` created during the binary file read operation.  The intent of this block is to allow `HopTimeSeries` hostnames to be efficiently serialized even though the Hostname is loaded asynchronously and may not be known at the time when a `HopTimeSeriesStartBlock` is written.  These handling instructions account for the possibility that multiple `ReverseDnsResultBlock` may exist for the same IP address with the same or different Hostname strings.
- `PingDataBlock` is the most common block, so it is designed for small size, and carries no IP address or absolute series-start timestamp; the reader determines which `HopTimeSeries` to append to by tracking the **most recently seen `HopTimeSeriesStartBlock`** for each `HopNumber`.
  - The timestamp of a Record is determined by adding together the`TimestampUtc` from `HopTimeSeriesBlock`, the TimeOffset from the `PingDataBlock`, and the `RelativeTimeMs` from the Record.
  - If a batched flush includes more than 65535 Records for a single HopNumber, or contains records spanning more than 65535ms of time, writer must split the records for that HopNumber into multiple `PingDataBlock`.
- Writer never seeks backwards; `FileStream.Position` only advances.

~4 bytes per ping record on disk, plus some overhead.  Overhead is higher with more frequent batched flushes.

### Step 5.2 — Implement `PingDataWriter`
Create `PingTest/Storage/PingDataWriter.cs`:
- Serializes hop information and ping records to a file, in batches.
- All blocks are written during batched flushes which happen periodically on a configurable interval (default 10 seconds)
- Subscribes to `MonitoringSession` event `HopTimeSeries.HostnameUpdated`
  - When `HopTimeSeries.HostnameUpdated` event is raised, queues a `ReverseDnsResultBlock` to be written during the next batched flush.
- **Batched flush** on configurable interval (default 10 seconds):
  - Emits all queued `ReverseDnsResultBlock`
  - Emits `HopTimeSeriesStartBlock` when a new `HopTimeSeries` begins.
  - Maintains a `long lastFlushedRecordIndex` per `HopTimeSeries`
  - For each `HopTimeSeries` with records since `lastFlushedRecordIndex`, iterates records starting from the unflushed index; if a pending (in-flight, `0xFFFE`) record is encountered during iteration, **stop immediately** and complete the `PingDataBlock` with only the records iterated so far — do not write the in-flight record and do not skip ahead past it; it will be included naturally in a future flush once completed. If the number of records to flush exceeds 65535, emit multiple consecutive `PingDataBlock`s for the same series.
  - Updates `lastFlushedRecordIndex` per series after each flush
- Opens file with `FileShare.Read` so reader can load historical data concurrently
- On creating a **new** session file: write the file header, then begin appending blocks. Do **not** write a file header when reopening an existing file to resume a session — append new blocks directly after the existing content.
- On session stop: perform a final flush, then **wait for all in-flight pings to complete** (i.e. wait until no `HopTimeSeries` contains any pending `0xFFFE` records) before performing one last flush and closing the file, ensuring no completed ping results are omitted from disk.

### Step 5.3 — Implement `PingDataReader`
Create `PingTest/Storage/PingDataReader.cs`:
- Reads file header, then scans blocks sequentially from start to end
- Skips unknown block types using `BlockLength`
- Incomplete trailing block handling: if the end of the file is reached mid-block (i.e. bytes remain but are fewer than the expected block body size declared in `BlockLength`), the block is corrupt (likely due to a crash during write). The reader must:
  1. Notify the user: "The session file ends with a corrupted block, likely due to an unclean shutdown. Would you like to truncate the file at the last complete block and resume the session?"
  2. If the user grants permission, truncate the file at the byte offset immediately after the last successfully-read complete block, then proceed with loading the recovered data and allow the session to be resumed (new blocks appended)
  3. If the user denies permission, open the file read-only; do not allow the session to be resumed or new ping data to be appended to this file
- Maintains two reader-local dictionaries:
  - `Dictionary<byte, HopTimeSeries> currentSeriesPerHop` — most recently started series for each hop number
  - `Dictionary<IPAddress, string> dnsCache` — all resolved hostname mappings seen so far in the file
- Reconstructs `MonitoringSession` by replaying blocks in order:
  - `HopTimeSeriesStartBlock` → creates a new `HopTimeSeries` with the block's address and `StartTimeUtc`, appends to `HopData[hop].Series`, updates `currentSeriesPerHop[hop]`. Immediately checks `dnsCache` for the new series' address and sets `Hostname` if found.
  - `ReverseDnsResultBlock` → adds/updates `dnsCache[address] = hostname`. Iterates all `HopTimeSeries` across all `HopData` entries with `Address == block.Address` and null `Hostname`, sets `Hostname`.
  - `PingDataBlock` → appends records to `currentSeriesPerHop[hop]`
- When reaching end of file, iterate over all `HopTimeSeries` and set `EndTimeUtc` to the timestamp of the last ping sent during that `HopTimeSeries` (effectively marking the hop as deactivated).
- Route history (`MonitoringSession.RouteHistory`) is reconstructed from the sequence of `HopTimeSeriesStartBlock` events
- Supports streaming/incremental load: reader can resume from a saved stream offset to append only new blocks into an already-loaded session

### Step 5.4 — Wire persistence into `PingSession`
- On `PingSession.Start()`: create `PingDataWriter` for the new session file
- On `PingSession.Stop()`: do final flush, close file
- On startup: optionally scan data directory and offer to resume/view past sessions; pass file path to `PingDataReader` to hydrate a `MonitoringSession` before starting `ContinuousRouteMonitor`

---

## Phase 6: Web Client Updates (depends on Phase 4)

### Step 6.1 — Update Pinia store (`ping.js`)
- Replace per-target `pingData` circular buffers with a route-aware model:
  - `route: { targetAddress, hops: [{hop, address, displayName, isActive}] }`
  - `hopData: Map<string, AggregatedPoints[]>` keyed by `"hop:address"` — only holds what's needed for current viewport
- Add actions: `queryData(targetAddress, startTime, endTime, maxPoints)`, handle `routeUpdate` / `pingUpdate` / `aggregatedData` binary message types
- Remove: old `pingData` buffer, `cacheSize`

### Step 6.2 — Update `PingGraph.vue`
- Instead of rendering from a local buffer of all pings, render from aggregated data points
- On viewport change (scroll, zoom, resize): issue `queryData` with the visible time range and canvas pixel width as maxPoints
- Debounce/throttle viewport change queries (e.g. 100ms)
- For real-time tail: append incoming `pingUpdate` data points immediately, only query for historical data when scrolling back
- Handle route changes: show visual indicator when a hop's address changes; support showing multiple addresses per hop number over time

### Step 6.3 — Update `WebSocketService.js`
- Handle new binary message types: `routeUpdate`, `pingUpdate`, `aggregatedData`
- Remove handlers for old `ping`, `pingBulk`, `targetAdded`, `targetRemoved`

---

## Relevant Files

**New files to create:**
- `PingTest/Storage/PingRecord.cs` — compact struct for individual ping results
- `PingTest/Storage/HopTimeSeries.cs` — per-hop time series with append, query, aggregate, prune, and async reverse DNS hostname
- `PingTest/Storage/ChunkedSeriesBuffer.cs` — chunked append/prune-friendly buffer with random-access helpers and binary-search support
- `PingTest/Storage/RouteSnapshot.cs` — route topology at a point in time (holds `HopTimeSeries[]`)
- `PingTest/Storage/HopHistory.cs` — single hop's chronological list of time-series entries (`byte HopNumber` + `List<HopTimeSeries>`)
- `PingTest/Storage/MonitoringSession.cs` — top-level data container with `HopHistory[255]` array
- `PingTest/Storage/PingDataFile.cs` — binary file format constants/helpers
- `PingTest/Storage/PingDataWriter.cs` — batched disk writer
- `PingTest/Storage/PingDataReader.cs` — disk reader/loader
- `PingTest/Services/ContinuousRouteMonitor.cs` — timer-driven continuous traceroute engine
- `PingTest/Services/TraceThrottler.cs` — copied/adapted from PingTracer2

**Files to modify:**
- `PingTest/Services/PingSession.cs` — gut and rebuild: remove PingTarget/BackgroundWorker/circular buffer, use ContinuousRouteMonitor + MonitoringSession
- `PingTest/Services/PingWebSocketHandler.cs` — new protocol messages, aggregation queries, updated initial state sync
- `PingTest/Settings.cs` — add retention/persistence settings, deprecate cacheSize
- `PingTest/pingtracer-web/src/stores/ping.js` — route-aware data model, viewport-based queries
- `PingTest/pingtracer-web/src/components/PingGraph.vue` — render from aggregated data, viewport-driven queries
- `PingTest/pingtracer-web/src/library/WebSocketService.js` — handle new message types

**Files for reference only (do not modify):**
- `PingTracer2/TraceRoute/RouteMonitor.cs` — adaptive hop count pattern
- `PingTracer2/TraceRoute/TraceThrottler.cs` — hop throttling logic to copy
- `PingTracer2/TraceRoute/TraceRouteBase.cs` — event-driven traceroute pattern
- `PingTracer2/Pinging/Pong.cs` — response encapsulation reference

**Files to leave untouched (WinForms):**
- `PingTest/MainForm.cs`, `PingTest/PingGraphControl.cs`, `PingTest/OptionsForm.cs`, etc.
- `PingTest/PingLog.cs` — keep for now; WinForms code may still reference it. New code uses PingRecord struct.

---

## Verification

1. **Unit tests for PingRecord/HopTimeSeries** — verify compact storage, binary search range queries, aggregation accuracy with known data sets, pruning correctness, nondecreasing timestamp clamping when system clock moves backward, and pending→complete in-place updates using `RecordHandle`
2. **Unit tests for RouteSnapshot.DiffFrom()** — verify route change detection comparing the `Address` property of `HopTimeSeries` entries; cover hop appearing/disappearing/changing/reverting to prior address
3. **Unit tests for MonitoringSession.RecordTraceResult() and PruneOlderThan()** — verify `HopHistory.Series` list grows correctly on address change, address revert creates new `HopTimeSeries` (no reuse), hops not in results get `EndTimeUtc` set, route change events fire correctly; verify `PruneOlderThan` removes old `RouteHistory` entries but retains exactly one snapshot predating the cutoff (the most recent one before the window)
4. **Integration test for ContinuousRouteMonitor** — mock traceroute, verify adaptive hop count, verify correct behavior under overlapping in-flight traces (each callback updates the intended `RecordHandle`), and verify high-TTL destination probe behavior after miss-threshold is reached
5. **Unit tests for disk format** — write a MonitoringSession to file with PingDataWriter, read back with PingDataReader, verify data integrity including multi-series-per-hop scenarios
6. **Memory benchmarks** — compare memory usage: old (360k PingLog instances) vs new (360k PingRecord structs in chunked storage), expect ~5x reduction
7. **Manual test** — start monitoring a known host, verify route topology appears in web UI, verify real-time streaming, scroll back and verify aggregated historical data loads correctly
8. **Route change test** — use a VPN connect/disconnect to trigger route change; verify web UI shows new route, old hop data preserved in `HopHistory.Series`
9. **Reverse DNS async test** — verify `HopTimeSeries` starts with null hostname, resolves in background, and emits `HostnameUpdated` without blocking ping ingestion

---

## Decisions

- **`HopHistory[]` array replaces dictionary**: `MonitoringSession.HopData` is a fixed 255-element array of `HopHistory` objects (one per possible hop), initialized eagerly at construction. This eliminates key collisions (address-revert creates a new `HopTimeSeries` in the same list rather than needing a new key) and provides O(1) hop lookup.
- **`HopHistory.Series` is always append-only during normal operation**: new series are appended to the end; once a series is closed (`EndTimeUtc` set), it is never reopened. A reverting address gets a brand-new entry regardless.
- **`RouteSnapshot.Hops` holds `HopTimeSeries[]` not `IPAddress[]`**: allows the snapshot to carry a direct reference to the active series at the time, simplifying history replay and route diff logic.
- **PingRecord uses compact RTT/status sentinels** rather than a separate status field — `0xFFFE` = pending, `0xFFFF` = timeout/no-response, `0..65533` = RTT ms.
- **No changes to WinForms code** — PingLog.cs is kept as-is; WinForms components continue using their existing data path until fully deprecated.
- **Server-side aggregation** rather than sending all data to JS.
- **One file per monitoring session** on disk.
- **ContinuousRouteMonitor uses RouteTracerMethodD** — most optimized implementation.
- **Destination liveness probing is explicit** — configurable consecutive miss threshold before high-TTL probe.
- **HopTimeSeries storage uses a chunked series buffer** — enables efficient front-pruning.
- **Record ordering is monotonic by policy** — backward clock clamp.
- **Write-at-send, update-on-complete model** — each ping inserts pending record at transmit time.
- **WebSocket transport is binary-first** — compact custom binary frame protocol.
- **Aggregation/query behavior for pending records** — pending (`0xFFFE`) excluded from latency stats, optionally counted as `inFlight`.

## Further Considerations

1. **Multi-host configurations without traceroute**: When the user provides multiple comma-separated addresses (no traceroute), should each address get its own `ContinuousRouteMonitor` doing full traceroutes, or should we support a simpler "direct ping only" mode? Recommendation: support both — `ContinuousRouteMonitor` for traceroute mode, and a lightweight `DirectPingMonitor` for single-hop direct monitoring that still uses the new `MonitoringSession` storage.
2. **Overlapping traceroute cycles**: If a traceroute takes longer than the interval, allow overlap to preserve consistent sampling cadence; use `RecordHandle` correlation.
3. **Data migration**: Clean break — old data is ephemeral (never persisted to disk).
