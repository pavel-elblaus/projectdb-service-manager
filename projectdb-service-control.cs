using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace ProjectDBServiceControl
{
	internal sealed class AddConfig
	{
		public string host { get; set; }
		public string appName { get; set; }
		public string password { get; set; }
	}

	internal sealed class LibraryConfig
	{
		public string sourcePath { get; set; }
	}

	internal sealed class LibraryMetadata
	{
		public string version { get; set; }
		public string sourceFile { get; set; }
		public string sha256 { get; set; }
		public string installedAtUtc { get; set; }
	}

	internal static class Program
	{
		private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
		private static readonly string ExecutableDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		private static readonly string BaseDirectory = String.Equals(new DirectoryInfo(ExecutableDirectory).Name, "bin", StringComparison.OrdinalIgnoreCase)
			? Directory.GetParent(ExecutableDirectory).FullName
			: ExecutableDirectory;
		private static readonly string BinDirectory = Path.Combine(BaseDirectory, "bin");
		private static readonly string ServiceRoot = Path.Combine(BaseDirectory, "service");
		private static readonly string CommonWinSw = Path.Combine(BinDirectory, "winsw.exe");
		private static readonly string LogWrapperExe = Path.Combine(BinDirectory, "projectdb-log-wrapper.exe");
		private static readonly string LibraryDirectory = Path.Combine(BaseDirectory, "lib");
		private static readonly string LibraryPath = Path.Combine(LibraryDirectory, "app.so");
		private static readonly string LibraryMetadataPath = Path.Combine(LibraryDirectory, "app.so.meta.json");
		private static readonly string LibraryHistoryDirectory = Path.Combine(LibraryDirectory, "history");
		private static readonly string ProgramDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProjectDB");
		private static readonly string ServiceStateDirectory = Path.Combine(ProgramDataDirectory, "service-state");
		private static readonly string CleanupLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProjectDB-uninstall-cleanup.log");
		private const string StartupTaskName = "ProjectDB Service Startup";
		private const string PublisherSubject = "CN=ProjectDB Local Publisher";
		private const string ProjectDbRegistryPath = "SOFTWARE\\ProjectDB";
		private const string PublisherThumbprintValue = "SigningCertificateThumbprint";

		[STAThread]
		private static int Main(string[] args)
		{
			if (args == null || args.Length < 1)
				return Fail("Usage: projectdb-service-control.exe <start|stop|restart|restart-all|startup|add|remove|remove-all|install-library|remove-library|uninstall-all> [value]");

			if (!IsAdministrator())
				return RelaunchElevated(args);

			string action = args[0].ToLowerInvariant();
			try
			{
				if (action == "start" || action == "stop" || action == "restart")
				{
					if (args.Length != 2 || String.IsNullOrWhiteSpace(args[1]))
						throw new ArgumentException("Service ID is required.");
					ControlService(args[1], action);
				}
				else if (action == "restart-all")
				{
					RestartAll();
				}
				else if (action == "startup")
				{
					StartConfiguredServices();
				}
				else if (action == "add")
				{
					if (args.Length != 2 || String.IsNullOrWhiteSpace(args[1]))
						throw new ArgumentException("Registration configuration file is required.");
					AddApplication(args[1]);
				}
				else if (action == "remove")
				{
					if (args.Length != 2 || String.IsNullOrWhiteSpace(args[1]))
						throw new ArgumentException("Application name is required.");
					RemoveApplication(args[1]);
				}
				else if (action == "remove-all")
				{
					RemoveAllApplications();
				}
				else if (action == "install-library")
				{
					if (args.Length != 2 || String.IsNullOrWhiteSpace(args[1]))
						throw new ArgumentException("Library configuration file is required.");
					InstallLibrary(args[1]);
				}
				else if (action == "remove-library")
				{
					RemoveLibrary();
				}
				else if (action == "uninstall-all")
				{
					return UninstallAll();
				}
				else
				{
					throw new ArgumentException("Unknown action: " + action);
				}
				return 0;
			}
			catch (Exception ex)
			{
				return Fail(ex.Message);
			}
		}

		private static bool IsAdministrator()
		{
			WindowsIdentity identity = WindowsIdentity.GetCurrent();
			WindowsPrincipal principal = new WindowsPrincipal(identity);
			return principal.IsInRole(WindowsBuiltInRole.Administrator);
		}

		private static int RelaunchElevated(string[] args)
		{
			try
			{
				ProcessStartInfo psi = new ProcessStartInfo();
				psi.FileName = Application.ExecutablePath;
				psi.Arguments = JoinArguments(args);
				psi.UseShellExecute = true;
				psi.Verb = "runas";
				psi.WindowStyle = ProcessWindowStyle.Hidden;
				using (Process process = Process.Start(psi))
				{
					process.WaitForExit();
					return process.ExitCode;
				}
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				if (ex.NativeErrorCode == 1223)
					return 1223;
				return Fail("Could not request administrator privileges.\r\n\r\n" + ex.Message);
			}
		}

		private static string JoinArguments(string[] args)
		{
			List<string> values = new List<string>();
			foreach (string arg in args)
				values.Add(QuoteArgument(arg));
			return String.Join(" ", values.ToArray());
		}

		private static string QuoteArgument(string value)
		{
			if (value == null)
				return "\"\"";
			return "\"" + value.Replace("\"", "\\\"") + "\"";
		}

		private static void ControlService(string serviceId, string action)
		{
			using (ServiceController service = new ServiceController(serviceId))
			{
				service.Refresh();
				if (action == "start")
				{
					SetAutoStartState(serviceId, true);
					Start(service);
				}
				else if (action == "stop")
				{
					SetAutoStartState(serviceId, false);
					Stop(service);
				}
				else
				{
					Stop(service);
					service.Refresh();
					Start(service);
				}
			}
		}

		private static string GetAutoStartPath(string serviceId)
		{
			return Path.Combine(ServiceStateDirectory, serviceId + ".autostart");
		}

		private static void SetAutoStartState(string serviceId, bool enabled)
		{
			Directory.CreateDirectory(ServiceStateDirectory);
			string path = GetAutoStartPath(serviceId);
			if (enabled)
				File.WriteAllText(path, "1", new UTF8Encoding(false));
			else if (File.Exists(path))
				File.Delete(path);
		}

		private static void StartConfiguredServices()
		{
			foreach (string serviceId in DiscoverServiceIds())
			{
				if (!File.Exists(GetAutoStartPath(serviceId)))
					continue;
				try
				{
					using (ServiceController service = new ServiceController(serviceId))
						Start(service);
				}
				catch
				{
					// Startup continues with the remaining registered applications.
				}
			}
		}

		private static void RestartAll()
		{
			List<string> errors = new List<string>();
			foreach (string serviceId in DiscoverServiceIds())
			{
				try
				{
					using (ServiceController service = new ServiceController(serviceId))
					{
						service.Refresh();
						bool wasActive = service.Status == ServiceControllerStatus.Running ||
							service.Status == ServiceControllerStatus.StartPending ||
							service.Status == ServiceControllerStatus.Paused ||
							service.Status == ServiceControllerStatus.PausePending ||
							service.Status == ServiceControllerStatus.ContinuePending;
						if (!wasActive)
							continue;
						Stop(service);
						service.Refresh();
						Start(service);
					}
				}
				catch (Exception ex)
				{
					errors.Add(serviceId + ": " + ex.Message);
				}
			}
			if (errors.Count > 0)
				throw new InvalidOperationException("Some services could not be restarted:\r\n\r\n" + String.Join("\r\n", errors.ToArray()));
		}

		private static List<string> DiscoverServiceIds()
		{
			List<string> result = new List<string>();
			if (!Directory.Exists(ServiceRoot))
				return result;
			foreach (string directory in Directory.GetDirectories(ServiceRoot))
			{
				string xmlPath = Path.Combine(directory, "projectdb-service.xml");
				if (!File.Exists(xmlPath))
					continue;
				try
				{
					XmlDocument doc = new XmlDocument();
					doc.Load(xmlPath);
					XmlNode node = doc.SelectSingleNode("/service/id");
					if (node != null && !String.IsNullOrWhiteSpace(node.InnerText))
						result.Add(node.InnerText.Trim());
				}
				catch
				{
				}
			}
			return result;
		}

		private static void AddApplication(string configPath)
		{
			AddConfig config = null;
			try
			{
				if (!File.Exists(configPath))
					throw new FileNotFoundException("Registration configuration file was not found.", configPath);
				string json = File.ReadAllText(configPath, Encoding.UTF8);
				JavaScriptSerializer serializer = new JavaScriptSerializer();
				config = serializer.Deserialize<AddConfig>(json);
			}
			finally
			{
				try { File.Delete(configPath); } catch { }
			}

			if (config == null)
				throw new InvalidOperationException("Registration configuration is invalid.");
			string host = NormalizeHost(config.host);
			string appName = config.appName == null ? String.Empty : config.appName.Trim();
			string password = config.password;
			ValidateApplicationName(appName);
			if (String.IsNullOrEmpty(password))
				throw new ArgumentException("Password cannot be empty.");

			string projectDbExe = Path.Combine(BaseDirectory, "projectdb.exe");
			if (!File.Exists(projectDbExe))
				throw new FileNotFoundException("projectdb.exe was not found. Install ProjectDB first.", projectDbExe);
			if (!File.Exists(LogWrapperExe))
				throw new FileNotFoundException("ProjectDB log wrapper was not found. Run ProjectDB Setup once to update the installation.", LogWrapperExe);
			if (!File.Exists(CommonWinSw))
				throw new FileNotFoundException("Common WinSW executable was not found. Run ProjectDB Setup once to update the installation.", CommonWinSw);

			string serviceId = GetServiceId(appName);
			string serviceDir = Path.Combine(ServiceRoot, appName);
			string cliDir = Path.Combine(BaseDirectory, "tmp", "server", appName);
			string cliFile = Path.Combine(cliDir, "cli.json");
			string logDir = Path.Combine(BaseDirectory, "log", "service", appName);
			string wrapper = Path.Combine(serviceDir, "projectdb-service.exe");
			string xmlPath = Path.Combine(serviceDir, "projectdb-service.xml");

			if (ServiceExists(serviceId) || Directory.Exists(serviceDir) || Directory.Exists(cliDir))
				throw new InvalidOperationException("Application '" + appName + "' is already registered or has existing files. Remove it first if you want to register it again.");

			Directory.CreateDirectory(serviceDir);
			Directory.CreateDirectory(cliDir);
			Directory.CreateDirectory(logDir);

			JavaScriptSerializer jsonSerializer = new JavaScriptSerializer();
			Dictionary<string, string> cli = new Dictionary<string, string>();
			cli["host"] = host;
			cli["password"] = password;
			WriteUtf8NoBom(cliFile, jsonSerializer.Serialize(cli));
			ProtectCliFile(cliFile);
			password = null;
			config.password = null;

			File.Copy(CommonWinSw, wrapper, true);
			string xml = BuildServiceXml(serviceId, appName, LogWrapperExe, logDir);
			WriteUtf8NoBom(xmlPath, xml);

			ProcessResult install = RunHidden(wrapper, "install");
			if (install.ExitCode != 0)
				throw new InvalidOperationException("WinSW could not register the service.\r\n\r\n" + install.Error.Trim());
			GrantInteractiveServiceControl(serviceId);
			SetAutoStartState(serviceId, true);

			// Registration must finish promptly even when host/password are invalid.
			// Start is best-effort; Service Manager will show the actual state and logs,
			// while Logs/Remove remain available so the application can be recreated.
			try
			{
				using (ServiceController service = new ServiceController(serviceId))
				{
					service.Start();
				}
			}
			catch
			{
				// The service remains registered; startup diagnostics are available in its logs.
			}
		}

		private static void InstallLibrary(string configPath)
		{
			LibraryConfig config = null;
			try
			{
				if (!File.Exists(configPath))
					throw new FileNotFoundException("Library configuration file was not found.", configPath);
				string json = File.ReadAllText(configPath, Encoding.UTF8);
				JavaScriptSerializer serializer = new JavaScriptSerializer();
				config = serializer.Deserialize<LibraryConfig>(json);
			}
			finally
			{
				try { File.Delete(configPath); } catch { }
			}

			if (config == null || String.IsNullOrWhiteSpace(config.sourcePath))
				throw new InvalidOperationException("Select a local .so library file.");

			string sourcePath = Path.GetFullPath(config.sourcePath);
			if (!File.Exists(sourcePath))
				throw new FileNotFoundException("Selected library file was not found.", sourcePath);
			if (!String.Equals(Path.GetExtension(sourcePath), ".so", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("Only .so library files can be installed.");
			if (new FileInfo(sourcePath).Length <= 0)
				throw new InvalidOperationException("The selected library file is empty.");

			List<string> activeServices = StopActiveServices();
			string backupPath = null;
			string previousMetadata = File.Exists(LibraryMetadataPath) ? File.ReadAllText(LibraryMetadataPath, Encoding.UTF8) : null;
			try
			{
				Directory.CreateDirectory(LibraryDirectory);
				if (File.Exists(LibraryPath)) backupPath = BackupCurrentLibrary();

				string temporary = Path.Combine(LibraryDirectory, ".app." + Guid.NewGuid().ToString("N") + ".tmp");
				try
				{
					File.Copy(sourcePath, temporary, true);
					File.Copy(temporary, LibraryPath, true);
				}
				finally { try { File.Delete(temporary); } catch { } }

				LibraryMetadata metadata = new LibraryMetadata();
				metadata.version = ReadLibraryVersion(LibraryPath) ?? String.Empty;
				metadata.sourceFile = Path.GetFileName(sourcePath);
				metadata.sha256 = ComputeSha256(LibraryPath);
				metadata.installedAtUtc = DateTime.UtcNow.ToString("o");
				JavaScriptSerializer serializer = new JavaScriptSerializer();
				WriteUtf8NoBom(LibraryMetadataPath, serializer.Serialize(metadata));

				RestartServices(activeServices, true);
			}
			catch
			{
				try
				{
					if (!String.IsNullOrEmpty(backupPath) && File.Exists(backupPath)) File.Copy(backupPath, LibraryPath, true);
					else File.Delete(LibraryPath);
					if (previousMetadata != null) WriteUtf8NoBom(LibraryMetadataPath, previousMetadata);
					else File.Delete(LibraryMetadataPath);
					RestartServices(activeServices, false);
				}
				catch { }
				throw;
			}
		}

		private static void RemoveLibrary()
		{
			if (!File.Exists(LibraryPath) && !File.Exists(LibraryMetadataPath))
				return;

			List<string> activeServices = StopActiveServices();
			string previousMetadata = File.Exists(LibraryMetadataPath) ? File.ReadAllText(LibraryMetadataPath, Encoding.UTF8) : null;
			string backupPath = null;
			try
			{
				if (File.Exists(LibraryPath))
					backupPath = BackupCurrentLibrary();
				try { File.Delete(LibraryPath); } catch { }
				try { File.Delete(LibraryMetadataPath); } catch { }
				RestartServices(activeServices, true);
			}
			catch
			{
				try
				{
					if (!String.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
						File.Copy(backupPath, LibraryPath, true);
					if (previousMetadata != null)
						WriteUtf8NoBom(LibraryMetadataPath, previousMetadata);
					RestartServices(activeServices, false);
				}
				catch { }
				throw;
			}
		}

		private static List<string> StopActiveServices()
		{
			List<string> active = new List<string>();
			foreach (string serviceId in DiscoverServiceIds())
			{
				try
				{
					using (ServiceController service = new ServiceController(serviceId))
					{
						service.Refresh();
						bool wasActive = service.Status == ServiceControllerStatus.Running ||
							service.Status == ServiceControllerStatus.StartPending ||
							service.Status == ServiceControllerStatus.Paused ||
							service.Status == ServiceControllerStatus.PausePending ||
							service.Status == ServiceControllerStatus.ContinuePending;
						if (!wasActive)
							continue;
						active.Add(serviceId);
						Stop(service);
					}
				}
				catch (Exception ex)
				{
					throw new InvalidOperationException("Could not stop service before updating app.so: " + serviceId + "\r\n\r\n" + ex.Message, ex);
				}
			}
			return active;
		}

		private static void RestartServices(List<string> serviceIds, bool verifyStable)
		{
			List<string> errors = new List<string>();
			foreach (string serviceId in serviceIds)
			{
				try
				{
					using (ServiceController service = new ServiceController(serviceId))
						Start(service);
					if (verifyStable)
						VerifyStableRunning(serviceId, 5, 20);
				}
				catch (Exception ex)
				{
					errors.Add(serviceId + ": " + ex.Message);
				}
			}
			if (errors.Count > 0)
				throw new InvalidOperationException("Some ProjectDB services did not restart correctly after the app.so change:\r\n\r\n" + String.Join("\r\n", errors.ToArray()));
		}

		private static string BackupCurrentLibrary()
		{
			if (!File.Exists(LibraryPath))
				return null;
			Directory.CreateDirectory(LibraryHistoryDirectory);
			string hash = ComputeSha256(LibraryPath);
			string shortHash = hash.Length >= 8 ? hash.Substring(0, 8) : hash;
			string backup = Path.Combine(LibraryHistoryDirectory, "app-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-" + shortHash + ".so");
			File.Copy(LibraryPath, backup, false);
			return backup;
		}

		private static string ReadLibraryVersion(string path)
		{
			try
			{
				byte[] buffer = new byte[256];
				int count;
				using (FileStream stream = File.OpenRead(path)) count = stream.Read(buffer, 0, buffer.Length);
				if (count <= 0) return null;
				string text = Encoding.ASCII.GetString(buffer, 0, count);
				int colon = text.IndexOf(':');
				if (colon <= 0 || colon > 64) return null;
				string version = text.Substring(0, colon).Trim();
				return Regex.IsMatch(version, "^[0-9A-Za-z._+-]{1,64}$") ? version : null;
			}
			catch { return null; }
		}

		private static string ComputeSha256(string path)
		{
			using (SHA256 sha = SHA256.Create())
			using (FileStream stream = File.OpenRead(path))
			{
				byte[] hash = sha.ComputeHash(stream);
				StringBuilder value = new StringBuilder(hash.Length * 2);
				foreach (byte b in hash)
					value.Append(b.ToString("x2"));
				return value.ToString();
			}
		}

		private static void RemoveApplication(string appName)
		{
			ValidateApplicationName(appName);
			string serviceDir = Path.Combine(ServiceRoot, appName);
			string xmlPath = Path.Combine(serviceDir, "projectdb-service.xml");
			string wrapper = Path.Combine(serviceDir, "projectdb-service.exe");
			string serviceId = GetServiceId(appName);
			if (File.Exists(xmlPath))
			{
				try
				{
					XmlDocument doc = new XmlDocument();
					doc.Load(xmlPath);
					XmlNode idNode = doc.SelectSingleNode("/service/id");
					if (idNode != null && !String.IsNullOrWhiteSpace(idNode.InnerText))
						serviceId = idNode.InnerText.Trim();
				}
				catch { }
			}

			if (ServiceExists(serviceId))
			{
				try
				{
					using (ServiceController service = new ServiceController(serviceId))
						Stop(service);
				}
				catch { }

				if (File.Exists(wrapper))
				{
					ProcessResult uninstall = RunHidden(wrapper, "uninstall");
					if (uninstall.ExitCode != 0 && ServiceExists(serviceId))
						DeleteServiceWithSc(serviceId);
				}
				else
				{
					DeleteServiceWithSc(serviceId);
				}
				WaitServiceRemoved(serviceId, 10);
			}

			SetAutoStartState(serviceId, false);
			DeleteDirectory(serviceDir);
			DeleteDirectory(Path.Combine(BaseDirectory, "tmp", "server", appName));
			DeleteDirectory(Path.Combine(BaseDirectory, "log", "service", appName));
		}

		private static void RemoveAllApplications()
		{
			List<string> errors = new List<string>();
			foreach (string appName in DiscoverApplicationNames())
			{
				try { RemoveApplication(appName); }
				catch (Exception ex) { errors.Add(appName + ": " + ex.Message); }
			}
			if (errors.Count > 0)
				throw new InvalidOperationException("Some applications could not be removed:\r\n\r\n" + String.Join("\r\n", errors.ToArray()));
		}

		private static int UninstallAll()
		{
			DialogResult confirm = MessageBox.Show(
				"This will remove all ProjectDB services, application configurations, logs, Service Manager startup entry and the complete ProjectDB installation.\r\n\r\nContinue?",
				"Uninstall ProjectDB",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Warning,
				MessageBoxDefaultButton.Button2);
			if (confirm != DialogResult.Yes)
				return 0;

			// Do not keep the installation directory as the process working directory.
			try { Environment.CurrentDirectory = Path.GetTempPath(); } catch { }

			try
			{
				foreach (Process process in Process.GetProcessesByName("projectdb-service-manager"))
				{
					try
					{
						process.Kill();
						if (!process.WaitForExit(5000))
							throw new InvalidOperationException("ProjectDB Service Manager did not exit during uninstall.");
					}
					finally { process.Dispose(); }
				}

				List<string> applications = DiscoverApplicationNames();
				foreach (string appName in applications)
					RemoveApplication(appName);

				RemoveStartupRegistration();
				RemoveStartupTask();
				RemoveManagerShortcuts();
				RemoveUninstallRegistration();
				RemoveLocalPublisherCertificate();
				RemoveProjectDbRegistry();

				try
				{
					File.WriteAllText(
						CleanupLogPath,
						DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " Uninstall cleanup diagnostics started." + Environment.NewLine +
						"Uninstall PID=" + Process.GetCurrentProcess().Id.ToString() + Environment.NewLine +
						"Working directory=" + Environment.CurrentDirectory + Environment.NewLine +
						"Install directory=" + BaseDirectory + Environment.NewLine +
						"ProgramData directory=" + ProgramDataDirectory + Environment.NewLine,
						new UTF8Encoding(false));
				}
				catch { }

				MessageBox.Show("ProjectDB was uninstalled successfully.", "ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Information);
				ScheduleDirectoryRemoval(BaseDirectory);
				ScheduleDirectoryRemoval(ProgramDataDirectory);
				return 0;
			}
			catch (Exception ex)
			{
				return Fail("ProjectDB could not be completely uninstalled.\r\n\r\n" + ex.Message);
			}
		}

		private static List<string> DiscoverApplicationNames()
		{
			List<string> result = new List<string>();
			if (!Directory.Exists(ServiceRoot))
				return result;
			foreach (string dir in Directory.GetDirectories(ServiceRoot))
				result.Add(new DirectoryInfo(dir).Name);
			return result;
		}

		private static void RemoveStartupRegistration()
		{
			using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
			using (RegistryKey key = baseKey.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
			{
				if (key != null)
				{
					key.DeleteValue("ProjectDB Tray", false);
					key.DeleteValue("ProjectDB Service Manager", false);
				}
			}
		}

		private static void RemoveStartupTask()
		{
			try
			{
				string schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
				RunHidden(schtasks, "/Delete /TN " + QuoteArgument(StartupTaskName) + " /F");
			}
			catch { }
			try
			{
				if (Directory.Exists(ServiceStateDirectory))
					Directory.Delete(ServiceStateDirectory, true);
			}
			catch { }
		}

		private static void RemoveManagerShortcuts()
		{
			try
			{
				string desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
				string desktopShortcut = Path.Combine(desktop, "ProjectDB Service Manager.lnk");
				if (File.Exists(desktopShortcut)) File.Delete(desktopShortcut);
			}
			catch { }
			try
			{
				string programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
				string startFolder = Path.Combine(programs, "ProjectDB");
				if (Directory.Exists(startFolder)) Directory.Delete(startFolder, true);
			}
			catch { }
		}

		private static void RemoveUninstallRegistration()
		{
			using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
			using (RegistryKey key = baseKey.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", true))
			{
				if (key != null)
					key.DeleteSubKeyTree("ProjectDB", false);
			}
		}

		private static void RemoveLocalPublisherCertificate()
		{
			string thumbprint = null;
			try
			{
				using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
				using (RegistryKey key = baseKey.OpenSubKey(ProjectDbRegistryPath, false))
				{
					if (key != null)
						thumbprint = key.GetValue(PublisherThumbprintValue) as string;
				}
			}
			catch { }

			StoreName[] stores = new StoreName[] { StoreName.My, StoreName.Root, StoreName.TrustedPublisher };
			foreach (StoreName storeName in stores)
			{
				try
				{
					using (X509Store store = new X509Store(storeName, StoreLocation.LocalMachine))
					{
						store.Open(OpenFlags.ReadWrite);
						X509Certificate2Collection matches = new X509Certificate2Collection();
						foreach (X509Certificate2 cert in store.Certificates)
						{
							bool sameThumbprint = !String.IsNullOrWhiteSpace(thumbprint) && String.Equals(cert.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase);
							bool sameSubject = String.Equals(cert.Subject, PublisherSubject, StringComparison.OrdinalIgnoreCase);
							if (sameThumbprint || sameSubject)
								matches.Add(cert);
						}
						foreach (X509Certificate2 cert in matches)
							store.Remove(cert);
					}
				}
				catch { }
			}
		}

		private static void RemoveProjectDbRegistry()
		{
			try
			{
				using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
				using (RegistryKey software = baseKey.OpenSubKey("SOFTWARE", true))
				{
					if (software != null)
						software.DeleteSubKeyTree("ProjectDB", false);
				}
			}
			catch { }
		}

		private static void ScheduleDirectoryRemoval(string path)
		{
			string escapedPath = path.Replace("'", "''");
			string escapedLog = CleanupLogPath.Replace("'", "''");
			int parentProcessId = Process.GetCurrentProcess().Id;
			string script =
				"$p='" + escapedPath + "';" +
				"$log='" + escapedLog + "';" +
				"$prefix=$p.TrimEnd('\\')+'\\';" +
				"$parent=" + parentProcessId.ToString() + ";" +
				"function Log([string]$m){try{[IO.File]::AppendAllText($log,((Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff')+' ['+$p+'] '+$m+[Environment]::NewLine),(New-Object Text.UTF8Encoding($false)))}catch{}};" +
				"Log ('cleanup process started; PID='+$PID+'; parent='+$parent+'; cwd='+[Environment]::CurrentDirectory);" +
				"$parentExited=$false;" +
				"for($w=0;$w -lt 120;$w++){" +
				"if(-not (Get-Process -Id $parent -ErrorAction SilentlyContinue)){$parentExited=$true;break};" +
				"Start-Sleep -Milliseconds 250" +
				"};" +
				"Log ('parent exited='+$parentExited+'; directory exists='+[IO.Directory]::Exists($p));" +
				"for($i=0;$i -lt 60 -and [IO.Directory]::Exists($p);$i++){" +
				"Log ('delete attempt '+($i+1));" +
				"try{" +
				"Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | ForEach-Object {" +
				"$exe=$_.ExecutablePath;" +
				"if($exe -and $exe.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){" +
				"Log ('residual process PID='+$_.ProcessId+'; name='+$_.Name+'; exe='+$exe);" +
				"try{Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop;Log ('stopped PID='+$_.ProcessId)}catch{Log ('could not stop PID='+$_.ProcessId+'; '+$_.Exception.GetType().FullName+': '+$_.Exception.Message)}" +
				"}" +
				"}" +
				"}catch{Log ('process enumeration failed; '+$_.Exception.GetType().FullName+': '+$_.Exception.Message)};" +
				"try{" +
				"$entries=@([IO.Directory]::EnumerateFileSystemEntries($p));" +
				"Log ('entries before delete='+$entries.Count);" +
				"[IO.Directory]::Delete($p,$true);" +
				"Log 'Directory.Delete returned successfully'" +
				"}catch{" +
				"$e=$_.Exception;" +
				"Log ('Directory.Delete failed; type='+$e.GetType().FullName+'; hresult=0x'+$e.HResult.ToString('X8')+'; message='+$e.Message)" +
				"};" +
				"if([IO.Directory]::Exists($p)){Start-Sleep -Milliseconds 500}" +
				"};" +
				"Log ('cleanup finished; directory exists='+[IO.Directory]::Exists($p));";
			string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
			ProcessStartInfo psi = new ProcessStartInfo();
			psi.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
			psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded;
			psi.WorkingDirectory = Path.GetTempPath();
			psi.UseShellExecute = false;
			psi.CreateNoWindow = true;
			psi.WindowStyle = ProcessWindowStyle.Hidden;
			Process.Start(psi);
		}

		private static void VerifyStableRunning(string serviceId, int stableSecondsRequired, int timeoutSeconds)
		{
			int stable = 0;
			for (int i = 0; i < timeoutSeconds; i++)
			{
				Thread.Sleep(1000);
				try
				{
					using (ServiceController service = new ServiceController(serviceId))
					{
						service.Refresh();
						if (service.Status == ServiceControllerStatus.Running)
							stable++;
						else
							stable = 0;
					}
				}
				catch { stable = 0; }
				if (stable >= stableSecondsRequired)
					return;
			}
			throw new InvalidOperationException("The service did not remain in Running state for " + stableSecondsRequired + " seconds. Check its service logs.");
		}

		private static string BuildServiceXml(string serviceId, string appName, string executablePath, string logDir)
		{
			return "<service>\r\n" +
				"\t<id>" + EscapeXml(serviceId) + "</id>\r\n" +
				"\t<name>ProjectDB - " + EscapeXml(appName) + "</name>\r\n" +
				"\t<description>ProjectDB application service for " + EscapeXml(appName) + "</description>\r\n\r\n" +
				"\t<executable>" + EscapeXml(executablePath) + "</executable>\r\n" +
				"\t<arguments>" + EscapeXml(appName) + "</arguments>\r\n" +
				"\t<workingdirectory>" + EscapeXml(BaseDirectory) + "</workingdirectory>\r\n\r\n" +
				"\t<env name=\"PDB_METRIC\" value=\"service\"/>\r\n" +
				"\t<env name=\"NO_COLOR\" value=\"1\"/>\r\n" +
				"\t<env name=\"FORCE_COLOR\" value=\"0\"/>\r\n\r\n" +
				"\t<startmode>Manual</startmode>\r\n\r\n" +
				"\t<stoptimeout>30 sec</stoptimeout>\r\n" +
				"\t<stopparentprocessfirst>true</stopparentprocessfirst>\r\n\r\n" +
				"\t<onfailure action=\"restart\" delay=\"3 sec\"/>\r\n" +
				"\t<resetfailure>1 hour</resetfailure>\r\n\r\n" +
				"\t<logpath>" + EscapeXml(logDir) + "</logpath>\r\n" +
				"\t<log mode=\"roll\"/>\r\n" +
				"</service>\r\n";
		}

		private static string EscapeXml(string value)
		{
			return SecurityElement.Escape(value) ?? String.Empty;
		}

		private static string NormalizeHost(string value)
		{
			if (String.IsNullOrWhiteSpace(value))
				throw new ArgumentException("Server address cannot be empty.");
			string host = value.Trim().TrimEnd('/');
			if (!Regex.IsMatch(host, "^https?://", RegexOptions.IgnoreCase))
				host = "https://" + host;
			Uri uri;
			if (!Uri.TryCreate(host, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
				throw new ArgumentException("Server address is invalid.");
			return host;
		}

		private static void ValidateApplicationName(string appName)
		{
			if (String.IsNullOrWhiteSpace(appName) || !Regex.IsMatch(appName, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$"))
				throw new ArgumentException("Application name is invalid. Use up to 100 letters, digits, dots, underscores or hyphens.");
		}

		private static string GetServiceId(string appName)
		{
			string sanitized = Regex.Replace(appName, "[^A-Za-z0-9]", String.Empty);
			if (sanitized.Length > 32)
				sanitized = sanitized.Substring(0, 32);
			using (SHA256 sha = SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(appName));
				StringBuilder value = new StringBuilder();
				for (int i = 0; i < 4; i++)
					value.Append(hash[i].ToString("X2"));
				return "PDB" + sanitized + value.ToString();
			}
		}

		private static void ProtectCliFile(string path)
		{
			FileSecurity security = new FileSecurity();
			security.SetAccessRuleProtection(true, false);
			SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
			SecurityIdentifier admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
			security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
			security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
			File.SetAccessControl(path, security);
		}

		private static void WriteUtf8NoBom(string path, string value)
		{
			File.WriteAllText(path, value, new UTF8Encoding(false));
		}

		private static void GrantInteractiveServiceControl(string serviceId)
		{
			string sc = Path.Combine(Environment.SystemDirectory, "sc.exe");
			ProcessResult current = RunHidden(sc, "sdshow " + QuoteArgument(serviceId));
			if (current.ExitCode != 0)
				throw new InvalidOperationException("Could not read service security descriptor for " + serviceId + ".\r\n" + current.Error.Trim());
			string sddl = null;
			foreach (string raw in (current.Output ?? String.Empty).Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
			{
				string line = raw.Trim();
				if (line.StartsWith("D:", StringComparison.Ordinal)) sddl = line;
			}
			if (String.IsNullOrWhiteSpace(sddl))
				throw new InvalidOperationException("Windows returned an invalid service security descriptor for " + serviceId + ".");
			const string ace = "(A;;LCRPWPLO;;;IU)";
			if (sddl.IndexOf(ace, StringComparison.OrdinalIgnoreCase) >= 0) return;
			int sacl = sddl.IndexOf("S:", StringComparison.Ordinal);
			string updated = sacl >= 0 ? sddl.Insert(sacl, ace) : sddl + ace;
			ProcessResult set = RunHidden(sc, "sdset " + QuoteArgument(serviceId) + " " + QuoteArgument(updated));
			if (set.ExitCode != 0)
				throw new InvalidOperationException("Could not grant interactive service control permission for " + serviceId + ".\r\n" + set.Error.Trim());
		}

		private static bool ServiceExists(string serviceId)
		{
			try
			{
				using (ServiceController service = new ServiceController(serviceId))
				{
					ServiceControllerStatus status = service.Status;
					return true;
				}
			}
			catch { return false; }
		}

		private static void WaitServiceRemoved(string serviceId, int seconds)
		{
			for (int i = 0; i < seconds * 4; i++)
			{
				if (!ServiceExists(serviceId))
					return;
				Thread.Sleep(250);
			}
			if (ServiceExists(serviceId))
				throw new InvalidOperationException("Windows service could not be removed: " + serviceId);
		}

		private static void DeleteServiceWithSc(string serviceId)
		{
			string sc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
			ProcessResult result = RunHidden(sc, "delete " + QuoteArgument(serviceId));
			if (result.ExitCode != 0 && ServiceExists(serviceId))
				throw new InvalidOperationException("Windows service could not be deleted.\r\n\r\n" + result.Error.Trim());
		}

		private static void DeleteDirectory(string path)
		{
			if (!Directory.Exists(path))
				return;
			Directory.Delete(path, true);
		}

		private static void Start(ServiceController service)
		{
			service.Refresh();
			if (service.Status == ServiceControllerStatus.Running)
				return;
			if (service.Status == ServiceControllerStatus.StopPending)
				service.WaitForStatus(ServiceControllerStatus.Stopped, Timeout);
			service.Refresh();
			if (service.Status == ServiceControllerStatus.Stopped)
				service.Start();
			service.WaitForStatus(ServiceControllerStatus.Running, Timeout);
		}

		private static void Stop(ServiceController service)
		{
			service.Refresh();
			if (service.Status == ServiceControllerStatus.Stopped)
				return;
			if (service.Status == ServiceControllerStatus.StartPending)
				service.WaitForStatus(ServiceControllerStatus.Running, Timeout);
			service.Refresh();
			if (service.Status == ServiceControllerStatus.Running || service.Status == ServiceControllerStatus.Paused)
				service.Stop();
			service.WaitForStatus(ServiceControllerStatus.Stopped, Timeout);
		}

		private sealed class ProcessResult
		{
			public int ExitCode;
			public string Output;
			public string Error;
		}

		private static ProcessResult RunHidden(string fileName, string arguments)
		{
			ProcessStartInfo psi = new ProcessStartInfo();
			psi.FileName = fileName;
			psi.Arguments = arguments;
			psi.UseShellExecute = false;
			psi.CreateNoWindow = true;
			psi.WindowStyle = ProcessWindowStyle.Hidden;
			psi.RedirectStandardOutput = true;
			psi.RedirectStandardError = true;
			using (Process process = Process.Start(psi))
			{
				string output = process.StandardOutput.ReadToEnd();
				string error = process.StandardError.ReadToEnd();
				process.WaitForExit();
				return new ProcessResult { ExitCode = process.ExitCode, Output = output, Error = error };
			}
		}

		private static int Fail(string message)
		{
			try { MessageBox.Show(message, "ProjectDB Service Manager", MessageBoxButtons.OK, MessageBoxIcon.Error); }
			catch { }
			return 1;
		}
	}
}
