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
		/// <summary>
		/// If the center of the form is not visible on any of the screens, moves the form to the screen whose center is closest to the form in its old position.
		/// </summary>
		/// <param name="form">The form to move.</param>
		public static void MoveOnscreenIfOffscreen(this Form form)
		{
			// Check if the center of the form is visible on any of the screens
			bool formIsVisible = false;
			Point formCenter = new Point(form.Left + form.Width / 2, form.Top + form.Height / 2);
			foreach (Screen screen in Screen.AllScreens)
			{
				if (screen.WorkingArea.Contains(formCenter))
				{
					formIsVisible = true;
					break;
				}
			}

			// If the center of the form is not visible, move it to the screen whose center is closest to the form in its old position
			if (!formIsVisible)
			{
				Screen closestScreen = Screen.AllScreens[0];
				double minDistance = double.MaxValue;
				foreach (Screen screen in Screen.AllScreens)
				{
					Point screenCenter = new Point(screen.WorkingArea.Left + screen.WorkingArea.Width / 2, screen.WorkingArea.Top + screen.WorkingArea.Height / 2);
					double distance = Math.Sqrt(Math.Pow(screenCenter.X - formCenter.X, 2) + Math.Pow(screenCenter.Y - formCenter.Y, 2));
					if (distance < minDistance)
					{
						minDistance = distance;
						closestScreen = screen;
					}
				}
				int x = closestScreen.WorkingArea.Left + (closestScreen.WorkingArea.Width - form.Width) / 2;
				int y = closestScreen.WorkingArea.Top + (closestScreen.WorkingArea.Height - form.Height) / 2;
				form.Location = new Point(x, y);
			}
		}
	}
}
