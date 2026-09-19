using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ProjectDBLogWrapper
{
	internal static class Program
	{
		private static readonly Regex AnsiEscape = new Regex(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
		private static readonly Regex ControlCharacters = new Regex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);
		private static readonly object OutputSync = new object();
		private static Process _child;
		private static IntPtr _job = IntPtr.Zero;

		private const uint JobObjectLimitKillOnJobClose = 0x00002000;
		private const int JobObjectExtendedLimitInformation = 9;

		[STAThread]
		private static int Main(string[] args)
		{
			if (args == null || args.Length != 1 || String.IsNullOrWhiteSpace(args[0]))
			{
				Console.Error.WriteLine("ProjectDB application name is required.");
				return 2;
			}

			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string projectDbExe = Path.Combine(baseDirectory, "projectdb.exe");
			if (!File.Exists(projectDbExe))
			{
				Console.Error.WriteLine("projectdb.exe was not found: " + projectDbExe);
				return 2;
			}

			try
			{
				Console.OutputEncoding = new UTF8Encoding(false);
			}
			catch { }

			ProcessStartInfo psi = new ProcessStartInfo();
			psi.FileName = projectDbExe;
			psi.Arguments = QuoteArgument(args[0]);
			psi.WorkingDirectory = baseDirectory;
			psi.UseShellExecute = false;
			psi.CreateNoWindow = true;
			psi.RedirectStandardOutput = true;
			psi.RedirectStandardError = true;
			psi.EnvironmentVariables["PDB_METRIC"] = "service";
			psi.EnvironmentVariables["NO_COLOR"] = "1";
			psi.EnvironmentVariables["FORCE_COLOR"] = "0";

			_child = new Process();
			_child.StartInfo = psi;
			_child.EnableRaisingEvents = true;

			try
			{
				if (!_child.Start())
					throw new InvalidOperationException("Could not start projectdb.exe.");

				CreateKillOnCloseJob(_child);

				Thread stdout = new Thread(new ThreadStart(delegate { Pump(_child.StandardOutput, false); }));
				Thread stderr = new Thread(new ThreadStart(delegate { Pump(_child.StandardError, true); }));
				stdout.IsBackground = true;
				stderr.IsBackground = true;
				stdout.Start();
				stderr.Start();

				_child.WaitForExit();
				stdout.Join(3000);
				stderr.Join(3000);
				return _child.ExitCode;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(Clean(ex.Message));
				return 1;
			}
			finally
			{
				try { if (_child != null) _child.Dispose(); } catch { }
				if (_job != IntPtr.Zero)
				{
					CloseHandle(_job);
					_job = IntPtr.Zero;
				}
			}
		}

		private static void Pump(StreamReader reader, bool error)
		{
			try
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					string clean = Clean(line);
					lock (OutputSync)
					{
						if (error) Console.Error.WriteLine(clean);
						else Console.Out.WriteLine(clean);
					}
				}
			}
			catch { }
		}

		private static string Clean(string value)
		{
			if (String.IsNullOrEmpty(value)) return value ?? String.Empty;
			string clean = AnsiEscape.Replace(value, String.Empty);
			clean = ControlCharacters.Replace(clean, String.Empty);
			return clean.TrimEnd();
		}

		private static string QuoteArgument(string value)
		{
			if (value == null) return "\"\"";
			return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
		}

		private static void CreateKillOnCloseJob(Process process)
		{
			try
			{
				_job = CreateJobObject(IntPtr.Zero, null);
				if (_job == IntPtr.Zero) return;

				JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
				info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
				int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
				IntPtr buffer = Marshal.AllocHGlobal(length);
				try
				{
					Marshal.StructureToPtr(info, buffer, false);
					if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, buffer, (uint)length)) return;
					AssignProcessToJobObject(_job, process.Handle);
				}
				finally
				{
					Marshal.FreeHGlobal(buffer);
				}
			}
			catch { }
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
		{
			public long PerProcessUserTimeLimit;
			public long PerJobUserTimeLimit;
			public uint LimitFlags;
			public UIntPtr MinimumWorkingSetSize;
			public UIntPtr MaximumWorkingSetSize;
			public uint ActiveProcessLimit;
			public UIntPtr Affinity;
			public uint PriorityClass;
			public uint SchedulingClass;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct IO_COUNTERS
		{
			public ulong ReadOperationCount;
			public ulong WriteOperationCount;
			public ulong OtherOperationCount;
			public ulong ReadTransferCount;
			public ulong WriteTransferCount;
			public ulong OtherTransferCount;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
		{
			public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
			public IO_COUNTERS IoInfo;
			public UIntPtr ProcessMemoryLimit;
			public UIntPtr JobMemoryLimit;
			public UIntPtr PeakProcessMemoryUsed;
			public UIntPtr PeakJobMemoryUsed;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string name);

		[DllImport("kernel32.dll")]
		private static extern bool SetInformationJobObject(IntPtr job, int infoType, IntPtr info, uint length);

		[DllImport("kernel32.dll")]
		private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

		[DllImport("kernel32.dll")]
		private static extern bool CloseHandle(IntPtr handle);
	}
}
