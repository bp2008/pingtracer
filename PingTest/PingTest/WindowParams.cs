namespace PingTracer
{
	public class WindowParams
	{
		/// <summary>
		/// X coordinate
		/// </summary>
		public int X;
		/// <summary>
		/// Y coordinate
		/// </summary>
		public int Y;
		/// <summary>
		/// Width, ignore if less than 1.
		/// </summary>
		public int W;
		/// <summary>
		/// Height, ignore if less than 1.
		/// </summary>
		public int H;
		public WindowParams() { }

		public WindowParams(int x, int y, int w, int h)
		{
			X = x;
			Y = y;
			W = w;
			H = h;
		}
		public override string ToString()
		{
			return X + "," + Y + "," + W + "," + H;
		}
	}
}