using System;
using System.Threading;
using System.Threading.Tasks;
using DawnTOD;

namespace DawnTODEditor.AI
{
    internal enum DawnTodAiRequestStage
    {
        Analyzing,
        Validating,
        Applying
    }

    internal enum DawnTodAiAnalyzeStatus
    {
        Applied,
        NoChanges,
        Cancelled,
        Stale,
        Failed
    }

    internal sealed class DawnTodAiAnalyzeResult
    {
        public DawnTodAiAnalyzeStatus Status { get; }
        public string RequestId { get; }
        public string RawJson { get; }
        public WeatherIntentApplyResult ApplyResult { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public bool IsSuccess => Status == DawnTodAiAnalyzeStatus.Applied ||
                                 Status == DawnTodAiAnalyzeStatus.NoChanges;

        private DawnTodAiAnalyzeResult(
            DawnTodAiAnalyzeStatus status,
            string requestId,
            string rawJson,
            WeatherIntentApplyResult applyResult,
            string errorCode,
            string errorMessage)
        {
            Status = status;
            RequestId = requestId;
            RawJson = rawJson;
            ApplyResult = applyResult;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public static DawnTodAiAnalyzeResult Completed(
            string requestId,
            string rawJson,
            WeatherIntentApplyResult applyResult)
        {
            DawnTodAiAnalyzeStatus status = applyResult.DidApply
                ? DawnTodAiAnalyzeStatus.Applied
                : DawnTodAiAnalyzeStatus.NoChanges;
            return new DawnTodAiAnalyzeResult(
                status,
                requestId,
                rawJson,
                applyResult,
                null,
                null);
        }

        public static DawnTodAiAnalyzeResult Cancelled(string requestId, string rawJson = null)
        {
            return new DawnTodAiAnalyzeResult(
                DawnTodAiAnalyzeStatus.Cancelled,
                requestId,
                rawJson,
                null,
                "REQUEST_CANCELLED",
                "The analysis request was cancelled.");
        }

        public static DawnTodAiAnalyzeResult Stale(string requestId, string rawJson)
        {
            return new DawnTodAiAnalyzeResult(
                DawnTodAiAnalyzeStatus.Stale,
                requestId,
                rawJson,
                null,
                "STALE_RESPONSE",
                "The response no longer belongs to the active request.");
        }

        public static DawnTodAiAnalyzeResult Failed(
            string requestId,
            string errorCode,
            string errorMessage,
            string rawJson = null)
        {
            return new DawnTodAiAnalyzeResult(
                DawnTodAiAnalyzeStatus.Failed,
                requestId,
                rawJson,
                null,
                errorCode,
                errorMessage);
        }
    }

    internal interface IDawnTodAiRequestCoordinator
    {
        bool HasActiveRequest { get; }
        string ActiveRequestId { get; }
        Task<DawnTodAiAnalyzeResult> AnalyzeAsync(
            string userInput,
            DawnWeatherController controller,
            float capturedHour,
            Action<DawnTodAiRequestStage> onStageChanged = null);
        Task<DawnTodAiAnalyzeResult> CancelCurrentAsync();
    }

    internal sealed class DawnTodAiRequestCoordinator : IDawnTodAiRequestCoordinator
    {
        private readonly IDawnTodAiRequestService _service;
        private readonly IDawnTodAiMainThreadDispatcher _dispatcher;
        private readonly object _gate = new object();
        private ActiveRequest _active;

        public bool HasActiveRequest
        {
            get
            {
                lock (_gate)
                {
                    return _active != null;
                }
            }
        }

        public string ActiveRequestId
        {
            get
            {
                lock (_gate)
                {
                    return _active?.Request.RequestId;
                }
            }
        }

        public DawnTodAiRequestCoordinator(
            IDawnTodAiRequestService service,
            IDawnTodAiMainThreadDispatcher dispatcher)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public async Task<DawnTodAiAnalyzeResult> AnalyzeAsync(
            string userInput,
            DawnWeatherController controller,
            float capturedHour,
            Action<DawnTodAiRequestStage> onStageChanged = null)
        {
            if (!_service.IsReady || _service.HttpClient == null)
            {
                return DawnTodAiAnalyzeResult.Failed(
                    null,
                    "SERVICE_NOT_READY",
                    "The local AI service is not ready.");
            }

            WeatherIntentAnalyzeRequestBuildResult built =
                WeatherIntentAnalyzeRequestBuilder.Build(userInput, controller, capturedHour);
            if (!built.IsValid)
            {
                return DawnTodAiAnalyzeResult.Failed(
                    null,
                    built.ErrorCode,
                    built.ErrorMessage);
            }

            var active = new ActiveRequest(built.Request);
            lock (_gate)
            {
                if (_active != null)
                {
                    active.Dispose();
                    return DawnTodAiAnalyzeResult.Failed(
                        built.Request.RequestId,
                        "REQUEST_BUSY",
                        "Another analysis request is already active.");
                }

                _active = active;
            }

            DawnTodAiHttpResult response;
            try
            {
                NotifyStage(onStageChanged, DawnTodAiRequestStage.Analyzing);
                response = await _service.HttpClient.AnalyzeAsync(
                    built.Request.Json,
                    active.TransportCancellation.Token);
            }
            catch (Exception)
            {
                response = DawnTodAiHttpResult.Failure(
                    "ANALYZE_FAILED",
                    "The analysis request failed.");
            }

            try
            {
                return await _dispatcher.RunAsync(
                    () => CompleteOnMainThread(active, response, onStageChanged));
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_active, active))
                    {
                        _active = null;
                    }
                }

                active.Dispose();
            }
        }

        public async Task<DawnTodAiAnalyzeResult> CancelCurrentAsync()
        {
            ActiveRequest active;
            IDawnTodAiHttpClient client;
            lock (_gate)
            {
                active = _active;
                if (active == null)
                {
                    return DawnTodAiAnalyzeResult.Failed(
                        null,
                        "NO_ACTIVE_REQUEST",
                        "There is no active analysis request to cancel.");
                }

                active.Cancelled = true;
                client = _service.HttpClient;
            }

            if (client != null)
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                {
                    await client.CancelAsync(active.Request.RequestId, timeout.Token);
                }
            }

            active.TransportCancellation.Cancel();
            lock (_gate)
            {
                if (ReferenceEquals(_active, active))
                {
                    _active = null;
                }
            }

            return DawnTodAiAnalyzeResult.Cancelled(active.Request.RequestId);
        }

        private DawnTodAiAnalyzeResult CompleteOnMainThread(
            ActiveRequest active,
            DawnTodAiHttpResult response,
            Action<DawnTodAiRequestStage> onStageChanged)
        {
            if (active.Cancelled)
            {
                return DawnTodAiAnalyzeResult.Cancelled(
                    active.Request.RequestId,
                    response?.Body);
            }

            lock (_gate)
            {
                if (!ReferenceEquals(_active, active))
                {
                    return DawnTodAiAnalyzeResult.Stale(
                        active.Request.RequestId,
                        response?.Body);
                }
            }

            NotifyStage(onStageChanged, DawnTodAiRequestStage.Validating);
            if (!DawnTodAiProtocolValidator.TryParseAnalyzeEnvelope(
                    response,
                    active.Request.RequestId,
                    out WeatherIntentPatch patch,
                    out string rawJson,
                    out string errorCode,
                    out string errorMessage))
            {
                return DawnTodAiAnalyzeResult.Failed(
                    active.Request.RequestId,
                    errorCode,
                    errorMessage,
                    rawJson);
            }

            NotifyStage(onStageChanged, DawnTodAiRequestStage.Applying);
            WeatherIntentApplyResult applyResult = WeatherPresetPatchApplier.Apply(
                active.Request.Target,
                patch);
            if (!applyResult.IsSuccess)
            {
                return DawnTodAiAnalyzeResult.Failed(
                    active.Request.RequestId,
                    applyResult.ErrorCode,
                    applyResult.ErrorMessage,
                    rawJson);
            }

            return DawnTodAiAnalyzeResult.Completed(
                active.Request.RequestId,
                rawJson,
                applyResult);
        }

        private static void NotifyStage(
            Action<DawnTodAiRequestStage> callback,
            DawnTodAiRequestStage stage)
        {
            try
            {
                callback?.Invoke(stage);
            }
            catch (Exception)
            {
                // UI progress callbacks must never alter request completion semantics.
            }
        }

        private sealed class ActiveRequest : IDisposable
        {
            public WeatherIntentAnalyzeRequest Request { get; }
            public CancellationTokenSource TransportCancellation { get; } =
                new CancellationTokenSource();
            public bool Cancelled { get; set; }

            public ActiveRequest(WeatherIntentAnalyzeRequest request)
            {
                Request = request;
            }

            public void Dispose()
            {
                TransportCancellation.Dispose();
            }
        }
    }
}
