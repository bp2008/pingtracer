using System.Collections.Generic;

namespace PingTracer.Storage
{
	/// <summary>
	/// Chronological list of all HopTimeSeries entries for a single hop position.
	/// New series are appended when the responding address changes; closed series
	/// are never reopened. Thread safety: callers must lock on this instance when
	/// accessing Series in a multi-threaded context.
	/// </summary>
	public class HopHistory
	{
		public readonly byte HopNumber;

		public readonly List<HopTimeSeries> Series = new List<HopTimeSeries>();

		/// <summary>
		/// The currently active series for this hop, or null if none is active.
		/// </summary>
		public HopTimeSeries ActiveSeries =>
			Series.Count > 0 && Series[Series.Count - 1].IsActive
				? Series[Series.Count - 1]
				: null;

		public HopHistory(byte hopNumber)
		{
			HopNumber = hopNumber;
		}
	}
}
