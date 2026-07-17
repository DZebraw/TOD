using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DawnTOD;
using UnityEngine;

namespace DawnTODEditor.AI
{
    internal enum DawnTodAiHistoryStatus
    {
        Analyzing,
        Validating,
        Applying,
        Success,
        Error,
        Cancelled
    }

    internal sealed class DawnTodAiHistoryEntry
    {
        public string UserInput { get; }
        public DateTime StartedAt { get; }
        public float CapturedHour { get; }
        public string ControllerName { get; }
        public string PresetName { get; }
        public string RequestId { get; internal set; }
        public DawnTodAiHistoryStatus Status { get; internal set; }
        public IReadOnlyList<string> AppliedFields { get; internal set; } = Array.Empty<string>();
        public TimeSpan Duration { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string ErrorMessage { get; internal set; }
        public string RawJson { get; internal set; }
        public bool IsRawJsonExpanded { get; set; }

        public DawnTodAiHistoryEntry(
            string userInput,
            DateTime startedAt,
            float capturedHour,
            DawnWeatherController controller,
            DawnWeatherPreset preset)
        {
            UserInput = userInput;
            StartedAt = startedAt;
            CapturedHour = capturedHour;
            ControllerName = controller != null ? controller.name : "None";
            PresetName = preset != null ? preset.name : "None";
            Status = DawnTodAiHistoryStatus.Analyzing;
        }
    }

    internal sealed class DawnTodAiPanelController : IDisposable
    {
        private readonly IDawnTodAiServiceControl _service;
        private readonly IDawnTodAiRequestCoordinator _coordinator;
        private readonly Action _onChanged;
        private readonly List<DawnTodAiHistoryEntry> _history =
            new List<DawnTodAiHistoryEntry>();

        private DawnTodAiHistoryEntry _activeEntry;
        private bool _requestInFlight;
        private bool _serviceOperationInFlight;
        private bool _disposed;

        public IReadOnlyList<DawnTodAiHistoryEntry> History => _history;
        public DawnTodAiHistoryEntry ActiveEntry => _activeEntry;
        public bool HasActiveRequest => _requestInFlight;
        public bool IsServiceOperationInFlight => _serviceOperationInFlight;
        public IDawnTodAiServiceControl Service => _service;
        public string ServiceActionError { get; private set; }

        public DawnTodAiPanelController(
            IDawnTodAiServiceControl service,
            IDawnTodAiRequestCoordinator coordinator,
            Action onChanged)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _onChanged = onChanged;
            _service.StateChanged += OnServiceStateChanged;
        }

        public bool CanSend(
            string userInput,
            DawnWeatherController controller,
            float capturedHour,
            RuntimePlatform platform,
            out string reason)
        {
            if (platform != RuntimePlatform.WindowsEditor)
            {
                reason = "The AI assistant is available only in the Windows Unity Editor.";
                return false;
            }

            if (_service.State != DawnTodAiServiceState.Ready || !_service.IsReady)
            {
                reason = "Start the local service and wait until it is Ready.";
                return false;
            }

            if (controller == null)
            {
                reason = "Select a Weather Controller.";
                return false;
            }

            if (controller.ActivePreset == null)
            {
                reason = "The selected controller needs an Active Preset.";
                return false;
            }

            if (!IsValidHour(capturedHour))
            {
                reason = "The current Lighting Editor time is invalid.";
                return false;
            }

            if (_requestInFlight || _coordinator.HasActiveRequest)
            {
                reason = "Wait for the current request to finish or cancel it.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(userInput))
            {
                reason = "Enter a natural-language lighting instruction.";
                return false;
            }

            reason = null;
            return true;
        }

        public Task<DawnTodAiServiceOperationResult> StartServiceAsync()
        {
            return RunServiceOperationAsync(_service.StartAsync);
        }

        public Task<DawnTodAiServiceOperationResult> StopServiceAsync()
        {
            return RunServiceOperationAsync(_service.StopAsync);
        }

        public Task<DawnTodAiServiceOperationResult> RestartServiceAsync()
        {
            return RunServiceOperationAsync(_service.RestartAsync);
        }

        public async Task<DawnTodAiAnalyzeResult> SendAsync(
            string userInput,
            DawnWeatherController controller,
            float capturedHour,
            RuntimePlatform platform,
            Action pausePlayback,
            Action<DawnTodAiAnalyzeResult> onApplied)
        {
            ThrowIfDisposed();
            if (!CanSend(userInput, controller, capturedHour, platform, out string reason))
            {
                return DawnTodAiAnalyzeResult.Failed(null, "SEND_NOT_ALLOWED", reason);
            }

            string normalizedInput = userInput.Trim();
            var entry = new DawnTodAiHistoryEntry(
                normalizedInput,
                DateTime.Now,
                capturedHour,
                controller,
                controller.ActivePreset);
            _history.Add(entry);
            _activeEntry = entry;
            _requestInFlight = true;
            NotifyChanged();

            DateTime startedUtc = DateTime.UtcNow;
            DawnTodAiAnalyzeResult result;
            try
            {
                pausePlayback?.Invoke();
                Task<DawnTodAiAnalyzeResult> operation = _coordinator.AnalyzeAsync(
                    normalizedInput,
                    controller,
                    capturedHour,
                    stage => SetRequestStage(entry, stage));
                entry.RequestId = _coordinator.ActiveRequestId;
                result = await operation;
            }
            catch (Exception)
            {
                result = DawnTodAiAnalyzeResult.Failed(
                    entry.RequestId,
                    "UI_REQUEST_FAILED",
                    "The analysis request could not be completed.");
            }

            CompleteEntry(entry, result, DateTime.UtcNow - startedUtc);
            try
            {
                if (!_disposed && result.IsSuccess && result.ApplyResult?.DidApply == true)
                {
                    onApplied?.Invoke(result);
                }
            }
            catch (Exception)
            {
                // The patch is already committed; a repaint callback must not strand request state.
            }

            if (ReferenceEquals(_activeEntry, entry))
            {
                _activeEntry = null;
            }

            _requestInFlight = false;
            NotifyChanged();
            return result;
        }

        public async Task<DawnTodAiAnalyzeResult> CancelAsync()
        {
            ThrowIfDisposed();
            if (!_requestInFlight || _activeEntry == null)
            {
                return DawnTodAiAnalyzeResult.Failed(
                    null,
                    "NO_ACTIVE_REQUEST",
                    "There is no active analysis request to cancel.");
            }

            DawnTodAiAnalyzeResult result;
            try
            {
                result = await _coordinator.CancelCurrentAsync();
            }
            catch (Exception)
            {
                result = DawnTodAiAnalyzeResult.Failed(
                    _activeEntry.RequestId,
                    "CANCEL_FAILED",
                    "The active request could not be cancelled.");
            }

            if (result.Status == DawnTodAiAnalyzeStatus.Cancelled)
            {
                _activeEntry.Status = DawnTodAiHistoryStatus.Cancelled;
                _activeEntry.ErrorCode = result.ErrorCode;
                _activeEntry.ErrorMessage = result.ErrorMessage;
                NotifyChanged();
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _service.StateChanged -= OnServiceStateChanged;
            if (_requestInFlight && _coordinator.HasActiveRequest)
            {
                _ = CancelAfterDisposeAsync();
            }
        }

        private async Task<DawnTodAiServiceOperationResult> RunServiceOperationAsync(
            Func<Task<DawnTodAiServiceOperationResult>> operation)
        {
            ThrowIfDisposed();
            if (_serviceOperationInFlight)
            {
                return DawnTodAiServiceOperationResult.Failed(
                    "SERVICE_BUSY",
                    "A service operation is already running.");
            }

            _serviceOperationInFlight = true;
            ServiceActionError = null;
            NotifyChanged();
            try
            {
                DawnTodAiServiceOperationResult result = await operation();
                if (!result.IsSuccess)
                {
                    ServiceActionError = result.ErrorMessage;
                }

                return result;
            }
            catch (Exception)
            {
                ServiceActionError = "The local service operation failed.";
                return DawnTodAiServiceOperationResult.Failed(
                    "SERVICE_OPERATION_FAILED",
                    ServiceActionError);
            }
            finally
            {
                _serviceOperationInFlight = false;
                NotifyChanged();
            }
        }

        private void SetRequestStage(
            DawnTodAiHistoryEntry entry,
            DawnTodAiRequestStage stage)
        {
            if (!ReferenceEquals(_activeEntry, entry) ||
                entry.Status == DawnTodAiHistoryStatus.Cancelled)
            {
                return;
            }

            switch (stage)
            {
                case DawnTodAiRequestStage.Analyzing:
                    entry.Status = DawnTodAiHistoryStatus.Analyzing;
                    break;
                case DawnTodAiRequestStage.Validating:
                    entry.Status = DawnTodAiHistoryStatus.Validating;
                    break;
                case DawnTodAiRequestStage.Applying:
                    entry.Status = DawnTodAiHistoryStatus.Applying;
                    break;
            }

            NotifyChanged();
        }

        private static void CompleteEntry(
            DawnTodAiHistoryEntry entry,
            DawnTodAiAnalyzeResult result,
            TimeSpan duration)
        {
            entry.Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
            entry.RequestId = result.RequestId ?? entry.RequestId;
            entry.RawJson = result.RawJson;
            entry.ErrorCode = result.ErrorCode;
            entry.ErrorMessage = result.ErrorMessage;
            entry.AppliedFields = result.ApplyResult?.AppliedFields ?? Array.Empty<string>();

            if (result.Status == DawnTodAiAnalyzeStatus.Cancelled)
            {
                entry.Status = DawnTodAiHistoryStatus.Cancelled;
            }
            else if (result.IsSuccess)
            {
                entry.Status = DawnTodAiHistoryStatus.Success;
            }
            else
            {
                entry.Status = DawnTodAiHistoryStatus.Error;
            }
        }

        private async Task CancelAfterDisposeAsync()
        {
            try
            {
                await _coordinator.CancelCurrentAsync();
            }
            catch (Exception)
            {
                // Closing the panel cancels only its request; service lifetime is unchanged.
            }
        }

        private void OnServiceStateChanged(DawnTodAiServiceState state)
        {
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            if (!_disposed)
            {
                _onChanged?.Invoke();
            }
        }

        private static bool IsValidHour(float hour)
        {
            return !float.IsNaN(hour) &&
                   !float.IsInfinity(hour) &&
                   hour >= 0f &&
                   hour < 24f;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DawnTodAiPanelController));
            }
        }
    }
}
