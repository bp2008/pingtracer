using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Net.NetworkInformation;
using System.Linq;
using System.Net;

namespace PingTracer
{
	public partial class PingGraphControl : UserControl
	{
		#region Fields and Properties
		private Settings settings = new Settings();

		public Pen penSuccess = new Pen(Color.FromArgb(64, 128, 64), 1);
		public Pen penSuccessBad = new Pen(Color.FromArgb(128, 128, 0), 1);
		public Pen penSuccessWorse = new Pen(Color.FromArgb(255, 255, 0), 1);
		public Pen penFailure = new Pen(Color.FromArgb(255, 0, 0), 1);
		public Brush brushText = new SolidBrush(Color.FromArgb(255, 255, 255));
		public Color colorBackground = Color.FromArgb(0, 0, 0);
		public Brush brushBackgroundBad = new SolidBrush(Color.FromArgb(35, 35, 0));
		public Brush brushBackgroundWorse = new SolidBrush(Color.FromArgb(40, 0, 0));
		public Brush brushBackgroundTimestamps = new SolidBrush(Color.FromArgb(0, 0, 0));
		public Brush brushTimestampsText = new SolidBrush(Color.FromArgb(200, 200, 200));
		public Pen penTimestampsMark = new Pen(Color.FromArgb(128, 128, 128), 1);
		public Pen penTimestampsBorder = new Pen(Color.FromArgb(128, 128, 128), 1);
		public Font textFont = new Font(FontFamily.GenericSansSerif, 8.25f);
		/// <summary>
		/// Text that is displayed in the upper left corner of the graph.
		/// </summary>
		public string DisplayName = "";
		/// <summary>
		/// A buffer to store information for the most recent pings.
		/// </summary>
		private PingLog[] pings;
		private string MouseHintText = "";
		/// <summary>
		/// Gets the number of pings currently cached within the graph control (slow).
		/// </summary>
		public int cachedPings
		{
			get
			{
				return pings.Count(x => x != null);
			}
		}
		/// <summary>
		/// The scroll X offset as a negative number, optionally offset by 1 if the "delay most recent ping" setting is set, in order to delay rendering of the most recent ping.
		/// </summary>
		private int countOffset
		{
			get
			{
				return (settings.delayMostRecentPing ? 0 : 1) - scrollXOffset;
			}
		}
		/// <summary>
		/// The start index to read from the buffer.
		/// </summary>
		private int StartIndex
		{
			get
			{
				long currentOffset = Interlocked.Read(ref _nextIndexOffset);
				if (currentOffset == -1)
					return 0;
				return (int)(((currentOffset + countOffset) - DisplayableCount) % pings.Length);
			}
		}
		private int BufferedCount
		{
			get
			{
				long currentOffset = Interlocked.Read(ref _nextIndexOffset);
				if (currentOffset == -1)
					return 0;
				else
				{
					return (int)Math.Min(currentOffset + countOffset, pings.Length);
				}
			}
		}
		/// <summary>
		/// Gets the number of pings we have data for in the current graph viewport. (the number of pings you should interate over, relatiev to StartIndex, when painting)
		/// </summary>
		private int DisplayableCount
		{
			get
			{
				return Math.Min(BufferedCount, this.Width);
			}
		}
		/// <summary>
		/// Counter for tracking the next index in the circular buffer.
		/// </summary>
		private long _nextIndexOffset = -1;
		/// <summary>
		/// The amount of pixels the graph has been scrolled to the left. Scroll position is clamped between 0 and the <see cref="pings"/> buffer size.
		/// </summary>
		private int scrollXOffset = 0;
		/// <summary>
		/// Gets or sets the amount of pixels the graph has been scrolled to the left.  Scroll position is clamped between 0 and the <see cref="pings"/> buffer size.
		/// </summary>
		public int ScrollXOffset
		{
			get
			{
				return scrollXOffset;
			}
			set
			{
				int v = value;
				if (v > pings.Length - this.Width)
					v = pings.Length - this.Width;
				if (v < 0)
					v = 0;
				scrollXOffset = v;
				if (scrollXOffset == 0)
					setLiveAtTime = Environment.TickCount;
			}
		}
		/// <summary>
		/// Remembers the TickCount (in milliseconds) when the graph was scrolled to the live position, so we can show a message for a short time.
		/// </summary>
		private int setLiveAtTime = 0;
		#endregion
		public PingGraphControl(Settings settings, IPAddress ipAddress, string hostName, bool reverseDnsLookup)
		{
			this.settings = settings;
			pings = new PingLog[settings.cacheSize];
			this.DisplayName = ipAddress.ToString();
			if (string.IsNullOrWhiteSpace(hostName))
			{
				if (reverseDnsLookup)
					ThreadPool.QueueUserWorkItem(LookupHostname, ipAddress);
			}
			else
				ConsumeHostName(hostName);
			InitializeComponent();
		}

		private void LookupHostname(object arg)
		{
			ConsumeHostName(GetIpHostname((IPAddress)arg));
		}

		private string GetIpHostname(IPAddress ip)
		{
			try
			{
				return Dns.GetHostEntry(ip).HostName;
			}
			catch (Exception)
			{
			}
			return string.Empty;
		}

		private void ConsumeHostName(string hostName)
		{
			if (string.IsNullOrWhiteSpace(hostName))
				return;
			this.DisplayName = hostName + " [" + this.DisplayName + "]";
		}

		public void AddPingLog(PingLog pingLog)
		{
			long newOffset = Interlocked.Increment(ref _nextIndexOffset);
			pings[newOffset % pings.Length] = pingLog;
			this.Invalidate();
		}
		public void AddPingLogToSpecificOffset(long offset, PingLog pingLog)
		{
			pings[offset % pings.Length] = pingLog;
			this.Invalidate();
		}
		/// <summary>
		/// I'm not sure this function is safe to call.
		/// </summary>
		/// <param name="offset"></param>
		public void ClearSpecificOffset(long offset)
		{
			pings[offset % pings.Length] = null;
			Interlocked.Exchange(ref _nextIndexOffset, offset);
			this.Invalidate();
		}
		public long ClearNextOffset()
		{
			long newOffset = Interlocked.Increment(ref _nextIndexOffset);
			pings[newOffset % pings.Length] = null;
			this.Invalidate();
			return newOffset;
		}
		public void ClearAll()
		{
			pings = new PingLog[pings.Length];
			Interlocked.Exchange(ref _nextIndexOffset, -1);
			this.Invalidate();
		}

		int timestampsHeight = 13;
		public int TimestampsHeight
		{
			get
			{
				return timestampsHeight;
			}
		}
		private bool isInvalidatedSync = false;
		/// <summary>
		/// True if the InvalidateSync method has been called and the Paint operation has not yet been performed.
		/// </summary>
		public bool IsInvalidatedSync
		{
			get
			{
				return isInvalidatedSync;
			}
		}
		double vScale = 1;
		int min = 0, avg = 0, max = 0, last = 0, height = short.MaxValue;
		public bool AlwaysShowServerNames = false;
		public int Threshold_Bad = 100;
		public int Threshold_Worse = 100;
		public int upperLimit = 0;
		public int lowerLimit = 0;
		public int upperLimitDraw = 0;
		public int lowerLimitDraw = 0;
		public bool AutoScale = false;
		public bool AutoScaleLimit = false;
		public bool ShowLastPing = false;
		public bool ShowAverage = false;
		public bool ShowJitter = false;
		public bool ShowMinMax = false;
		public bool ShowPacketLoss = false;
		public bool ShowTimestamps = true;

		private void PingGraphControl_Paint(object sender, PaintEventArgs e)
		{
			isInvalidatedSync = false;
			e.Graphics.Clear(colorBackground);

			bool showTimestampsThisTime = ShowTimestamps;
			height = Math.Min(this.Height - (showTimestampsThisTime ? timestampsHeight : 0), short.MaxValue);

			Pen pen = penSuccess;
			int start = StartIndex;
			if (start == -1)
				return;

			int count = DisplayableCount;

			max = int.MinValue;
			min = int.MaxValue;
			int sum = 0;
			int successCount = 0;

			// Loop through pings to calculate min, max, and average values
			for (int i = 0; i < count; i++)
			{
				int idx = (start + i) % pings.Length;
				PingLog p = pings[idx];
				if (p != null && p.result == IPStatus.Success)
				{
					successCount++;
					last = p.pingTime;
					sum += last;
					max = Math.Max(max, last);
					min = Math.Min(min, last);
				}
			}

			decimal packetLoss = 0;
			if (count > 0)
				packetLoss = ((count - successCount) / (decimal)count) * 100;

			avg = sum == 0 || successCount == 0 ? 0 : (int)((double)sum / (double)successCount);
			if (min == int.MaxValue)
				min = 0;
			if (max == int.MinValue)
				max = 0;

			// Auto-scale feature based on lowest and highest ping values
			if (AutoScale)
			{
				lowerLimitDraw = AutoScaleLimit ? Math.Max(min - 1, lowerLimit) : min - 1;
				upperLimitDraw = AutoScaleLimit ? Math.Min(max + 1, upperLimit) : max + 1;
			}
			else
			{
				if (lowerLimit == 0)
				{
					lowerLimitDraw = 0;
				}
				else
                {
					lowerLimitDraw = lowerLimit;
				}
				if (upperLimit == 0) { 
					upperLimitDraw = 200;
				}
				else
                {
					upperLimitDraw = upperLimit;
				}
			}

			// Scaling for the graph
			vScale = (double)height / (upperLimitDraw - lowerLimitDraw);

			// Calculate thresholds based on the scale
			int scaledBadLine = (int)((Threshold_Bad - lowerLimitDraw) * vScale);
			int scaledWorseLine = (int)((Threshold_Worse - lowerLimitDraw) * vScale);

			// Draw background regions for thresholds
			if (scaledWorseLine < height)
				e.Graphics.FillRectangle(brushBackgroundWorse, new Rectangle(0, 0, this.Width, height - scaledWorseLine));
			if (scaledBadLine < height)
				e.Graphics.FillRectangle(brushBackgroundBad, new Rectangle(0, height - scaledWorseLine, this.Width, scaledWorseLine - scaledBadLine));

			if (showTimestampsThisTime)
				e.Graphics.DrawLine(penTimestampsBorder, new Point(0, height), new Point(this.Width - 1, height));

			Point pStart = new Point(this.Width - count, height - 1);
			Point pEnd = new Point(this.Width - count, height - 1);
			Point pTimestampMarkStart = new Point(this.Width - count, height + 1);
			Point pTimestampMarkEnd = new Point(this.Width - count, this.Height - 1);

			int lastStampedMinute = -1;
			string timelineOverlayString = "";

			for (int i = 0; i < count; i++)
			{
				try
				{
					int idx = (start + i) % pings.Length;
					PingLog p = pings[idx];
					if (p == null)
						continue;

					if (p.result == IPStatus.Success)
					{
						if (p.pingTime < Threshold_Bad)
							pen = penSuccess;
						else if (p.pingTime < Threshold_Worse)
							pen = penSuccessBad;
						else
							pen = penSuccessWorse;

						// Adjust the Y-coordinate to fit within the fixed range
						pStart.Y = (int)(height - ((p.pingTime - lowerLimitDraw) * vScale));
					}
					else
					{
						pen = penFailure;
						pStart.Y = height; // Failure is shown at the bottom (100ms)
					}

					e.Graphics.DrawLine(pen, pStart, pEnd);

					// Timestamp drawing logic (unchanged)
					if (showTimestampsThisTime)
					{
						if (lastStampedMinute == -1 || p.startTime.Minute != lastStampedMinute)
						{
							if (settings.showDateOnGraphTimeline && lastStampedMinute == -1)
								timelineOverlayString += p.startTime.ToString("yyyy-M-d ");
							if (p.startTime.Second < 2)
								e.Graphics.DrawLine(penTimestampsMark, pTimestampMarkStart, pTimestampMarkEnd);

							string stamp = p.startTime.ToString("t");
							SizeF strSize = e.Graphics.MeasureString(stamp, textFont);
							e.Graphics.FillRectangle(brushBackgroundTimestamps, new Rectangle(pTimestampMarkStart.X + 1, pTimestampMarkStart.Y, (int)strSize.Width - 1, (int)timestampsHeight - 1));
							e.Graphics.DrawString(stamp, textFont, brushTimestampsText, pTimestampMarkStart.X, pTimestampMarkStart.Y - 1);

							lastStampedMinute = p.startTime.Minute;
						}
					}
				}
				finally
				{
					pStart.X++;
					pEnd.X++;
					pTimestampMarkStart.X++;
					pTimestampMarkEnd.X++;
				}
			}

			// Overlay logic remains unchanged
			if (timelineOverlayString.Length > 0)
			{
				SizeF strSize = e.Graphics.MeasureString(timelineOverlayString, textFont);
				e.Graphics.FillRectangle(brushBackgroundTimestamps, new Rectangle(0, pTimestampMarkStart.Y, (int)strSize.Width - 1, (int)timestampsHeight - 1));
				e.Graphics.DrawString(timelineOverlayString, textFont, brushTimestampsText, 0, pTimestampMarkStart.Y - 1);
			}

			// Add lower and upper limit labels on the right side
			string lowerLimitLabel = lowerLimitDraw.ToString();
			string upperLimitLabel = upperLimitDraw.ToString();
			SizeF lowerLimitSize = e.Graphics.MeasureString(lowerLimitLabel, textFont);
			SizeF upperLimitSize = e.Graphics.MeasureString(upperLimitLabel, textFont);
			e.Graphics.DrawString(lowerLimitLabel, textFont, brushText, this.Width - lowerLimitSize.Width, height - 10);
			e.Graphics.DrawString(upperLimitLabel, textFont, brushText, this.Width - upperLimitSize.Width, 0);

			string statusStr = "";

			if (scrollXOffset != 0 && settings.warnGraphNotLive)
				statusStr += "NOT LIVE -" + scrollXOffset + ": ";
			if (ShowPacketLoss)
				statusStr += packetLoss.ToString("0.00") + "% ";

			List<int> intVals = new List<int>();
			if (ShowLastPing)
				intVals.Add(last);
			if (ShowAverage)
				intVals.Add(avg);
			if (ShowJitter)
				intVals.Add(Math.Abs(max - min));
			if (ShowMinMax)
			{
				intVals.Add(min);
				intVals.Add(max);
			}

			if (intVals.Count > 0)
				statusStr += "[" + string.Join(",", intVals) + "] ";

			if (!string.IsNullOrEmpty(MouseHintText))
			{
				if (!string.IsNullOrEmpty(DisplayName))
					statusStr += DisplayName + " ";
				statusStr += MouseHintText + " ";
			}
			else if (AlwaysShowServerNames && !string.IsNullOrEmpty(DisplayName))
				statusStr += DisplayName + " ";

			e.Graphics.DrawString(statusStr, textFont, brushText, 1, 1);
		}
  
		private void PingGraphControl_Resize(object sender, EventArgs e)
		{
			this.Invalidate();
		}

		private string GetTimestamp(DateTime time)
		{
			if (!string.IsNullOrWhiteSpace(settings.customTimeStr))
			{
				try
				{
					return time.ToString(settings.customTimeStr);
				}
				catch { }
			}
			return time.ToString("h:mm:ss tt");
		}
		/// <summary>
		/// Invalidates the control, causing a redraw, and marking the IsInvalidatedSync flag = true
		/// </summary>
		public void InvalidateSync()
		{
			isInvalidatedSync = true;
			base.Invalidate();
		}
		#region Mouseover Hint
		private void PingGraphControl_MouseMove(object sender, MouseEventArgs e)
		{
			MouseHintText = GetStatusOfPingAtPosition(e.X, e.Y);
			this.Invalidate();
		}

		private void PingGraphControl_MouseLeave(object sender, EventArgs e)
		{
			MouseHintText = "";
			this.Invalidate();
		}

		public string GetStatusOfPingAtPosition(int x, int y)
		{
			try
			{
				int offset = x - this.Width;
				int start = StartIndex + DisplayableCount;
				int i = (start + offset) % pings.Length;
				if (offset <= -pings.Length)
					return "Out of bounds, Mouse ms: " + GetScaledHeightValue(height - y);
				else if (i < 0)
					return "No Data Yet, Mouse ms: " + GetScaledHeightValue(height - y);
				PingLog pingLog = pings[i];
				if (pingLog == null)
					return "Waiting for response, Mouse ms: " + GetScaledHeightValue(height - y);
				if (pingLog.result != IPStatus.Success)
					return GetTimestamp(pingLog.startTime) + ": " + pingLog.result.ToString() + ", Mouse ms: " + GetScaledHeightValue(height - y);
				return GetTimestamp(pingLog.startTime) + ": " + pingLog.pingTime + " ms, Mouse ms: " + GetScaledHeightValue(height - y);
			}
			catch (Exception)
			{
				return "Error, Mouse ms: " + (height - y);
			}
		}
		private int GetScaledHeightValue(int height)
		{
			if (vScale == 0)
				return 0;
			return (int)(height / vScale);
		}
		#endregion
	}
}
