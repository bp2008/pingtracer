using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections.Concurrent;
using System.Threading;

namespace PingTracer
{
	/// <summary>
	/// <para>Any class inheriting from this may be loaded and saved from file easily. Uses <see cref="System.Xml.Serialization.XmlSerializer"/>.</para>
	/// <para>Note that strings stored via this class will have '\r' characters removed by the xml writer.</para>
	/// </summary>
	public abstract class SerializableObjectBase
	{
		private static ConcurrentDictionary<string, object> fileLocks = new ConcurrentDictionary<string, object>();
		private static object MakeLockKey(string filePath)
		{
			return filePath;
		}
		public bool Save(string filePath = null)
		{
			int tries = 0;
			while (tries++ < 5)
				try
				{
					if (filePath == null)
						filePath = this.GetType().Name + ".cfg";
					object lockObj = fileLocks.GetOrAdd(filePath.ToLower(), MakeLockKey);
					lock (lockObj)
					{
						FileInfo fi = new FileInfo(filePath);
						if (!fi.Exists)
						{
							if (!fi.Directory.Exists)
								Directory.CreateDirectory(fi.Directory.FullName);
						}
						System.Xml.Serialization.XmlSerializer x = new System.Xml.Serialization.XmlSerializer(this.GetType());
						using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
							x.Serialize(fs, this);
					}
					return true;
				}
				catch (ThreadAbortException) { throw; }
				catch (Exception)
				{
					if (tries >= 5)
					{
					}
					else
						Thread.Sleep(1);
				}
			return false;
		}
		public bool Load(string filePath = null)
		{
			int tries = 0;
			while (tries++ < 5)
				try
				{
					Type thistype = this.GetType();
					if (filePath == null)
						filePath = thistype.Name + ".cfg";
					object lockObj = fileLocks.GetOrAdd(filePath.ToLower(), MakeLockKey);
					lock (lockObj)
					{
						if (!File.Exists(filePath))
							return false;
						System.Xml.Serialization.XmlSerializer x = new System.Xml.Serialization.XmlSerializer(this.GetType());
						x.UnknownElement += X_UnknownElement;
						object obj;
						using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
							obj = x.Deserialize(fs);
						foreach (FieldInfo sourceField in obj.GetType().GetFields())
						{
							try
							{
								FieldInfo targetField = thistype.GetField(sourceField.Name);
								if (targetField != null && targetField.MemberType == sourceField.MemberType)
									targetField.SetValue(this, sourceField.GetValue(obj));
							}
							catch (ThreadAbortException) { throw; }
							catch (Exception) { }
						}
						if (obj.GetType().GetCustomAttributes(typeof(SerializeProperties), false).FirstOrDefault() != null)
						{
							foreach (PropertyInfo sourceProperty in obj.GetType().GetProperties())
							{
								try
								{
									PropertyInfo targetProperty = thistype.GetProperty(sourceProperty.Name);
									if (targetProperty != null && targetProperty.MemberType == sourceProperty.MemberType)
										targetProperty.SetValue(this, sourceProperty.GetValue(obj));
								}
								catch (ThreadAbortException) { throw; }
								catch (Exception) { }
							}
						}
					}
					return true;
				}
				catch (ThreadAbortException) { throw; }
				catch (Exception)
				{
					if (tries >= 5)
					{
					}
					else
						Thread.Sleep(1);
				}
			return false;
		}

		private void X_UnknownElement(object sender, System.Xml.Serialization.XmlElementEventArgs e)
		{
			throw new ApplicationException("Configuration file is invalid.");
		}

		/// <summary>
		/// (Thread-)Safely checks if the settings file exists, and if not, saves the current instance.
		/// </summary>
		/// <param name="filePath"></param>
		public void SaveIfNoExist(string filePath = null)
		{
			if (filePath == null)
				filePath = this.GetType().Name + ".cfg";
			object lockObj = fileLocks.GetOrAdd(filePath.ToLower(), MakeLockKey);
			lock (lockObj)
			{
				if (!File.Exists(filePath))
					Save(filePath);
			}
		}
	}
	public class SerializeProperties : Attribute
	{
		public SerializeProperties() { }
	}
}
