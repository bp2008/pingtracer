using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Net.NetworkInformation;

namespace PingTracer
{
	public partial class PingGraphControl : UserControl
	{
		#region Fields and Properties
		public Settings settings = new Settings();

		public Pen penSuccess = new Pen(Color.FromArgb(64, 128, 64), 1);
		public Pen penSuccessBad = new Pen(Color.FromArgb(128, 128, 0), 1);
		public Pen penSuccessWorse = new Pen(Color.FromArgb(255, 255, 0), 1);
		public Pen penFailure = new Pen(Color.FromArgb(255, 0, 0), 1);
		public Brush brushText = new SolidBrush(Color.FromArgb(255, 255, 255));
		public Color colorBackground = Color.FromArgb(0, 0, 0);
		public Brush brushBackgroundBad = new SolidBrush(Color.FromArgb(35, 35, 0));
		public Brush brushBackgroundWorse = new SolidBrush(Color.FromArgb(40, 0, 0));
		/// <summary>
		/// Text that is displayed in the upper left corner of the graph.
		/// </summary>
		public string DisplayName = "";
		/// <summary>
		/// A buffer to store information for the most recent pings.
		/// </summary>
		private PingLog[] pings = new PingLog[10000];
		private string MouseHintText = "";
		private int countOffset
		{
			get
			{
				return settings.delayMostRecentPing ? 0 : 1;
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
					return (int)Math.Min(currentOffset + countOffset, pings.Length);
			}
		}
		private int DisplayableCount
		{
			get
			{
				return Math.Min(BufferedCount, this.Width);
			}
		}
		private long _nextIndexOffset = -1;
		private int NextIndex
		{
			get
			{
				return (int)((Interlocked.Increment(ref _nextIndexOffset)) % pings.Length);
			}
		}
		#endregion
		public PingGraphControl()
		{
			InitializeComponent();
		}
		public void AddPingLog(PingLog pingLog)
		{
			pings[NextIndex] = pingLog;
			this.Invalidate();
		}
		public void AddPingLogToSpecificOffset(long offset, PingLog pingLog)
		{
			pings[offset % pings.Length] = pingLog;
			this.Invalidate();
		}
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

		double vScale = 1;
		int min = 0, avg = 0, max = 0, last = 0;
		public bool AlwaysShowServerNames = false;
		public int Threshold_Bad = 100;
		public int Threshold_Worse = 100;
		public bool ShowMinMax = false;
		public bool ShowPacketLoss = false;

		private void PingGraphControl_Paint(object sender, PaintEventArgs e)
		{
			e.Graphics.Clear(colorBackground);
			int height = Math.Min(this.Height, short.MaxValue);
			Pen pen = penSuccess;
			int start = StartIndex;
			if (start == -1)
				return;
			int count = DisplayableCount;

			max = int.MinValue;
			min = int.MaxValue;
			int sum = 0;
			int successCount = 0;
			for (int i = 0; i < count; i++)
			{
				int idx = (start + i) % pings.Length;
				if (pings[idx] != null && pings[idx].result == IPStatus.Success)
				{
					successCount++;
					last = pings[idx].pingTime;
					sum += last;
					max = Math.Max(max, last);
					min = Math.Min(min, last);
				}
			}
			decimal packetLoss = 0;
			if(count > 0)
				packetLoss = ((count - successCount) / (decimal)count) * 100;
			avg = sum == 0 || count == 0 ? 0 : (int)((double)sum / (double)count);
			if (min == int.MaxValue)
				min = 0;
			if (max == int.MinValue)
				max = 0;
			if (max > this.Height)
			{
				// max value is too high to draw in the box.
				int maxForScaling = Math.Min((int)(max * 1.1), (int)(Threshold_Worse * 1.5));
				maxForScaling = Math.Max(maxForScaling, this.Height);
				vScale = (double)this.Height / (double)maxForScaling;
			}
			else
				vScale = 1f;
			int scaledBadLine = (int)(vScale * Threshold_Bad);
			int scaledWorseLine = (int)(vScale * Threshold_Worse);
			if (scaledWorseLine < this.Height)
				e.Graphics.FillRectangle(brushBackgroundWorse, new Rectangle(0, 0, this.Width, this.Height - scaledWorseLine));
			if (scaledBadLine < this.Height)
				e.Graphics.FillRectangle(brushBackgroundBad, new Rectangle(0, this.Height - scaledWorseLine, this.Width, scaledWorseLine - scaledBadLine));
			Point pStart = new Point(this.Width - count, height);
			Point pEnd = new Point(this.Width - count, height);
			for (int i = 0; i < count; i++)
			{
				try
				{
					int idx = (start + i) % pings.Length;
					if (pings[idx] == null)
						continue;
					if (pings[idx].result == System.Net.NetworkInformation.IPStatus.Success)
					{
						if (pings[idx].pingTime < Threshold_Bad)
							pen = penSuccess;
						else if (pings[idx].pingTime < Threshold_Worse)
							pen = penSuccessBad;
						else
							pen = penSuccessWorse;
						pStart.Y = (int)(height - (pings[idx].pingTime * vScale));
					}
					else
					{
						pen = penFailure;
						pStart.Y = 0;
					}

					e.Graphics.DrawLine(pen, pStart, pEnd);
				}
				finally
				{
					pStart.X++;
					pEnd.X++;
				}
			}
			string statusStr = "";
			if(ShowPacketLoss)
				statusStr += packetLoss.ToString("0.00") + "% ";
			if (ShowMinMax)
				statusStr += "[" + min + "," + max + "," + avg + "," + last + "]";
			else
				statusStr += "[" + avg + "," + last + "]";
			if (!string.IsNullOrEmpty(MouseHintText))
			{
				if (!string.IsNullOrEmpty(DisplayName))
					statusStr += " " + DisplayName;
				statusStr += " " + MouseHintText;
			}
			else if (AlwaysShowServerNames && !string.IsNullOrEmpty(DisplayName))
				statusStr += " " + DisplayName;

			e.Graphics.DrawString(statusStr, this.Font, brushText, 1, 1);
			//SizeF measuredSize = e.Graphics.MeasureString(statusStr, this.Font);
			//e.Graphics.DrawString(statusStr, this.Font, brushText, (this.Width - measuredSize.Width) - 15, 1);
		}

		private void PingGraphControl_Resize(object sender, EventArgs e)
		{
			this.Invalidate();
		}

		private string GetTimestamp(DateTime time)
		{
			return time.ToString("h:mm:ss tt");
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
				if (i < 0)
					return "No Data Yet, Mouse ms: " + GetScaledHeightValue(this.Height - y);
				PingLog pingLog = pings[i];
				if (pingLog == null)
					return "Waiting for response, Mouse ms: " + GetScaledHeightValue(this.Height - y);
				if (pingLog.result != System.Net.NetworkInformation.IPStatus.Success)
					return GetTimestamp(pingLog.startTime) + ": " + pingLog.result.ToString() + ", Mouse ms: " + GetScaledHeightValue(this.Height - y);
				return GetTimestamp(pingLog.startTime) + ": " + pingLog.pingTime + " ms, Mouse ms: " + GetScaledHeightValue(this.Height - y);
			}
			catch (Exception)
			{
				return "Error, Mouse ms: " + (this.Height - y);
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
