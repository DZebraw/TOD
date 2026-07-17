using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace DawnTODEditor.AI
{
    internal enum DawnTodAiServiceState
    {
        Stopped,
        Starting,
        Ready,
        Error,
        Stopping
    }

    internal sealed class DawnTodAiServiceOperationResult
    {
        public bool IsSuccess { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        private DawnTodAiServiceOperationResult(
            bool isSuccess,
            string errorCode,
            string errorMessage)
        {
            IsSuccess = isSuccess;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public static DawnTodAiServiceOperationResult Success()
        {
            return new DawnTodAiServiceOperationResult(true, null, null);
        }

        public static DawnTodAiServiceOperationResult Failed(string code, string message)
        {
            return new DawnTodAiServiceOperationResult(false, code, message);
        }
    }

    internal interface IDawnTodAiSessionStore
    {
        bool TryLoad(out int processId, out string sessionToken);
        void Save(int processId, string sessionToken);
        void Clear();
    }

    internal interface IDawnTodAiClock
    {
        DateTime UtcNow { get; }
        Task Delay(TimeSpan delay, CancellationToken cancellationToken);
    }

    internal interface IDawnTodAiEditorEnvironment
    {
        RuntimePlatform Platform { get; }
        int CurrentProcessId { get; }
    }

    internal interface IDawnTodAiRequestService
    {
        bool IsReady { get; }
        IDawnTodAiHttpClient HttpClient { get; }
    }

    internal interface IDawnTodAiServiceControl : IDawnTodAiRequestService
    {
        DawnTodAiServiceState State { get; }
        string LastErrorCode { get; }
        string LastErrorMessage { get; }
        event Action<DawnTodAiServiceState> StateChanged;
        Task<DawnTodAiServiceOperationResult> StartAsync();
        Task<DawnTodAiServiceOperationResult> StopAsync();
        Task<DawnTodAiServiceOperationResult> RestartAsync();
    }

    internal sealed class DawnTodAiSessionStore : IDawnTodAiSessionStore
    {
        private const string DefaultScope = "DawnTOD.AI.Service";

        private readonly string _processIdKey;
        private readonly string _sessionTokenKey;

        public DawnTodAiSessionStore(string scope = DefaultScope)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException("Session scope is required.", nameof(scope));
            }

            _processIdKey = scope + ".ProcessId";
            _sessionTokenKey = scope + ".SessionToken";
        }

        public bool TryLoad(out int processId, out string sessionToken)
        {
            processId = SessionState.GetInt(_processIdKey, 0);
            sessionToken = SessionState.GetString(_sessionTokenKey, string.Empty);
            return processId > 0 && !string.IsNullOrEmpty(sessionToken);
        }

        public void Save(int processId, string sessionToken)
        {
            SessionState.SetInt(_processIdKey, processId);
            SessionState.SetString(_sessionTokenKey, sessionToken);
        }

        public void Clear()
        {
            SessionState.EraseInt(_processIdKey);
            SessionState.EraseString(_sessionTokenKey);
        }
    }

    internal sealed class DawnTodAiClock : IDawnTodAiClock
    {
        public DateTime UtcNow => DateTime.UtcNow;

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }

    internal sealed class DawnTodAiEditorEnvironment : IDawnTodAiEditorEnvironment
    {
        public RuntimePlatform Platform => Application.platform;
        public int CurrentProcessId => Process.GetCurrentProcess().Id;
    }

    internal sealed class DawnTodAiServiceManager : IDawnTodAiServiceControl, IDisposable
    {
        private static readonly Lazy<DawnTodAiServiceManager> SharedInstance =
            new Lazy<DawnTodAiServiceManager>(CreateDefault);

        private readonly DawnTodAiServicePaths _paths;
        private readonly string _pathResolutionErrorCode;
        private readonly string _pathResolutionErrorMessage;
        private readonly IDawnTodAiProcessLauncher _processLauncher;
        private readonly IDawnTodAiProcessLocator _processLocator;
        private readonly IDawnTodAiHttpClientFactory _httpClientFactory;
        private readonly IDawnTodAiSessionStore _sessionStore;
        private readonly IDawnTodAiPortProbe _portProbe;
        private readonly IDawnTodAiClock _clock;
        private readonly IDawnTodAiEditorEnvironment _environment;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);

        private IDawnTodAiProcessHandle _process;
        private IDawnTodAiHttpClient _httpClient;
        private CancellationTokenSource _operationCancellation;
        private bool _preparingForReload;
        private bool _disposed;

        public static DawnTodAiServiceManager Shared => SharedInstance.Value;
        public DawnTodAiServiceState State { get; private set; } = DawnTodAiServiceState.Stopped;
        public string LastErrorCode { get; private set; }
        public string LastErrorMessage { get; private set; }
        public bool IsReady => State == DawnTodAiServiceState.Ready && _httpClient != null;
        public IDawnTodAiHttpClient HttpClient => IsReady ? _httpClient : null;

        public event Action<DawnTodAiServiceState> StateChanged;

        internal DawnTodAiServiceManager(
            DawnTodAiServicePaths paths,
            IDawnTodAiProcessLauncher processLauncher,
            IDawnTodAiProcessLocator processLocator,
            IDawnTodAiHttpClientFactory httpClientFactory,
            IDawnTodAiSessionStore sessionStore,
            IDawnTodAiPortProbe portProbe,
            IDawnTodAiClock clock,
            IDawnTodAiEditorEnvironment environment,
            string pathResolutionErrorCode = null,
            string pathResolutionErrorMessage = null)
        {
            _paths = paths;
            _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
            _processLocator = processLocator ?? throw new ArgumentNullException(nameof(processLocator));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _portProbe = portProbe ?? throw new ArgumentNullException(nameof(portProbe));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _pathResolutionErrorCode = pathResolutionErrorCode;
            _pathResolutionErrorMessage = pathResolutionErrorMessage;
        }

        public async Task<DawnTodAiServiceOperationResult> StartAsync()
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync();
            try
            {
                if (State == DawnTodAiServiceState.Ready)
                {
                    return DawnTodAiServiceOperationResult.Success();
                }

                if (State == DawnTodAiServiceState.Starting ||
                    State == DawnTodAiServiceState.Stopping)
                {
                    return DawnTodAiServiceOperationResult.Failed(
                        "SERVICE_BUSY",
                        "A service lifecycle operation is already running.");
                }

                _preparingForReload = false;
                SetState(DawnTodAiServiceState.Starting);
                DawnTodAiPreflightResult preflight = ValidatePreflight();
                if (!preflight.IsValid)
                {
                    return FailStart(preflight.ErrorCode, preflight.ErrorMessage);
                }

                if (_sessionStore.TryLoad(out int storedProcessId, out string storedSessionToken))
                {
                    DawnTodAiServiceOperationResult restoreResult;
                    try
                    {
                        restoreResult = await RestoreStoredSessionAsync(
                            preflight,
                            storedProcessId,
                            storedSessionToken);
                    }
                    catch (OperationCanceledException)
                    {
                        if (_preparingForReload)
                        {
                            return DawnTodAiServiceOperationResult.Failed(
                                "DOMAIN_RELOAD",
                                "Service startup will be restored after the assembly reload.");
                        }

                        return FailStart("START_CANCELLED", "Service startup was cancelled.");
                    }

                    if (restoreResult.IsSuccess)
                    {
                        return restoreResult;
                    }

                    SetState(DawnTodAiServiceState.Starting);
                }

                if (!_portProbe.IsAvailable())
                {
                    return FailStart(
                        "PORT_IN_USE",
                        $"Loopback port {DawnTodAiProtocol.Port} is already in use.");
                }

                string sessionToken = CreateSessionToken();
                try
                {
                    _process = _processLauncher.Start(
                        _paths,
                        sessionToken,
                        _environment.CurrentProcessId);
                }
                catch (Exception)
                {
                    return FailStart("PROCESS_START_FAILED", "The local service process failed to start.");
                }

                _sessionStore.Save(_process.Id, sessionToken);
                _httpClient = _httpClientFactory.Create(sessionToken);
                ReplaceOperationCancellation();

                DawnTodAiHealthResult health;
                try
                {
                    health = await WaitForHealthAsync(
                        preflight.SkillHash,
                        TimeSpan.FromSeconds(5),
                        _operationCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    if (_preparingForReload)
                    {
                        return DawnTodAiServiceOperationResult.Failed(
                            "DOMAIN_RELOAD",
                            "Service startup will be restored after the assembly reload.");
                    }

                    return FailStart("START_CANCELLED", "Service startup was cancelled.");
                }

                if (!health.IsReady)
                {
                    return FailStart(health.ErrorCode, health.ErrorMessage);
                }

                LastErrorCode = null;
                LastErrorMessage = null;
                SetState(DawnTodAiServiceState.Ready);
                return DawnTodAiServiceOperationResult.Success();
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task<DawnTodAiServiceOperationResult> StopAsync()
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync();
            try
            {
                if (State == DawnTodAiServiceState.Stopped && _process == null)
                {
                    _sessionStore.Clear();
                    return DawnTodAiServiceOperationResult.Success();
                }

                SetState(DawnTodAiServiceState.Stopping);
                ReplaceOperationCancellation();
                if (_httpClient != null)
                {
                    using (var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await _httpClient.ShutdownAsync(shutdownTimeout.Token);
                    }
                }

                DateTime deadline = _clock.UtcNow.AddSeconds(2);
                while (_process != null && !_process.HasExited && _clock.UtcNow < deadline)
                {
                    await _clock.Delay(TimeSpan.FromMilliseconds(50), _operationCancellation.Token);
                }

                if (_process != null && !_process.HasExited)
                {
                    if (!DawnTodAiProcessIdentity.MatchesExpectedExecutable(
                            _process,
                            _paths.ExecutablePath))
                    {
                        LastErrorCode = "PROCESS_IDENTITY_MISMATCH";
                        LastErrorMessage = "The stored PID does not belong to the package service executable.";
                        SetState(DawnTodAiServiceState.Error);
                        return DawnTodAiServiceOperationResult.Failed(
                            LastErrorCode,
                            LastErrorMessage);
                    }

                    _process.Kill();
                }

                ReleaseSession(true);
                LastErrorCode = null;
                LastErrorMessage = null;
                SetState(DawnTodAiServiceState.Stopped);
                return DawnTodAiServiceOperationResult.Success();
            }
            catch (OperationCanceledException)
            {
                return DawnTodAiServiceOperationResult.Failed(
                    "STOP_CANCELLED",
                    "Service shutdown was cancelled.");
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task<DawnTodAiServiceOperationResult> RestartAsync()
        {
            DawnTodAiServiceOperationResult stop = await StopAsync();
            return stop.IsSuccess ? await StartAsync() : stop;
        }

        public async Task<DawnTodAiServiceOperationResult> TryRestoreSessionAsync()
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync();
            try
            {
                if (!_sessionStore.TryLoad(out int processId, out string sessionToken))
                {
                    SetState(DawnTodAiServiceState.Stopped);
                    return DawnTodAiServiceOperationResult.Success();
                }

                SetState(DawnTodAiServiceState.Starting);
                DawnTodAiPreflightResult preflight = ValidatePreflight();
                return await RestoreStoredSessionAsync(preflight, processId, sessionToken);
            }
            catch (OperationCanceledException)
            {
                SetState(DawnTodAiServiceState.Stopped);
                return DawnTodAiServiceOperationResult.Failed(
                    "RESTORE_CANCELLED",
                    "Service reconnection was cancelled.");
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public void PrepareForAssemblyReload()
        {
            if (_disposed)
            {
                return;
            }

            _preparingForReload = true;
            _operationCancellation?.Cancel();
            _httpClient?.Dispose();
            _httpClient = null;
            _process?.Dispose();
            _process = null;
        }

        public void TerminateForEditorQuit()
        {
            if (_disposed)
            {
                return;
            }

            _operationCancellation?.Cancel();
            KillProcessIfSafe();
            ReleaseSession(true);
            SetState(DawnTodAiServiceState.Stopped);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _httpClient?.Dispose();
            _process?.Dispose();
            _lifecycleGate.Dispose();
        }

        private static DawnTodAiServiceManager CreateDefault()
        {
            DawnTodAiServicePaths.TryResolveCurrentPackage(
                out DawnTodAiServicePaths paths,
                out string errorCode,
                out string errorMessage);
            return new DawnTodAiServiceManager(
                paths,
                new DawnTodAiProcessLauncher(),
                new DawnTodAiProcessLocator(),
                new DawnTodAiHttpClientFactory(),
                new DawnTodAiSessionStore(),
                new DawnTodAiPortProbe(),
                new DawnTodAiClock(),
                new DawnTodAiEditorEnvironment(),
                errorCode,
                errorMessage);
        }

        private DawnTodAiPreflightResult ValidatePreflight()
        {
            if (_paths == null)
            {
                return DawnTodAiPreflightResult.Invalid(
                    _pathResolutionErrorCode ?? "PACKAGE_ROOT_NOT_FOUND",
                    _pathResolutionErrorMessage ?? "The DawnTOD package paths are unavailable.");
            }

            return DawnTodAiServicePreflight.Validate(_paths, _environment.Platform);
        }

        private async Task<DawnTodAiServiceOperationResult> RestoreStoredSessionAsync(
            DawnTodAiPreflightResult preflight,
            int processId,
            string sessionToken)
        {
            if (!preflight.IsValid ||
                !_processLocator.TryGet(processId, out _process) ||
                !DawnTodAiProcessIdentity.MatchesExpectedExecutable(
                    _process,
                    _paths.ExecutablePath))
            {
                string code = preflight.IsValid ? "SESSION_PROCESS_INVALID" : preflight.ErrorCode;
                string message = preflight.IsValid
                    ? "The previous service process could not be safely reconnected."
                    : preflight.ErrorMessage;
                ReleaseSession(true);
                LastErrorCode = code;
                LastErrorMessage = message;
                SetState(DawnTodAiServiceState.Stopped);
                return DawnTodAiServiceOperationResult.Failed(code, message);
            }

            _httpClient = _httpClientFactory.Create(sessionToken);
            ReplaceOperationCancellation();
            DawnTodAiHealthResult health = await WaitForHealthAsync(
                preflight.SkillHash,
                TimeSpan.FromSeconds(2),
                _operationCancellation.Token);
            if (!health.IsReady)
            {
                KillProcessIfSafe();
                ReleaseSession(true);
                LastErrorCode = health.ErrorCode;
                LastErrorMessage = health.ErrorMessage;
                SetState(DawnTodAiServiceState.Stopped);
                return DawnTodAiServiceOperationResult.Failed(
                    health.ErrorCode,
                    health.ErrorMessage);
            }

            LastErrorCode = null;
            LastErrorMessage = null;
            SetState(DawnTodAiServiceState.Ready);
            return DawnTodAiServiceOperationResult.Success();
        }

        private async Task<DawnTodAiHealthResult> WaitForHealthAsync(
            string expectedSkillHash,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            DateTime deadline = _clock.UtcNow.Add(timeout);
            DawnTodAiHealthResult last = DawnTodAiHealthResult.Failed(
                "START_TIMEOUT",
                "The service did not become ready before the timeout.");

            while (_clock.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_process == null || _process.HasExited)
                {
                    string exitCode = _process?.ExitCode.HasValue == true
                        ? _process.ExitCode.Value.ToString()
                        : "unknown";
                    return DawnTodAiHealthResult.Failed(
                        "PROCESS_EXITED",
                        "The service process exited before becoming ready (exit code " + exitCode + ").");
                }

                using (var requestTimeout =
                       CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    requestTimeout.CancelAfter(TimeSpan.FromMilliseconds(750));
                    DawnTodAiHttpResult response = await _httpClient
                        .GetStatusAsync(requestTimeout.Token);
                    last = DawnTodAiProtocolValidator.ValidateHealth(response, expectedSkillHash);
                }

                if (last.IsReady || !last.IsRetryable)
                {
                    return last;
                }

                await _clock.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }

            return DawnTodAiHealthResult.Failed(
                "START_TIMEOUT",
                "The service did not become ready within the configured timeout. Last error: " +
                (last.ErrorCode ?? "unknown") + ".");
        }

        private DawnTodAiServiceOperationResult FailStart(string code, string message)
        {
            KillProcessIfSafe();
            ReleaseSession(true);
            LastErrorCode = code;
            LastErrorMessage = message;
            SetState(DawnTodAiServiceState.Error);
            return DawnTodAiServiceOperationResult.Failed(code, message);
        }

        private bool KillProcessIfSafe()
        {
            if (_process == null || _process.HasExited)
            {
                return true;
            }

            if (_paths == null || !DawnTodAiProcessIdentity.MatchesExpectedExecutable(
                    _process,
                    _paths.ExecutablePath))
            {
                return false;
            }

            _process.Kill();
            return true;
        }

        private void ReleaseSession(bool clearStore)
        {
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            _httpClient?.Dispose();
            _httpClient = null;
            _process?.Dispose();
            _process = null;
            if (clearStore)
            {
                _sessionStore.Clear();
            }
        }

        private void ReplaceOperationCancellation()
        {
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _operationCancellation = new CancellationTokenSource();
        }

        private void SetState(DawnTodAiServiceState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            StateChanged?.Invoke(state);
        }

        private static string CreateSessionToken()
        {
            var bytes = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DawnTodAiServiceManager));
            }
        }
    }
}
