using PingTracer.Tracer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PingTracer
{
	/// <summary>
	/// Specifies how the application will behave upon startup.
	/// </summary>
	public partial class StartupOptions
	{
		/// <summary>
		/// If not null, pings will start to the stored configuration with this display name or host field value upon application startup.
		/// </summary>
		public string StartupHostName = null;
		/// <summary>
		/// If true, the StartupHostName argument will prefer to match a stored configuration that is configured to prefer IPv6.  If false, then IPv4 will be preferred.
		/// </summary>
		public bool PreferIPv6 = false;
		/// <summary>
		/// If not null, the window will be positioned here upon application startup.  Width or Height values less than 1 will be ignored.
		/// </summary>
		public WindowParams WindowLocation = null;
		/// <summary>
		/// If true, pings will be started automatically upon application startup.
		/// </summary>
		public bool StartPinging;
		/// <summary>
		/// If true, the graphs will be maximized automatically upon application startup.
		/// </summary>
		public bool MaximizeGraphs;

		/// <summary>
		/// Constructs an empty StartupOptions.
		/// </summary>
		public StartupOptions() { }
		/// <summary>
		/// Constructs a StartupOptions from an args string.
		/// </summary>
		/// <param name="args"></param>
		public StartupOptions(string[] args)
		{
			HashSet<string> flagKeysWithNoValue = new HashSet<string>(new string[] { "-s", "-m", "-6" });
			Dictionary<string, string> flags = new Dictionary<string, string>();
			string key = null;
			for (int i = 0; i < args.Length; i++)
			{
				string arg = args[i];
				if (key != null)
				{
					flags[key] = arg;
					key = null;
				}
				else if (flagKeysWithNoValue.Contains(arg))
					flags[arg] = arg;
				else
					key = arg;
			}
			if (flags.TryGetValue("-h", out string startupHostName))
				this.StartupHostName = startupHostName;
			if (flags.TryGetValue("-l", out string startupLocation))
			{
				try
				{
					string[] parts = startupLocation.Split(' ', ',', '.');
					int[] ints = parts.Select(int.Parse).ToArray();
					if (ints.Length == 2)
						this.WindowLocation = new WindowParams(ints[0], ints[1], 0, 0);
					else if (ints.Length == 3)
						this.WindowLocation = new WindowParams(ints[0], ints[1], ints[2], 0);
					else if (ints.Length >= 4)
						this.WindowLocation = new WindowParams(ints[0], ints[1], ints[2], ints[3]);
				}
				catch (Exception ex)
				{
					MessageBox.Show("Unable to load window startup position: \"" + startupLocation + "\" because of error: " + ex.Message);
				}
			}
			if (flags.ContainsKey("-s"))
				this.StartPinging = true;
			if (flags.ContainsKey("-m"))
				this.MaximizeGraphs = true;
			if (flags.ContainsKey("-6"))
				this.PreferIPv6 = true;
		}

		/// <summary>
		/// Returns the command line text that would produce this StartupOptions.
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();
			string[] args = GetArgs();
			for (int i = 0; i < args.Length; i++)
			{
				sb.Append(EscapeCommandLineArgument(args[i], args[i].Contains(' ')));
				if (i + 1 < args.Length)
					sb.Append(' ');
			}
			return sb.ToString();
		}

		private string[] GetArgs()
		{
			List<string> args = new List<string>();
			if (StartupHostName != null)
			{
				args.Add("-h");
				args.Add(StartupHostName);
			}
			if (PreferIPv6)
				args.Add("-6");
			if (WindowLocation != null)
			{
				args.Add("-l");
				args.Add(WindowLocation.X + "," + WindowLocation.Y + "," + WindowLocation.W + "," + WindowLocation.H);
			}
			if (StartPinging)
				args.Add("-s");
			if (MaximizeGraphs)
				args.Add("-m");
			return args.ToArray();
		}

		/// <summary>
		/// Escapes backslashes and double-quotation marks by prepending backslashes.
		/// </summary>
		/// <param name="str">Unescaped string.</param>
		/// <param name="wrapInDoubleQuotes">If true, the return value will be wrapped in double quotes.</param>
		/// <returns>A string suitable to be used as a command line argument.</returns>
		private static string EscapeCommandLineArgument(string str, bool wrapInDoubleQuotes = false)
		{
			string dqWrap = wrapInDoubleQuotes ? "\"" : "";
			return dqWrap + str.Replace("\\", "\\\\").Replace("\"", "\\\"") + dqWrap;
		}
	}
}