using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PingTracer
{
	public static class Extensions
	{
		/// <summary>
		/// Sets the location of the form to be near the mouse pointer, preferably not directly on top of the mouse pointer, but entirely on-screen if possible.
		/// </summary>
		/// <param name="form">The form.</param>
		public static void SetLocationNearMouse(this Form form)
		{
			int offset = 10;
			int x = 0, y = 0;
			Point cursor = Cursor.Position;
			Screen screen = Screen.FromPoint(cursor);
			Rectangle workspace = screen.WorkingArea;
			Point centerScreen = new Point(workspace.X + (workspace.Width / 2), workspace.Y + (workspace.Height / 2));

			// Position the form near the cursor, extending away from the cursor toward the center of the screen.
			if (cursor.X <= centerScreen.X)
			{
				if (cursor.Y <= centerScreen.Y)
				{
					// Upper-left quadrant
					x = cursor.X + offset;
					y = cursor.Y + offset;
				}
				else
				{
					// Lower-left quadrant
					x = cursor.X + offset;
					y = cursor.Y - offset - form.Height;
				}
			}
			else
			{
				if (cursor.Y <= centerScreen.Y)
				{
					// Upper-right quadrant
					x = cursor.X - offset - form.Width;
					y = cursor.Y + offset;
				}
				else
				{
					// Lower-right quadrant
					x = cursor.X - offset - form.Width;
					y = cursor.Y - offset - form.Height;
				}
			}

			// Screen bounds check.  Keep form entirely within this screen if possible, but ensure that the top left corner is visible if all else fails.
			if (x >= workspace.X + (workspace.Width - form.Width))
				x = workspace.X + (workspace.Width - form.Width);
			if (x < workspace.X)
				x = workspace.X;
			if (y >= workspace.Y + (workspace.Height - form.Height))
				y = workspace.Y + (workspace.Height - form.Height);
			if (y < workspace.Y)
				y = workspace.Y;

			// Assign location
			form.StartPosition = FormStartPosition.Manual;
			form.Location = new Point(x, y);
		}
	}
}
