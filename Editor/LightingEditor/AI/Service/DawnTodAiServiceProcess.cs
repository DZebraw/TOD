using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace DawnTODEditor.AI
{
    internal interface IDawnTodAiProcessHandle : IDisposable
    {
        int Id { get; }
        bool HasExited { get; }
        int? ExitCode { get; }
        bool TryGetExecutablePath(out string executablePath);
        void Kill();
    }

    internal interface IDawnTodAiProcessLauncher
    {
        IDawnTodAiProcessHandle Start(
            DawnTodAiServicePaths paths,
            string sessionToken,
            int parentProcessId);
    }

    internal interface IDawnTodAiProcessLocator
    {
        bool TryGet(int processId, out IDawnTodAiProcessHandle process);
    }

    internal interface IDawnTodAiPortProbe
    {
        bool IsAvailable();
    }

    internal sealed class DawnTodAiProcessLauncher : IDawnTodAiProcessLauncher
    {
        private readonly string _configPathOverride;
        private readonly string _localAppDataOverride;

        public DawnTodAiProcessLauncher(
            string configPathOverride = null,
            string localAppDataOverride = null)
        {
            _configPathOverride = string.IsNullOrWhiteSpace(configPathOverride)
                ? null
                : Path.GetFullPath(configPathOverride);
            _localAppDataOverride = string.IsNullOrWhiteSpace(localAppDataOverride)
                ? null
                : Path.GetFullPath(localAppDataOverride);
        }

        public IDawnTodAiProcessHandle Start(
            DawnTodAiServicePaths paths,
            string sessionToken,
            int parentProcessId)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = paths.ExecutablePath,
                WorkingDirectory = paths.ServiceDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.EnvironmentVariables[DawnTodAiProtocol.SessionTokenEnvironmentVariable] =
                sessionToken;
            startInfo.EnvironmentVariables[DawnTodAiProtocol.ParentPidEnvironmentVariable] =
                parentProcessId.ToString(CultureInfo.InvariantCulture);
            if (_configPathOverride != null)
            {
                startInfo.EnvironmentVariables[DawnTodAiProtocol.ConfigPathEnvironmentVariable] =
                    _configPathOverride;
            }
            if (_localAppDataOverride != null)
            {
                startInfo.EnvironmentVariables["LOCALAPPDATA"] = _localAppDataOverride;
            }

            Process process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("The service process did not start.");
            }

            return new DawnTodAiProcessHandle(process);
        }
    }

    internal sealed class DawnTodAiProcessLocator : IDawnTodAiProcessLocator
    {
        public bool TryGet(int processId, out IDawnTodAiProcessHandle process)
        {
            process = null;
            if (processId <= 0)
            {
                return false;
            }

            try
            {
                Process located = Process.GetProcessById(processId);
                if (located.HasExited)
                {
                    located.Dispose();
                    return false;
                }

                process = new DawnTodAiProcessHandle(located);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    internal sealed class DawnTodAiProcessHandle : IDawnTodAiProcessHandle
    {
        private readonly Process _process;

        public DawnTodAiProcessHandle(Process process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        public int Id => _process.Id;

        public bool HasExited
        {
            get
            {
                try
                {
                    return _process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public int? ExitCode
        {
            get
            {
                try
                {
                    return _process.HasExited ? _process.ExitCode : (int?)null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        public bool TryGetExecutablePath(out string executablePath)
        {
            executablePath = null;
            try
            {
                executablePath = _process.MainModule?.FileName;
                return !string.IsNullOrWhiteSpace(executablePath);
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public void Kill()
        {
            if (HasExited)
            {
                return;
            }

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                    Arguments = "/PID " + _process.Id + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process treeKiller = Process.Start(startInfo))
                {
                    if (treeKiller != null &&
                        treeKiller.WaitForExit(2000) &&
                        treeKiller.ExitCode == 0)
                    {
                        _process.WaitForExit(2000);
                        return;
                    }
                }
            }

            if (!HasExited)
            {
                _process.Kill();
                _process.WaitForExit(2000);
            }
        }

        public void Dispose()
        {
            _process.Dispose();
        }
    }

    internal sealed class DawnTodAiPortProbe : IDawnTodAiPortProbe
    {
        public bool IsAvailable()
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, DawnTodAiProtocol.Port);
                listener.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                listener?.Stop();
            }
        }
    }

    internal static class DawnTodAiProcessIdentity
    {
        public static bool MatchesExpectedExecutable(
            IDawnTodAiProcessHandle process,
            string expectedExecutablePath)
        {
            if (process == null || process.HasExited ||
                !process.TryGetExecutablePath(out string actualPath))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(actualPath),
                    Path.GetFullPath(expectedExecutablePath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
