namespace PingTracer
{
	/// <summary>
	/// Ping graph scaling methods.
	/// </summary>
	public enum GraphScalingMethod
	{
		/// <summary>
		/// The Classic method prefers to treat 1 pixel as 1 millisecond, but can zoom out if needed to show higher response time values correctly.
		/// </summary>
		Classic = 0,
		/// <summary>
		/// The Zoom Unlimited method zooms in or out to closely fit the data but will not zoom out beyond the user's specified limits, possibly causing clipping.
		/// </summary>
		Zoom = 1,
		/// <summary>
		/// The Zoom Unlimited method zooms in or out to closely fit the data.
		/// </summary>
		Zoom_Unlimited = 2,
		/// <summary>
		/// The Fixed method shows exactly the user-specified response time range and does not zoom to fit the data.
		/// </summary>
		Fixed = 3,
	}
}