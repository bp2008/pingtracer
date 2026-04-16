using BPUtil;
using BPUtil.SimpleHttp;
using BPUtil.SimpleHttp.WebSockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PingTracer.Tracer;
using SmartPing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PingTracer.Services
{
	/// <summary>
	/// Manages WebSocket connections for streaming ping data to web clients.
	/// </summary>
	public class PingWebSocketHandler
	{
		private readonly ConcurrentDictionary<string, ClientConnection> clients = new ConcurrentDictionary<string, ClientConnection>();

		/// <summary>
		/// The current active session, if any.
		/// </summary>
		private PingSession currentSession;

		/// <summary>
		/// The settings instance shared across the app.
		/// </summary>
		private readonly Settings settings;

		/// <summary>
		/// Lock for session start/stop operations.
		/// </summary>
		private readonly object sessionLock = new object();

		/// <summary>
		/// Status text.
		/// </summary>
		private string currentStatus = "Idle";

		/// <summary>
		/// The current configuration GUID.
		/// </summary>
		private string currentConfigGuid = null;

		/// <summary>
		/// Log buffer.
		/// </summary>
		private readonly List<string> logBuffer = new List<string>();
		private readonly object logLock = new object();
		private const int MaxLogLines = 1000;

		public PingWebSocketHandler(Settings settings)
		{
			this.settings = settings;
		}

		/// <summary>
		/// Handles a WebSocket connection from a web client.
		/// Called from WebServer when a WebSocket upgrade is detected on the /ws path.
		/// </summary>
		public void HandleConnection(HttpProcessor p)
		{
			string clientId = Guid.NewGuid().ToString();
			ClientConnection client = null;

			try
			{
				WebSocket ws = new WebSocket(p,
					onMessageReceived: (frame) =>
					{
						if (frame is WebSocketTextFrame textFrame)
							HandleClientMessage(clientId, textFrame.Text);
					},
					onClose: (closeFrame) =>
					{
						RemoveClient(clientId);
					});

				// Extend timeouts for long-lived WebSocket connections
				ws.ReceiveTimeout = 300000; // 5 minutes
				ws.SendTimeout = 30000;     // 30 seconds

				client = new ClientConnection(clientId, ws);
				clients[clientId] = client;

				// Send initial state
				SendInitialState(client);

				// Keep the connection alive - the read thread in WebSocket handles this
				// We need to block here so the HttpProcessor doesn't close the connection
				client.WaitForClose();
			}
			catch (Exception ex)
			{
				if (!HttpProcessor.IsOrdinaryDisconnectException(ex))
					Logger.Debug(ex, "PingWebSocketHandler");
			}
			finally
			{
				RemoveClient(clientId);
			}
		}

		private void RemoveClient(string clientId)
		{
			clients.TryRemove(clientId, out ClientConnection removed);
			removed?.SignalClose();
		}

		private void SendInitialState(ClientConnection client)
		{
			// Send configurations list
			PingConfigurations allConfigs = new PingConfigurations();
			allConfigs.Load();

			var configList = allConfigs.configurations.Select(c => new
			{
				guid = c.guid,
				displayName = c.displayName,
				hosts = c.hosts?.Select(h => h.hostname).ToList() ?? new List<string>()
			}).ToList();

			client.Send(JsonConvert.SerializeObject(new
			{
				type = "configurations",
				configurations = configList,
				selectedGuid = currentConfigGuid
			}));

			// Send current status
			client.Send(JsonConvert.SerializeObject(new
			{
				type = "status",
				status = currentStatus,
				isRunning = currentSession?.IsRunning ?? false,
				successfulPings = currentSession?.SuccessfulPings ?? 0,
				failedPings = currentSession?.FailedPings ?? 0,
				cacheSize = settings.cacheSize
			}));

			// Send current config details if one is selected
			if (currentConfigGuid != null)
			{
				PingConfiguration cfg = allConfigs.configurations.FirstOrDefault(c => c.guid == currentConfigGuid);
				if (cfg != null)
					SendConfigDetails(client, cfg);
			}

			// Send existing targets and their data if a session is active
			if (currentSession != null)
			{
				var sessionTargets = currentSession.GetTargets();
				foreach (PingTarget target in sessionTargets)
				{
					client.Send(JsonConvert.SerializeObject(new
					{
						type = "targetAdded",
						id = target.Id,
						displayName = target.DisplayName,
						address = target.Address.ToString(),
						cacheSize = target.CacheSize
					}));

					// Send recent ping data for this target (send last N pings)
					SendBulkPingData(client, target);
				}
			}
		}

		private void SendBulkPingData(ClientConnection client, PingTarget target)
		{
			// Send ping data in chunks to avoid overwhelming the WebSocket
			long total = target.TotalPingsRecorded;
			if (total <= 0) return;

			int toSend = (int)Math.Min(total, 3600); // Send up to 1 hour of data at 1/sec
			PingLog[] pings = target.GetRecentPings(toSend);

			// Encode as compact binary-style JSON array for efficiency
			var pingData = new List<object>(pings.Length);
			foreach (PingLog p in pings)
			{
				if (p == null)
					pingData.Add(null);
				else
					pingData.Add(new { t = p.startTime.ToUnixTimeMs(), ms = p.pingTime, s = (int)p.result });
			}

			client.Send(JsonConvert.SerializeObject(new
			{
				type = "pingBulk",
				targetId = target.Id,
				pings = pingData
			}));
		}

		private void SendConfigDetails(ClientConnection client, PingConfiguration cfg)
		{
			client.Send(JsonConvert.SerializeObject(new
			{
				type = "configDetails",
				config = new
				{
					guid = cfg.guid,
					displayName = cfg.displayName,
					hosts = cfg.hosts?.Select(h => h.hostname).ToList(),
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
				}
			}));
		}

		private void HandleClientMessage(string clientId, string message)
		{
			try
			{
				JObject msg = JObject.Parse(message);
				string action = msg.Value<string>("action");

				switch (action)
				{
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
						HandleGetConfigurations(clientId);
						break;
					case "saveConfig":
						HandleSaveConfig(msg["config"]);
						break;
					case "deleteConfig":
						HandleDeleteConfig(msg.Value<string>("guid"));
						break;
					case "setPingRate":
						HandleSetPingRate(msg.Value<int>("rate"), msg.Value<bool>("pingsPerSecond"));
						break;
					case "requestPingData":
						HandleRequestPingData(clientId, msg.Value<int>("targetId"), msg.Value<int>("count"), msg.Value<int>("offset"));
						break;
				}
			}
			catch (Exception ex)
			{
				if (clients.TryGetValue(clientId, out ClientConnection client))
				{
					client.Send(JsonConvert.SerializeObject(new
					{
						type = "error",
						message = ex.Message
					}));
				}
			}
		}

		private void HandleSelectConfig(string guid)
		{
			lock (sessionLock)
			{
				if (currentSession?.IsRunning == true)
				{
					BroadcastError("Cannot change configuration while pings are running.");
					return;
				}

				PingConfigurations allConfigs = new PingConfigurations();
				allConfigs.Load();
				PingConfiguration cfg = allConfigs.configurations.FirstOrDefault(c => c.guid == guid);
				if (cfg == null)
				{
					BroadcastError("Configuration not found.");
					return;
				}

				currentConfigGuid = guid;
				settings.lastLoadedConfigurationGuid = guid;
				settings.Save();

				Broadcast(JsonConvert.SerializeObject(new
				{
					type = "configSelected",
					guid = guid
				}));

				foreach (var client in clients.Values)
					SendConfigDetails(client, cfg);
			}
		}

		private void HandleStart()
		{
			lock (sessionLock)
			{
				if (currentSession?.IsRunning == true) return;

				if (currentConfigGuid == null)
				{
					BroadcastError("No configuration selected.");
					return;
				}

				PingConfigurations allConfigs = new PingConfigurations();
				allConfigs.Load();
				PingConfiguration cfg = allConfigs.configurations.FirstOrDefault(c => c.guid == currentConfigGuid);
				if (cfg == null)
				{
					BroadcastError("Configuration not found.");
					return;
				}

				currentSession = new PingSession(cfg, settings);
				currentSession.TargetAdded += OnTargetAdded;
				currentSession.TargetRemoved += OnTargetRemoved;
				currentSession.PingResultReceived += OnPingResult;
				currentSession.StatusChanged += OnStatusChanged;
				currentSession.LogCreated += OnLogCreated;
				currentSession.SessionStopped += OnSessionStopped;
				currentSession.Start();

				Broadcast(JsonConvert.SerializeObject(new
				{
					type = "started",
					isRunning = true
				}));
			}
		}

		private void HandleStop()
		{
			lock (sessionLock)
			{
				if (currentSession == null || !currentSession.IsRunning) return;
				currentSession.Stop();
			}
		}

		private void HandleSetPingRate(int rate, bool pingsPerSecond)
		{
			lock (sessionLock)
			{
				currentSession?.SetPingDelay(rate, pingsPerSecond);
			}
		}

		private void HandleGetConfigurations(string clientId)
		{
			PingConfigurations allConfigs = new PingConfigurations();
			allConfigs.Load();

			var configList = allConfigs.configurations.Select(c => new
			{
				guid = c.guid,
				displayName = c.displayName,
				hosts = c.hosts?.Select(h => h.hostname).ToList() ?? new List<string>()
			}).ToList();

			if (clients.TryGetValue(clientId, out ClientConnection client))
			{
				client.Send(JsonConvert.SerializeObject(new
				{
					type = "configurations",
					configurations = configList,
					selectedGuid = currentConfigGuid
				}));
			}
		}

		private void HandleSaveConfig(JToken configToken)
		{
			if (configToken == null)
			{
				BroadcastError("Invalid config data.");
				return;
			}

			PingConfigurations allConfigs = new PingConfigurations();
			allConfigs.Load();

			string guid = configToken.Value<string>("guid");
			bool isNew = string.IsNullOrEmpty(guid);

			PingConfiguration cfg;
			if (isNew)
			{
				cfg = new PingConfiguration();
			}
			else
			{
				cfg = allConfigs.configurations.FirstOrDefault(c => c.guid == guid);
				if (cfg == null)
				{
					cfg = new PingConfiguration();
					cfg.guid = guid;
				}
			}

			cfg.displayName = configToken.Value<string>("displayName") ?? "";
			var hostsArray = configToken["hosts"];
			if (hostsArray != null)
			{
				cfg.hosts = new List<Host>();
				foreach (var h in hostsArray)
				{
					string hostname = h.Value<string>();
					if (!string.IsNullOrWhiteSpace(hostname))
						cfg.hosts.Add(new Host { hostname = hostname.Trim() });
				}
			}

			cfg.rate = configToken.Value<int?>("rate") ?? cfg.rate;
			cfg.pingsPerSecond = configToken.Value<bool?>("pingsPerSecond") ?? cfg.pingsPerSecond;
			cfg.doTraceRoute = configToken.Value<bool?>("doTraceRoute") ?? cfg.doTraceRoute;
			cfg.reverseDnsLookup = configToken.Value<bool?>("reverseDnsLookup") ?? cfg.reverseDnsLookup;
			cfg.preferIPv4 = configToken.Value<bool?>("preferIPv4") ?? cfg.preferIPv4;
			cfg.monitorUnresponsiveHops = configToken.Value<bool?>("monitorUnresponsiveHops") ?? cfg.monitorUnresponsiveHops;
			cfg.drawServerNames = configToken.Value<bool?>("drawServerNames") ?? cfg.drawServerNames;
			cfg.drawLastPing = configToken.Value<bool?>("drawLastPing") ?? cfg.drawLastPing;
			cfg.drawAverage = configToken.Value<bool?>("drawAverage") ?? cfg.drawAverage;
			cfg.drawJitter = configToken.Value<bool?>("drawJitter") ?? cfg.drawJitter;
			cfg.drawMinMax = configToken.Value<bool?>("drawMinMax") ?? cfg.drawMinMax;
			cfg.drawPacketLoss = configToken.Value<bool?>("drawPacketLoss") ?? cfg.drawPacketLoss;
			cfg.drawLimitText = configToken.Value<bool?>("drawLimitText") ?? cfg.drawLimitText;
			cfg.badThreshold = configToken.Value<int?>("badThreshold") ?? cfg.badThreshold;
			cfg.worseThreshold = configToken.Value<int?>("worseThreshold") ?? cfg.worseThreshold;
			cfg.upperLimit = configToken.Value<int?>("upperLimit") ?? cfg.upperLimit;
			cfg.lowerLimit = configToken.Value<int?>("lowerLimit") ?? cfg.lowerLimit;
			cfg.ScalingMethodID = configToken.Value<int?>("scalingMethodID") ?? cfg.ScalingMethodID;
			cfg.logFailures = configToken.Value<bool?>("logFailures") ?? cfg.logFailures;
			cfg.logSuccesses = configToken.Value<bool?>("logSuccesses") ?? cfg.logSuccesses;

			PingConfigurations.SaveSingleConfiguration(cfg);

			// Refresh and broadcast updated configurations list
			HandleGetConfigurations_Broadcast();

			// Send updated config details so clients reflect changes
			foreach (var client in clients.Values)
				SendConfigDetails(client, cfg);

			Broadcast(JsonConvert.SerializeObject(new
			{
				type = "configSaved",
				guid = cfg.guid
			}));
		}

		private void HandleDeleteConfig(string guid)
		{
			if (string.IsNullOrEmpty(guid))
			{
				BroadcastError("Invalid configuration GUID.");
				return;
			}

			if (currentSession?.IsRunning == true && currentConfigGuid == guid)
			{
				BroadcastError("Cannot delete the currently running configuration.");
				return;
			}

			PingConfigurations allConfigs = new PingConfigurations();
			allConfigs.Load();
			allConfigs.configurations.RemoveAll(c => c.guid == guid);
			allConfigs.Save();

			if (currentConfigGuid == guid)
				currentConfigGuid = null;

			HandleGetConfigurations_Broadcast();

			Broadcast(JsonConvert.SerializeObject(new
			{
				type = "configDeleted",
				guid = guid
			}));
		}

		private void HandleGetConfigurations_Broadcast()
		{
			PingConfigurations allConfigs = new PingConfigurations();
			allConfigs.Load();

			var configList = allConfigs.configurations.Select(c => new
			{
				guid = c.guid,
				displayName = c.displayName,
				hosts = c.hosts?.Select(h => h.hostname).ToList() ?? new List<string>()
			}).ToList();

			Broadcast(JsonConvert.SerializeObject(new
			{
				type = "configurations",
				configurations = configList,
				selectedGuid = currentConfigGuid
			}));
		}

		private void HandleRequestPingData(string clientId, int targetId, int count, int offset)
		{
			if (currentSession == null) return;
			PingTarget target = currentSession.GetTarget(targetId);
			if (target == null) return;

			PingLog[] pings = target.GetRecentPings(count, offset);
			var pingData = new List<object>(pings.Length);
			foreach (PingLog p in pings)
			{
				if (p == null)
					pingData.Add(null);
				else
					pingData.Add(new { t = p.startTime.ToUnixTimeMs(), ms = p.pingTime, s = (int)p.result });
			}

			if (clients.TryGetValue(clientId, out ClientConnection client))
			{
				client.Send(JsonConvert.SerializeObject(new
				{
					type = "pingBulk",
					targetId = targetId,
					pings = pingData
				}));
			}
		}

		#region Session Event Handlers

		private void OnTargetAdded(PingTarget target)
		{
			Broadcast(JsonConvert.SerializeObject(new
			{
				type = "targetAdded",
				id = target.Id,
				displayName = target.DisplayName,
				address = target.Address.ToString(),
				cacheSize = target.CacheSize
			}));
		}

		private void OnTargetRemoved(int targetId)
		{
			Broadcast(JsonConvert.SerializeObject(new
			{
				type = "targetRemoved",
				id = targetId
			}));
		}

		private void OnPingResult(int targetId, PingLog log)
		{
			Broadcast(JsonConvert.SerializeObject(new
			{
				type = "ping",
				targetId = targetId,
				t = log.startTime.ToUnixTimeMs(),
				ms = log.pingTime,
				s = (int)log.result
			}));
		}

		private void OnStatusChanged(string status)
		{
			currentStatus = status;
			Broadcast(JsonConvert.SerializeObject(new
			{
				type = "status",
				status = status,
				isRunning = currentSession?.IsRunning ?? false,
				successfulPings = currentSession?.SuccessfulPings ?? 0,
				failedPings = currentSession?.FailedPings ?? 0
			}));
		}

		private void OnLogCreated(string logEntry)
		{
			lock (logLock)
			{
				logBuffer.Add(logEntry);
				while (logBuffer.Count > MaxLogLines)
					logBuffer.RemoveAt(0);
			}
			Broadcast(JsonConvert.SerializeObject(new
			{
				type = "log",
				message = logEntry
			}));
		}

		private void OnSessionStopped()
		{
			Broadcast(JsonConvert.SerializeObject(new
			{
				type = "stopped",
				isRunning = false
			}));
		}

		#endregion

		#region Broadcasting

		private void Broadcast(string message)
		{
			foreach (var client in clients.Values)
			{
				try
				{
					client.Send(message);
				}
				catch (Exception) { }
			}
		}

		private void BroadcastError(string errorMessage)
		{
			Broadcast(JsonConvert.SerializeObject(new
			{
				type = "error",
				message = errorMessage
			}));
		}

		#endregion
	}

	/// <summary>
	/// Represents a connected WebSocket client.
	/// </summary>
	internal class ClientConnection
	{
		public string Id { get; }
		private readonly WebSocket ws;
		private readonly ManualResetEventSlim closeEvent = new ManualResetEventSlim(false);
		private readonly object sendLock = new object();

		public ClientConnection(string id, WebSocket ws)
		{
			Id = id;
			this.ws = ws;
		}

		public void Send(string message)
		{
			lock (sendLock)
			{
				if (ws.State == WebSocketState.Open)
					ws.Send(message);
			}
		}

		public void WaitForClose()
		{
			closeEvent.Wait();
		}

		public void SignalClose()
		{
			closeEvent.Set();
		}
	}

	/// <summary>
	/// Extension methods for DateTime to support Unix timestamp conversion.
	/// </summary>
	public static class DateTimeExtensions
	{
		private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		/// <summary>
		/// Converts a DateTime to Unix time in milliseconds.
		/// </summary>
		public static long ToUnixTimeMs(this DateTime dt)
		{
			return (long)(dt.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
		}
	}
}
