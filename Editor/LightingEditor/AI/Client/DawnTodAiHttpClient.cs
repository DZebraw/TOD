using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DawnTODEditor.AI
{
    internal interface IDawnTodAiHttpClient : IDisposable
    {
        Task<DawnTodAiHttpResult> GetStatusAsync(CancellationToken cancellationToken);
        Task<DawnTodAiHttpResult> AnalyzeAsync(string requestJson, CancellationToken cancellationToken);
        Task<DawnTodAiHttpResult> CancelAsync(string requestId, CancellationToken cancellationToken);
        Task<DawnTodAiHttpResult> ShutdownAsync(CancellationToken cancellationToken);
    }

    internal interface IDawnTodAiHttpClientFactory
    {
        IDawnTodAiHttpClient Create(string sessionToken);
    }

    internal sealed class DawnTodAiHttpClientFactory : IDawnTodAiHttpClientFactory
    {
        public IDawnTodAiHttpClient Create(string sessionToken)
        {
            return new DawnTodAiHttpClient(sessionToken);
        }
    }

    internal sealed class DawnTodAiHttpClient : IDawnTodAiHttpClient
    {
        private readonly HttpClient _client;
        private readonly string _sessionToken;
        private bool _disposed;

        public DawnTodAiHttpClient(string sessionToken, HttpMessageHandler handler = null)
        {
            if (string.IsNullOrEmpty(sessionToken))
            {
                throw new ArgumentException("A session token is required.", nameof(sessionToken));
            }

            _sessionToken = sessionToken;
            _client = handler == null ? new HttpClient() : new HttpClient(handler, true);
            _client.BaseAddress = new Uri(DawnTodAiProtocol.BaseUrl, UriKind.Absolute);
            _client.Timeout = Timeout.InfiniteTimeSpan;
        }

        public Task<DawnTodAiHttpResult> GetStatusAsync(CancellationToken cancellationToken)
        {
            return SendAsync(HttpMethod.Get, "status", null, cancellationToken);
        }

        public Task<DawnTodAiHttpResult> AnalyzeAsync(
            string requestJson,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                return Task.FromResult(
                    DawnTodAiHttpResult.Failure("INVALID_REQUEST", "The analysis request is empty."));
            }

            return SendAsync(HttpMethod.Post, "analyze", requestJson, cancellationToken);
        }

        public Task<DawnTodAiHttpResult> CancelAsync(
            string requestId,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(requestId, out _))
            {
                return Task.FromResult(
                    DawnTodAiHttpResult.Failure("INVALID_REQUEST_ID", "The request id is invalid."));
            }

            return SendAsync(
                HttpMethod.Post,
                "tasks/" + Uri.EscapeDataString(requestId) + "/cancel",
                null,
                cancellationToken);
        }

        public Task<DawnTodAiHttpResult> ShutdownAsync(CancellationToken cancellationToken)
        {
            return SendAsync(HttpMethod.Post, "shutdown", null, cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _client.Dispose();
        }

        private async Task<DawnTodAiHttpResult> SendAsync(
            HttpMethod method,
            string relativePath,
            string json,
            CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return DawnTodAiHttpResult.Failure(
                    "CLIENT_DISPOSED",
                    "The service HTTP client has been disposed.");
            }

            using (var request = new HttpRequestMessage(method, relativePath))
            {
                request.Headers.TryAddWithoutValidation(
                    DawnTodAiProtocol.SessionTokenHeader,
                    _sessionToken);
                if (json != null)
                {
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                try
                {
                    using (HttpResponseMessage response = await _client
                               .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                               .ConfigureAwait(false))
                    {
                        string body = response.Content == null
                            ? string.Empty
                            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return DawnTodAiHttpResult.Response(response.StatusCode, body);
                    }
                }
                catch (OperationCanceledException)
                {
                    return DawnTodAiHttpResult.Failure(
                        "REQUEST_CANCELLED",
                        "The HTTP request was cancelled.");
                }
                catch (HttpRequestException)
                {
                    return DawnTodAiHttpResult.Failure(
                        "CONNECTION_FAILED",
                        "The local service could not be reached.");
                }
                catch (Exception)
                {
                    return DawnTodAiHttpResult.Failure(
                        "HTTP_FAILED",
                        "The local service request failed.");
                }
            }
        }
    }
}
