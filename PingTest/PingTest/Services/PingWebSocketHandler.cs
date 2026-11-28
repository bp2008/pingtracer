using BPUtil;
using BPUtil.SimpleHttp;
using BPUtil.SimpleHttp.WebSockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PingTracer.Storage;
using PingTracer.Tracer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PingTracer.Services
{
	/// <summary>
	/// Serves the web interface over a single WebSocket per browser tab.
	/// <para><b>Control plane</b> (JSON text frames): configurations, run control, status, logs, session metadata, addresses and route events.</para>
	/// <para><b>Data plane</b> (binary frames, see <see cref="BinaryFrameType"/>): live ping results and responses to column queries.</para>
	/// <para>
	/// Consistency: live results and query responses for a session are both queued while holding that session's
	/// <see cref="MonitoringSession.DataLock"/>, and each client's queue is delivered in order.  A query response therefore
	/// contains exactly the samples completed before every live item that follows it, so the browser can replace a cached
	/// range with a query response and then apply subsequent live items without double counting or gaps.
	/// </para>
	/// </summary>
	public class PingWebSocketHandler
	{
		private const int MaxLogLines = 1000;
		private const int MaxQueryColumns = 4096;

		private readonly Settings settings;
		private readonly ConcurrentDictionary<string, ClientConnection> clients = new ConcurrentDictionary<string, ClientConnection>();
		private readonly object hubLock = new object();
		private readonly List<SessionFeed> feeds = new List<SessionFeed>();
		private readonly List<string> logBuffer = new List<string>();
		private readonly object logLock = new object();
		private readonly Timer statusTimer;

		private PingSession currentRun;
		private string currentConfigGuid;
		private string status = "Idle";

		/// <summary>Guards <see cref="data"/>, <see cref="dataLoad"/>, <see cref="loadGeneration"/> and <see cref="starting"/>.  Taken before hubLock.</summary>
		private readonly object dataLock = new object();
		/// <summary>The selected configuration's graph data (kept across runs), or null while it loads.</summary>
		private ConfigDataSet data;
		private Task<ConfigDataSet> dataLoad;
		private string dataLoadGuid;
		private int loadGeneration;
		private bool loading;
		/// <summary>True from a Start request until the run has been created.</summary>
		private bool starting;
		/// <summary>Describes what the graphs show: "live" (current/last run) or "history" (a loaded file).</summary>
		private string viewSource = "live";
		private string viewFileName = null;

		public PingWebSocketHandler(Settings settings)
		{
			this.settings = settings;
			currentConfigGuid = settings.lastLoadedConfigurationGuid;
			statusTimer = new Timer(_ => { if (currentRun?.IsRunning == true) BroadcastStatus(); }, null, 1000, 1000);
			PingConfiguration cfg = LoadConfig(currentConfigGuid);
			if (cfg != null)
				BeginLoad(cfg);
		}

		private bool IsBusy => currentRun?.IsRunning == true || starting;

		#region Configuration data

		/// <summary>
		/// Starts loading a configuration's saved graph data in the background, replacing the data shown.  The returned task
		/// completes with the loaded data (or an empty set if saving is disabled or loading failed).
		/// </summary>
		private Task<ConfigDataSet> BeginLoad(PingConfiguration cfg)
		{
			string guid = cfg.guid;
			List<(string, SessionMode)> keys = PingSession.GetSessionKeys(cfg);
			long retentionMs = Math.Max(60, cfg.dataRetentionSeconds) * 1000L;
			string displayName = cfg.displayName;
			int gen;
			lock (dataLock)
			{
				gen = ++loadGeneration;
				data = null;
				loading = true;
				if (viewSource == "live")
					ClearView("live", null);
				dataLoad = Task.Run(() =>
				{
					ConfigDataSet d;
					System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
					try
					{
						d = settings.saveSessionData
							? ConfigDataSet.Load(settings.DataRoot, guid, keys, retentionMs, OnLog)
							: new ConfigDataSet(settings.DataRoot, guid);
					}
					catch (Exception ex)
					{
						OnLog("Unable to load the saved data for " + displayName + ": " + ex.Message);
						Logger.Debug(ex, "ConfigDataSet.Load");
						d = new ConfigDataSet(settings.DataRoot, guid);
					}
					lock (dataLock)
					{
						if (gen != loadGeneration)
							return d;
						data = d;
						loading = false;
						if (viewSource == "live")
							foreach (MonitoringSession s in d.Sessions)
								AddToView(s);
					}
					IReadOnlyList<MonitoringSession> loaded = d.Sessions;
					if (loaded.Count > 0)
					{
						long records = 0;
						foreach (MonitoringSession s in loaded)
							foreach (HopSeries h in s.GetHops())
								records += h.Count;
						OnLog("Loaded the saved data for " + displayName + " (" + records.ToString("N0") + " pings) in " + sw.ElapsedMilliseconds + " ms.");
					}
					BroadcastStatus();
					return d;
				});
				dataLoadGuid = guid;
			}
			BroadcastStatus();
			return dataLoad;
		}

		/// <summary>The data of a configuration: the data being loaded or already loaded for it, or a new load.</summary>
		private Task<ConfigDataSet> GetData(PingConfiguration cfg)
		{
			lock (dataLock)
			{
				if (dataLoad != null && dataLoadGuid == cfg.guid)
					return dataLoad;
			}
			return BeginLoad(cfg);
		}

		#endregion

		#region Connection lifecycle

		/// <summary>
		/// Handles a WebSocket upgrade request on /ws.  The returned task completes when the connection closes; no thread is
		/// held while it is open (the WebSocket has its own reader thread and <see cref="ClientConnection"/> its own sender).
		/// </summary>
		public async Task HandleConnectionAsync(HttpProcessor p)
		{
			string clientId = Guid.NewGuid().ToString("N").Substring(0, 8);
			ClientConnection client = null;
			try
			{
				WebSocket ws = new WebSocket(p);
				ws.ReceiveTimeout = 300000;
				ws.SendTimeout = 30000;
				client = new ClientConnection(clientId, ws);
				clients[clientId] = client;
				SendInitialState(client);
				ws.StartReading(frame =>
				{
					if (frame is WebSocketTextFrame tf)
						HandleClientMessage(client, tf.Text);
				}, close => client.Close());
				await client.Closed.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				if (!HttpProcessor.IsOrdinaryDisconnectException(ex))
					Logger.Debug(ex, "PingWebSocketHandler");
			}
			finally
			{
				clients.TryRemove(clientId, out _);
				client?.Close();
				lock (hubLock)
				{
					foreach (SessionFeed f in feeds)
						f.Unsubscribe(client);
				}
			}
		}

		private void SendInitialState(ClientConnection client)
		{
			client.EnqueueJson(Json(new { type = "hello", serverTime = ServerClock.NowUnixMs, version = 3 }));
			client.EnqueueJson(BuildConfigurationsJson());
			client.EnqueueJson(BuildStatusJson());
			client.EnqueueJson(BuildSettingsJson());
			PingConfiguration cfg = LoadConfig(currentConfigGuid);
			if (cfg != null)
				client.EnqueueJson(BuildConfigDetailsJson(cfg));
			lock (logLock)
				client.EnqueueJson(Json(new { type = "logHistory", messages = logBuffer.ToArray() }));
			lock (hubLock)
			{
				client.EnqueueJson(BuildViewJson());
				foreach (SessionFeed f in feeds)
					f.Subscribe(client);
			}
		}

		#endregion

		#region Session feeds

		/// <summary>
		/// Connects a <see cref="MonitoringSession"/> to the web clients.  Session events are forwarded to subscribed clients
		/// from within the session's data lock, preserving order.
		/// </summary>
		private sealed class SessionFeed
		{
			public readonly MonitoringSession Session;
			private readonly HashSet<ClientConnection> subscribers = new HashSet<ClientConnection>();
			private ClientConnection[] subscriberArray = Array.Empty<ClientConnection>();

			public SessionFeed(MonitoringSession session)
			{
				Session = session;
				lock (session.DataLock)
				{
					session.SampleCompleted += OnSample;
					session.SampleVoided += OnVoid;
					session.AddressAdded += OnAddressAdded;
					session.HostnameResolved += OnHostname;
					session.RouteEventAdded += OnRouteEvent;
					session.StateChanged += OnStateChanged;
				}
			}

			public void Detach()
			{
				lock (Session.DataLock)
				{
					Session.SampleCompleted -= OnSample;
					Session.SampleVoided -= OnVoid;
					Session.AddressAdded -= OnAddressAdded;
					Session.HostnameResolved -= OnHostname;
					Session.RouteEventAdded -= OnRouteEvent;
					Session.StateChanged -= OnStateChanged;
					subscribers.Clear();
					subscriberArray = Array.Empty<ClientConnection>();
				}
			}

			/// <summary>Sends the session's full metadata to the client, then streams changes.</summary>
			public void Subscribe(ClientConnection client)
			{
				lock (Session.DataLock)
				{
					if (!subscribers.Add(client))
						return;
					subscriberArray = subscribers.ToArray();
					client.EnqueueJson(BuildSessionJson(Session));
					client.EnqueueJson(Json(new
					{
						type = "addresses",
						sessionId = Session.Id,
						items = Session.Addresses.All().Select(a => new { id = a.id, ip = a.address.ToString(), host = a.hostname ?? "" }).ToArray(),
					}));
					client.EnqueueJson(BuildRouteEventsJson(Session, Session.GetRouteEvents()));
				}
			}

			public void Unsubscribe(ClientConnection client)
			{
				if (client == null) return;
				lock (Session.DataLock)
				{
					if (subscribers.Remove(client))
						subscriberArray = subscribers.ToArray();
				}
			}

			public ClientConnection[] Subscribers => subscriberArray;

			private void OnSample(MonitoringSession s, int ttl, PingSample r)
			{
				LiveItem item = new LiveItem { SessionId = s.Id, Ttl = (byte)ttl, Kind = 0, TimeUnixMs = s.ToUnixMs(r.TimeMs), Rtt = r.Rtt, Addr = r.Addr };
				foreach (ClientConnection c in subscriberArray)
					c.EnqueueLive(item);
			}

			private void OnVoid(MonitoringSession s, int ttl, PingSample previous)
			{
				LiveItem item = new LiveItem { SessionId = s.Id, Ttl = (byte)ttl, Kind = 1, TimeUnixMs = s.ToUnixMs(previous.TimeMs), Rtt = previous.Rtt, Addr = previous.Addr };
				foreach (ClientConnection c in subscriberArray)
					c.EnqueueLive(item);
			}

			private void OnAddressAdded(MonitoringSession s, ushort id, IPAddress ip)
			{
				SendAll(Json(new { type = "addresses", sessionId = s.Id, items = new[] { new { id, ip = ip.ToString(), host = s.Addresses.GetHostname(id) ?? "" } } }));
			}

			private void OnHostname(MonitoringSession s, ushort id, string host)
			{
				SendAll(Json(new { type = "addresses", sessionId = s.Id, items = new[] { new { id, ip = s.Addresses.GetAddress(id)?.ToString() ?? "", host = host ?? "" } } }));
			}

			private void OnRouteEvent(MonitoringSession s, RouteEvent e)
			{
				SendAll(BuildRouteEventsJson(s, new[] { e }));
			}

			private void OnStateChanged(MonitoringSession s)
			{
				SendAll(BuildSessionJson(s));
			}

			private void SendAll(string json)
			{
				foreach (ClientConnection c in subscriberArray)
					c.EnqueueJson(json);
			}
		}

		private static string BuildSessionJson(MonitoringSession s)
		{
			return Json(new
			{
				type = "session",
				id = s.Id,
				target = s.TargetAddress.ToString(),
				// Every address the session has pinged as its destination (its host name may have resolved differently over time).
				targets = s.GetTargets().Select(ip => ip.ToString()).ToArray(),
				displayName = s.DisplayName,
				startMs = s.DataStartUnixMs,
				endMs = s.IsRunning ? (long?)null : Math.Max(s.DataStartUnixMs, s.ToUnixMs(s.LastRecordTimeMs)),
				// Times between runs when nothing was monitored: [[start, end], ...]
				gaps = s.Gaps.Select(g => new[] { g.start, g.end }).ToArray(),
				mode = s.Mode == SessionMode.Direct ? "direct" : "trace",
				intervalMs = s.IntervalMs,
				timeoutMs = s.TimeoutMs,
				destTtl = s.DestinationTtl,
				repliedTtls = s.GetRepliedTtls(),
				running = s.IsRunning,
				readOnly = s.IsReadOnly,
			});
		}

		private static string BuildRouteEventsJson(MonitoringSession s, IEnumerable<RouteEvent> events)
		{
			return Json(new
			{
				type = "routeEvents",
				sessionId = s.Id,
				items = events.Select(e => new
				{
					t = s.ToUnixMs(e.TimeMs),
					kind = e.Kind switch { RouteEventKind.HopAddress => "addr", RouteEventKind.Target => "target", _ => "dest" },
					ttl = e.Ttl,
					value = e.Value,
					initial = e.Initial,
				}).ToArray(),
			});
		}

		/// <summary>Removes all sessions from the view (clients are told to clear their data).</summary>
		private void ClearView(string source, string fileName)
		{
			lock (hubLock)
			{
				foreach (SessionFeed f in feeds)
					f.Detach();
				feeds.Clear();
				viewSource = source;
				viewFileName = fileName;
				string json = BuildViewJson();
				foreach (ClientConnection c in clients.Values)
					c.EnqueueJson(json);
			}
		}

		private string BuildViewJson() => Json(new { type = "view", source = viewSource, fileName = viewFileName });

		private void AddToView(MonitoringSession session)
		{
			lock (hubLock)
			{
				SessionFeed feed = new SessionFeed(session);
				feeds.Add(feed);
				foreach (ClientConnection c in clients.Values)
					feed.Subscribe(c);
			}
		}

		private SessionFeed FindFeed(int sessionId)
		{
			lock (hubLock)
				return feeds.FirstOrDefault(f => f.Session.Id == sessionId);
		}

		#endregion

		#region Client messages

		private void HandleClientMessage(ClientConnection client, string message)
		{
			try
			{
				JObject msg = JObject.Parse(message);
				string action = msg.Value<string>("action");
				switch (action)
				{
					case "time":
						client.EnqueueJson(Json(new { type = "time", c = msg.Value<double>("c"), s = ServerClock.NowUnixMs }));
						break;
					case "query":
						HandleQuery(client, msg);
						break;
					case "selectConfig":
						HandleSelectConfig(msg.Value<string>("guid"));
						break;
					case "start":
						HandleStart();
						break;
					case "stop":
						HandleStop();
						break;
					case "getConfigurations":
						client.EnqueueJson(BuildConfigurationsJson());
						break;
					case "saveConfig":
						HandleSaveConfig(msg["config"]);
						break;
					case "deleteConfig":
						HandleDeleteConfig(msg.Value<string>("guid"));
						break;
					case "setPingRate":
						currentRun?.SetPingDelay(msg.Value<int>("rate"), msg.Value<bool>("pingsPerSecond"));
						break;
					case "saveSettings":
						HandleSaveSettings(msg["settings"]);
						break;
					case "listHistory":
						client.EnqueueJson(BuildHistoryJson());
						break;
					case "openHistory":
						HandleOpenHistory(msg.Value<string>("fileName"));
						break;
					case "closeHistory":
						HandleCloseHistory();
						break;
				}
			}
			catch (Exception ex)
			{
				client.EnqueueJson(Json(new { type = "error", message = ex.Message }));
			}
		}

		private void HandleQuery(ClientConnection client, JObject msg)
		{
			uint queryId = (uint)(msg.Value<long?>("qid") ?? 0);
			int sessionId = msg.Value<int?>("sessionId") ?? 0;
			int msPerCol = msg.Value<int?>("msPerCol") ?? 1000;
			long startCol = (long)(msg.Value<double?>("startCol") ?? 0);
			int colCount = Math.Clamp(msg.Value<int?>("colCount") ?? 0, 0, MaxQueryColumns);
			if (msPerCol < 1) msPerCol = 1;

			SessionFeed feed = FindFeed(sessionId);
			if (feed == null)
			{
				client.EnqueueBinary(BuildQueryFrame(queryId, sessionId, msPerCol, startCol, colCount, new List<(int, ColumnStats, int)>()));
				return;
			}
			MonitoringSession s = feed.Session;
			lock (s.DataLock)
			{
				List<(int ttl, ColumnStats stats, int nonEmpty)> results = s.Query(startCol, msPerCol, colCount);
				client.EnqueueBinary(BuildQueryFrame(queryId, sessionId, msPerCol, startCol, colCount, results));
			}
		}

		internal static byte[] BuildQueryFrame(uint queryId, int sessionId, int msPerCol, long startCol, int colCount, List<(int ttl, ColumnStats stats, int nonEmpty)> results)
		{
			int estimate = 32 + results.Sum(r => 3 + r.nonEmpty * 17);
			BinaryFrameWriter w = new BinaryFrameWriter(estimate);
			w.U8(BinaryFrameType.QueryResult);
			w.U32(queryId);
			w.U16((ushort)sessionId);
			w.U32((uint)msPerCol);
			w.F64(startCol);
			w.U16((ushort)colCount);
			w.U16((ushort)results.Count);
			foreach ((int ttl, ColumnStats st, int nonEmpty) in results)
			{
				w.U8((byte)ttl);
				w.U16((ushort)nonEmpty);
				for (int c = 0; c < st.Length; c++)
				{
					if (st.N[c] == 0) continue;
					w.U16((ushort)c);
					w.U16(st.N[c]);
					w.U16(st.Lost[c]);
					w.U16(st.Min[c]);
					w.U16(st.Max[c]);
					w.F32(st.Sum[c]);
					w.U16(st.Addr[c]);
					w.U8(st.Flags[c]);
				}
			}
			return w.ToArray();
		}

		private void HandleSelectConfig(string guid)
		{
			if (IsBusy)
			{
				BroadcastError("Stop the current session before choosing a different configuration.");
				return;
			}
			PingConfiguration cfg = LoadConfig(guid);
			if (cfg == null)
			{
				BroadcastError("Configuration not found.");
				return;
			}
			currentConfigGuid = guid;
			settings.lastLoadedConfigurationGuid = guid;
			settings.Save();
			Broadcast(Json(new { type = "configSelected", guid }));
			Broadcast(BuildConfigDetailsJson(cfg));
			BeginLoad(cfg);
		}

		private void HandleStart()
		{
			PingConfiguration cfg;
			lock (dataLock)
			{
				if (IsBusy)
					return;
				cfg = LoadConfig(currentConfigGuid);
				if (cfg == null)
				{
					BroadcastError(currentConfigGuid == null ? "No configuration selected." : "Configuration not found.");
					return;
				}
				starting = true;
			}
			Task.Run(async () =>
			{
				try
				{
					await StartAsync(cfg).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					OnLog("Failed to start: " + ex.Message);
					Logger.Debug(ex, "PingSession.Start");
				}
				finally
				{
					lock (dataLock) starting = false;
					BroadcastStatus();
				}
			});
		}

		private async Task StartAsync(PingConfiguration cfg)
		{
			ConfigDataSet d = await GetData(cfg).ConfigureAwait(false);
			ConfigDataSet.RunLock runLock;
			try
			{
				runLock = d.TryAcquireRunLock();
			}
			catch (Exception ex)
			{
				BroadcastError("Unable to start " + cfg.displayName + ": " + ex.Message);
				return;
			}
			if (runLock == null)
			{
				BroadcastError("\"" + cfg.displayName + "\" is already running in another PingTracer instance.");
				return;
			}
			try
			{
				if (settings.saveSessionData && d.IsStale())
				{
					OnLog("Reloading the saved data for " + cfg.displayName + ": another PingTracer instance has run it since it was loaded.");
					d = await BeginLoad(cfg).ConfigureAwait(false);
				}
			}
			catch
			{
				runLock.Dispose();
				throw;
			}
			PingSession run = new PingSession(cfg, settings, d, runLock);
			lock (dataLock)
			{
				currentRun = run;
				if (viewSource != "live")
				{
					ClearView("live", null);
					foreach (MonitoringSession s in d.Sessions)
						AddToView(s);
				}
			}
			run.SessionCreated += AddToView;
			run.Log += OnLog;
			run.StatusChanged += s =>
			{
				status = s;
				BroadcastStatus();
			};
			run.Stopped += () =>
			{
				BroadcastStatus();
				// Re-send session metadata so clients learn each session's end time.
				foreach (SessionFeed f in SnapshotFeeds())
					f.Session.SetRunning(false);
			};
			run.Start();
		}

		private List<SessionFeed> SnapshotFeeds()
		{
			lock (hubLock) return new List<SessionFeed>(feeds);
		}

		private void HandleStop()
		{
			PingSession run = currentRun;
			if (run == null || !run.IsRunning)
				return;
			Task.Run(async () =>
			{
				try { await run.StopAsync().ConfigureAwait(false); }
				catch (Exception ex) { Logger.Debug(ex, "PingSession.StopAsync"); }
			});
		}

		/// <summary>Stops any active run. Called when the application exits.</summary>
		public void Shutdown()
		{
			PingSession run = currentRun;
			if (run != null && run.IsRunning)
			{
				try { run.StopAsync().Wait(TimeSpan.FromSeconds(10)); } catch { }
			}
		}

		private void HandleSaveConfig(JToken t)
		{
			if (t == null)
			{
				BroadcastError("Invalid configuration data.");
				return;
			}
			string guid = t.Value<string>("guid");
			PingConfiguration cfg = string.IsNullOrEmpty(guid) ? null : LoadConfig(guid);
			bool isNew = cfg == null;
			// What decides which saved data is shown; when it changes, the selected configuration's data is reloaded.
			string dataIdentity = cfg == null ? null : DataIdentity(cfg);
			if (cfg == null)
			{
				cfg = new PingConfiguration();
				if (!string.IsNullOrEmpty(guid))
					cfg.guid = guid;
			}
			cfg.displayName = (t.Value<string>("displayName") ?? "").Trim();
			if (t["hosts"] is JArray hostsArray)
			{
				cfg.hosts = new List<Host>();
				foreach (JToken h in hostsArray)
				{
					string hostname = h.Type == JTokenType.String ? h.Value<string>() : h.Value<string>("hostname");
					if (!string.IsNullOrWhiteSpace(hostname))
						cfg.hosts.Add(new Host { hostname = hostname.Trim() });
				}
			}
			if (cfg.hosts == null || cfg.hosts.Count == 0)
			{
				BroadcastError("A configuration needs at least one host.");
				return;
			}
			if (string.IsNullOrWhiteSpace(cfg.displayName))
				cfg.displayName = cfg.GetHostString();
			cfg.rate = Math.Clamp(IntOrNull(t, "rate") ?? cfg.rate, 1, 100);
			cfg.pingsPerSecond = t.Value<bool?>("pingsPerSecond") ?? cfg.pingsPerSecond;
			cfg.doTraceRoute = t.Value<bool?>("doTraceRoute") ?? cfg.doTraceRoute;
			cfg.reverseDnsLookup = t.Value<bool?>("reverseDnsLookup") ?? cfg.reverseDnsLookup;
			cfg.preferIPv4 = t.Value<bool?>("preferIPv4") ?? cfg.preferIPv4;
			cfg.monitorUnresponsiveHops = t.Value<bool?>("monitorUnresponsiveHops") ?? cfg.monitorUnresponsiveHops;
			cfg.drawServerNames = t.Value<bool?>("drawServerNames") ?? cfg.drawServerNames;
			cfg.drawLastPing = t.Value<bool?>("drawLastPing") ?? cfg.drawLastPing;
			cfg.drawAverage = t.Value<bool?>("drawAverage") ?? cfg.drawAverage;
			cfg.drawJitter = t.Value<bool?>("drawJitter") ?? cfg.drawJitter;
			cfg.drawMinMax = t.Value<bool?>("drawMinMax") ?? cfg.drawMinMax;
			cfg.drawPacketLoss = t.Value<bool?>("drawPacketLoss") ?? cfg.drawPacketLoss;
			cfg.drawLimitText = t.Value<bool?>("drawLimitText") ?? cfg.drawLimitText;
			cfg.badThreshold = IntOrNull(t, "badThreshold") ?? cfg.badThreshold;
			cfg.worseThreshold = IntOrNull(t, "worseThreshold") ?? cfg.worseThreshold;
			cfg.upperLimit = IntOrNull(t, "upperLimit") ?? cfg.upperLimit;
			cfg.lowerLimit = IntOrNull(t, "lowerLimit") ?? cfg.lowerLimit;
			cfg.ScalingMethodID = IntOrNull(t, "scalingMethodID") ?? cfg.ScalingMethodID;
			cfg.logFailures = t.Value<bool?>("logFailures") ?? cfg.logFailures;
			cfg.logSuccesses = t.Value<bool?>("logSuccesses") ?? cfg.logSuccesses;
			long? retentionSeconds = t["dataRetentionSeconds"]?.Type is JTokenType.Integer or JTokenType.Float ? (long)t.Value<double>("dataRetentionSeconds") : null;
			if (retentionSeconds.HasValue)
				cfg.dataRetentionSeconds = Math.Clamp(retentionSeconds.Value, MinRetentionSeconds, MaxRetentionSeconds);
			cfg.badThreshold = Math.Max(0, cfg.badThreshold);
			cfg.worseThreshold = Math.Max(0, cfg.worseThreshold);
			cfg.lowerLimit = Math.Max(0, cfg.lowerLimit);
			if (cfg.upperLimit <= cfg.lowerLimit)
				cfg.upperLimit = cfg.lowerLimit + 1;

			PingConfigurations.SaveSingleConfiguration(cfg);
			Broadcast(BuildConfigurationsJson());
			if (cfg.guid == currentConfigGuid)
				Broadcast(BuildConfigDetailsJson(cfg));
			Broadcast(Json(new { type = "configSaved", guid = cfg.guid }));
			// A newly created configuration becomes the selected one (unless a session is running), so Start runs it.
			if (currentConfigGuid == null || (isNew && !IsBusy))
				HandleSelectConfig(cfg.guid);
			else if (!isNew && cfg.guid == currentConfigGuid && !IsBusy && dataIdentity != DataIdentity(cfg))
				BeginLoad(cfg);
		}

		public const long MinRetentionSeconds = 600;
		public const long MaxRetentionSeconds = 400L * 86400;

		private static string DataIdentity(PingConfiguration cfg)
			=> string.Join("|", PingSession.GetSessionKeys(cfg).Select(k => k.hostKey + "/" + k.mode)) + "#" + cfg.dataRetentionSeconds;

		/// <summary>Reads an integer field, accepting numbers or numeric strings; anything else (missing, empty, not a number) is null.</summary>
		private static int? IntOrNull(JToken t, string name)
		{
			JToken v = t[name];
			if (v == null) return null;
			if (v.Type == JTokenType.Integer) return (int)Math.Clamp(v.Value<long>(), int.MinValue, int.MaxValue);
			if (v.Type == JTokenType.Float)
			{
				double d = v.Value<double>();
				return double.IsFinite(d) ? (int)Math.Clamp(Math.Round(d), int.MinValue, int.MaxValue) : null;
			}
			if (v.Type == JTokenType.String && int.TryParse(v.Value<string>(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int i))
				return i;
			return null;
		}

		private void HandleDeleteConfig(string guid)
		{
			if (string.IsNullOrEmpty(guid))
			{
				BroadcastError("Invalid configuration id.");
				return;
			}
			if (IsBusy && currentConfigGuid == guid)
			{
				BroadcastError("Cannot delete the configuration that is currently running.");
				return;
			}
			try
			{
				if (!ConfigDataSet.DeleteData(settings.DataRoot, guid))
				{
					BroadcastError("This configuration is running in another PingTracer instance, so it cannot be deleted now.");
					return;
				}
			}
			catch (Exception ex)
			{
				BroadcastError("Unable to delete the configuration's saved data: " + ex.Message);
				return;
			}
			if (currentConfigGuid == guid)
			{
				lock (dataLock)
				{
					loadGeneration++;
					data = null;
					dataLoad = null;
					dataLoadGuid = null;
					loading = false;
					if (viewSource == "live")
						ClearView("live", null);
				}
			}
			PingConfigurations all = new PingConfigurations();
			all.Load();
			all.configurations.RemoveAll(c => c.guid == guid);
			all.Save();
			if (currentConfigGuid == guid)
				currentConfigGuid = null;
			Broadcast(BuildConfigurationsJson());
			Broadcast(Json(new { type = "configDeleted", guid }));
		}

		private void HandleSaveSettings(JToken t)
		{
			if (t == null) return;
			settings.pingTimeoutMs = Math.Clamp(IntOrNull(t, "pingTimeoutMs") ?? settings.pingTimeoutMs, 200, 30000);
			settings.maxHopsDefault = Math.Clamp(IntOrNull(t, "maxHops") ?? settings.maxHopsDefault, 1, 255);
			settings.pingPayloadSizeBytes = Math.Clamp(IntOrNull(t, "payloadBytes") ?? settings.pingPayloadSizeBytes, 0, 65500);
			settings.saveSessionData = t.Value<bool?>("saveSessionData") ?? settings.saveSessionData;
			settings.Save();
			Broadcast(BuildSettingsJson());
		}

		private string DataDirectory => settings.DataRoot;

		private string BuildHistoryJson()
		{
			List<PingDataFile.FileInfoSummary> files = PingDataFile.ListSessionFiles(DataDirectory, recursive: true);
			return Json(new
			{
				type = "history",
				directory = Path.GetFullPath(DataDirectory),
				files = files.Select(f => new
				{
					fileName = f.FileName,
					sizeBytes = f.SizeBytes,
					startMs = f.StartUnixMs,
					lastWriteMs = f.LastWriteUnixMs,
					endMs = f.EndUnixMs,
					displayName = f.DisplayName,
					target = f.Target,
					mode = f.Mode == SessionMode.Direct ? "direct" : "trace",
				}).ToArray(),
			});
		}

		private void HandleOpenHistory(string fileName)
		{
			if (IsBusy)
			{
				BroadcastError("Stop the current session before opening a saved one.");
				return;
			}
			string path = ResolveHistoryPath(fileName);
			if (path == null)
			{
				BroadcastError("Invalid file name.");
				return;
			}
			Task.Run(() =>
			{
				try
				{
					PingDataReader.Result result = PingDataReader.Load(path);
					ClearView("history", fileName);
					AddToView(result.Session);
					OnLog("Opened saved session " + fileName + (result.Truncated ? " (the file ends with an incomplete block, which was ignored)" : ""));
				}
				catch (Exception ex)
				{
					BroadcastError("Unable to open " + fileName + ": " + ex.Message);
				}
			});
		}

		/// <summary>Resolves a history file name (a path relative to the data directory) to a full path inside it, or null.</summary>
		private string ResolveHistoryPath(string fileName)
		{
			if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(PingDataFile.FileExtension, StringComparison.OrdinalIgnoreCase))
				return null;
			foreach (string part in fileName.Split('/', '\\'))
				if (part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
					return null;
			string root = Path.GetFullPath(DataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string full = Path.GetFullPath(Path.Combine(root, fileName));
			return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
		}

		private void HandleCloseHistory()
		{
			lock (dataLock)
			{
				if (viewSource != "history")
					return;
				ClearView("live", null);
				if (data != null)
					foreach (MonitoringSession s in data.Sessions)
						AddToView(s);
			}
		}

		#endregion

		#region JSON builders

		private static readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include };

		private static string Json(object o) => JsonConvert.SerializeObject(o, jsonSettings);

		private static PingConfiguration LoadConfig(string guid)
		{
			if (string.IsNullOrEmpty(guid)) return null;
			PingConfigurations all = new PingConfigurations();
			all.Load();
			return all.configurations.FirstOrDefault(c => c.guid == guid);
		}

		private string BuildConfigurationsJson()
		{
			PingConfigurations all = new PingConfigurations();
			all.Load();
			return Json(new
			{
				type = "configurations",
				configurations = all.configurations.Select(c => new
				{
					guid = c.guid,
					displayName = c.displayName,
					hosts = c.hosts?.Select(h => h.hostname).ToList() ?? new List<string>(),
				}).ToList(),
				selectedGuid = currentConfigGuid,
			});
		}

		private static string BuildConfigDetailsJson(PingConfiguration cfg)
		{
			return Json(new
			{
				type = "configDetails",
				config = new
				{
					guid = cfg.guid,
					displayName = cfg.displayName,
					hosts = cfg.hosts?.Select(h => h.hostname).ToList() ?? new List<string>(),
					rate = cfg.rate,
					pingsPerSecond = cfg.pingsPerSecond,
					doTraceRoute = cfg.doTraceRoute,
					reverseDnsLookup = cfg.reverseDnsLookup,
					preferIPv4 = cfg.preferIPv4,
					monitorUnresponsiveHops = cfg.monitorUnresponsiveHops,
					drawServerNames = cfg.drawServerNames,
					drawLastPing = cfg.drawLastPing,
					drawAverage = cfg.drawAverage,
					drawJitter = cfg.drawJitter,
					drawMinMax = cfg.drawMinMax,
					drawPacketLoss = cfg.drawPacketLoss,
					drawLimitText = cfg.drawLimitText,
					badThreshold = cfg.badThreshold,
					worseThreshold = cfg.worseThreshold,
					upperLimit = cfg.upperLimit,
					lowerLimit = cfg.lowerLimit,
					scalingMethodID = cfg.ScalingMethodID,
					logFailures = cfg.logFailures,
					logSuccesses = cfg.logSuccesses,
					dataRetentionSeconds = cfg.dataRetentionSeconds,
				},
			});
		}

		private string BuildStatusJson()
		{
			PingSession run = currentRun;
			return Json(new
			{
				type = "status",
				status = loading && !(run?.IsRunning ?? false) ? "Loading saved data…" : status,
				isRunning = run?.IsRunning ?? false,
				loading,
				sent = run?.SentCount ?? 0,
				lost = run?.LostCount ?? 0,
				serverTime = ServerClock.NowUnixMs,
			});
		}

		private string BuildSettingsJson()
		{
			return Json(new
			{
				type = "settings",
				pingTimeoutMs = settings.pingTimeoutMs,
				maxHops = settings.maxHopsDefault,
				payloadBytes = settings.pingPayloadSizeBytes,
				saveSessionData = settings.saveSessionData,
				dataDirectory = Path.GetFullPath(DataDirectory),
			});
		}

		#endregion

		#region Broadcasting

		private void BroadcastStatus() => Broadcast(BuildStatusJson());

		private void OnLog(string message)
		{
			// The date too: the log keeps lines from runs that last for days.
			string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  " + message;
			lock (logLock)
			{
				logBuffer.Add(line);
				if (logBuffer.Count > MaxLogLines)
					logBuffer.RemoveRange(0, logBuffer.Count - MaxLogLines);
			}
			Broadcast(Json(new { type = "log", message = line }));
		}

		private void Broadcast(string json)
		{
			foreach (ClientConnection c in clients.Values)
				c.EnqueueJson(json);
		}

		private void BroadcastError(string message) => Broadcast(Json(new { type = "error", message }));

		#endregion
	}
}
