using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tkn_Variable;

namespace YesiLdefter.Codes
{
    /// <summary>
    /// WhatsApp API client for ustad-web-api WhatsApp endpoints
    /// </summary>
    public class WhatsAppApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private string _jwtToken;
        private string _firmGUID;
        private bool _disposed;

        // Constants
        private const string API_PATH = "/api/operations/whatsapp";
        public const string TEST_PHONE = "+905306437498";

        public WhatsAppApiClient(string baseUrl, string jwtToken, string firmGUID)
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new ArgumentException("Base URL cannot be null or empty", nameof(baseUrl));
            if (string.IsNullOrEmpty(jwtToken))
                throw new ArgumentException("JWT token cannot be null or empty", nameof(jwtToken));
            if (string.IsNullOrEmpty(firmGUID))
                throw new ArgumentException("Firm GUID cannot be null or empty", nameof(firmGUID));

            _baseUrl = baseUrl.TrimEnd('/');
            _jwtToken = jwtToken;
            _firmGUID = firmGUID;

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
            _httpClient.DefaultRequestHeaders.Add("X-Firm-GUID", _firmGUID);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public void UpdateToken(string newToken)
        {
            _jwtToken = newToken;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
        }

        public async Task<InboxResponse> GetInbox(int page = 1, int pageSize = 20, string search = null, bool filterUnread = false)
        {
            return await ExecuteApiCall(async () =>
            {
                var queryParams = new List<string>
                {
                    $"page={page}",
                    $"pageSize={pageSize}"
                };

                if (!string.IsNullOrEmpty(search))
                    queryParams.Add($"search={Uri.EscapeDataString(search)}");
                if (filterUnread)
                    queryParams.Add("filterUnread=true");

                var url = $"{_baseUrl}{API_PATH}/inbox?{string.Join("&", queryParams)}";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<InboxResponse>(json, GetJsonSettings());
            }, "GetInbox");
        }

        public async Task<ThreadResponse> GetThread(string conversationId)
        {
            return await ExecuteApiCall(async () =>
            {
                var url = $"{_baseUrl}{API_PATH}/threads/{Uri.EscapeDataString(conversationId)}";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ThreadResponse>(json, GetJsonSettings());
            }, "GetThread");
        }

        /// <summary>
        /// Thrown when the API returns 429 Too Many Requests. RetryAfterSeconds is from Retry-After header or default 5.
        /// </summary>
        public class WhatsAppRateLimitException : Exception
        {
            public int RetryAfterSeconds { get; }

            public WhatsAppRateLimitException(int retryAfterSeconds = 5)
                : base($"Rate limit reached. Retry after {retryAfterSeconds}s.")
            {
                RetryAfterSeconds = retryAfterSeconds;
            }
        }

        public async Task<SendMessageResponse> SendMessage(string userPhone, string message, bool isAI = false)
        {
            var url = $"{_baseUrl}{API_PATH}/send";
            var payload = new
            {
                userPhone = NormalizePhone(userPhone),
                message = message,
                isAI = isAI
            };

            var json = JsonConvert.SerializeObject(payload, GetJsonSettings());
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);

            if (response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                int retryAfter = 5;
                if (response.Headers.RetryAfter?.Delta.HasValue == true)
                    retryAfter = (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                else if (response.Headers.RetryAfter?.Date.HasValue == true)
                    retryAfter = Math.Max(1, (int)(response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
                if (retryAfter < 1) retryAfter = 5;
                throw new WhatsAppRateLimitException(retryAfter);
            }

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeObject<SendMessageResponse>(responseJson, GetJsonSettings());
        }

        public async Task<int> GetUnreadCount()
        {
            try
            {
                return await ExecuteApiCall(async () =>
                {
                    var url = $"{_baseUrl}/api/operations/data/whatsapp/unread-count";
                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(json, GetJsonSettings());

                    if (result != null && result.ContainsKey("unreadCount"))
                    {
                        if (result["unreadCount"] is long longValue)
                            return (int)longValue;
                        if (result["unreadCount"] is int intValue)
                            return intValue;
                        if (int.TryParse(result["unreadCount"]?.ToString(), out int parsedValue))
                            return parsedValue;
                    }

                    return 0;
                }, "GetUnreadCount");
            }
            catch
            {
                return 0;
            }
        }

        public async Task<string> GetSessionStatus()
        {
            try
            {
                var url = $"{_baseUrl}{API_PATH}/session/status";
                var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // 404 = session not found; backend may auto-create on next call. Surface as INITIALIZING so UI shows orange "Initializing..."
                    return "INITIALIZING";
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(json, GetJsonSettings());

                if (result != null && result.ContainsKey("status"))
                    return result["status"]?.ToString() ?? "UNKNOWN";

                return "UNKNOWN";
            }
            catch (HttpRequestException ex) when (ex.Message?.Contains("404") == true)
            {
                return "INITIALIZING";
            }
            catch
            {
                return "DISCONNECTED";
            }
        }

        public async Task<CheckPhoneResponse> CheckPhone(string phone)
        {
            return await ExecuteApiCall(async () =>
            {
                var normalized = NormalizePhone(phone).TrimStart('+');
                var url = $"{_baseUrl}{API_PATH}/check/{Uri.EscapeDataString(normalized)}";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<CheckPhoneResponse>(json, GetJsonSettings());
            }, "CheckPhone");
        }

        public async Task<bool> TestConnection()
        {
            try
            {
                return await ExecuteApiCall(async () =>
                {
                    var url = $"{_baseUrl}{API_PATH}/test";
                    var response = await _httpClient.PostAsync(url, new StringContent(string.Empty));
                    response.EnsureSuccessStatusCode();
                    return true;
                }, "TestConnection");
            }
            catch
            {
                return false;
            }
        }

        public async Task<BulkSendResponse> SendBulk(string[] recipients, string message, bool isAI = false)
        {
            return await ExecuteApiCall(async () =>
            {
                var url = $"{_baseUrl}{API_PATH}/send-bulk";
                var payload = new
                {
                    recipients = recipients ?? Array.Empty<string>(),
                    message = message,
                    isAI = isAI
                };

                var json = JsonConvert.SerializeObject(payload, GetJsonSettings());
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<BulkSendResponse>(responseJson, GetJsonSettings());
            }, "SendBulk");
        }

        public string NormalizeForComparison(string phone)
        {
            if (string.IsNullOrEmpty(phone))
                return string.Empty;

            var normalized = NormalizePhone(phone);
            return normalized.StartsWith("+") ? normalized.Substring(1) : normalized;
        }

        private string NormalizePhone(string phone)
        {
            if (string.IsNullOrEmpty(phone))
                return phone;

            // Remove all non-digit characters except +
            var digits = Regex.Replace(phone, @"[^\d+]", "");

            // Remove leading + for processing
            var hasPlus = digits.StartsWith("+");
            if (hasPlus)
                digits = digits.Substring(1);

            // Turkish number normalization
            if (digits.StartsWith("0"))
                digits = "90" + digits.Substring(1);
            else if (digits.Length == 10 && !digits.StartsWith("90"))
                digits = "90" + digits;

            return "+" + digits;
        }

        private async Task<T> ExecuteApiCall<T>(Func<Task<T>> apiCall, string operationName)
        {
            try
            {
                return await apiCall();
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("401"))
            {
                throw new Exception("Authentication required. Please log in again.", ex);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                // Rate limited - wait and retry once
                await Task.Delay(5000);
                return await apiCall();
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"{operationName} failed: {ex.Message}", ex);
            }
            catch (TaskCanceledException)
            {
                throw new Exception($"{operationName} timed out. Please check your connection.");
            }
        }

        private JsonSerializerSettings GetJsonSettings()
        {
            return new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
                // Property names are explicitly mapped via [JsonProperty] attributes
            };
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }

    #region Data Models

    public class Conversation
    {
        [JsonProperty("conversationId")]
        public string ConversationId { get; set; }

        [JsonProperty("userPhone")]
        public string UserPhone { get; set; }

        [JsonProperty("userName")]
        public string UserName { get; set; }

        [JsonProperty("lastMessage")]
        public string LastMessage { get; set; }

        [JsonProperty("lastMessageAt")]
        public DateTime? LastMessageAt { get; set; }

        [JsonProperty("unreadCount")]
        public int UnreadCount { get; set; }

        [JsonProperty("firmGUID")]
        public string FirmGUID { get; set; }
    }

    public class WhatsAppMessage
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("firmGUID")]
        public string FirmGUID { get; set; }

        [JsonProperty("userPhone")]
        public string UserPhone { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("direction")]
        public string Direction { get; set; } // "incoming", "outgoing", "outgoing_ai"

        [JsonProperty("isAI")]
        public bool IsAI { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; } // "pending", "sent", "delivered", "read", "failed"

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("createdBy")]
        public string CreatedBy { get; set; }
    }

    public class InboxResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("data")]
        public InboxData Data { get; set; }
    }

    public class InboxData
    {
        [JsonProperty("conversations")]
        public List<Conversation> Conversations { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("page")]
        public int Page { get; set; }

        [JsonProperty("pageSize")]
        public int PageSize { get; set; }
    }

    public class ThreadResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("data")]
        public ThreadData Data { get; set; }
    }

    public class ThreadData
    {
        [JsonProperty("conversationId")]
        public string ConversationId { get; set; }

        [JsonProperty("userPhone")]
        public string UserPhone { get; set; }

        [JsonProperty("userName")]
        public string UserName { get; set; }

        [JsonProperty("lastMessageAt")]
        public string LastMessageAt { get; set; }

        [JsonProperty("onWhatsApp")]
        public bool OnWhatsApp { get; set; }

        [JsonProperty("messages")]
        public List<WhatsAppMessage> Messages { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }

    public class SendMessageResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("messageId")]
        public int MessageId { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }

    public class CheckPhoneResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("onWhatsApp")]
        public bool OnWhatsApp { get; set; }

        [JsonProperty("normalizedPhone")]
        public string NormalizedPhone { get; set; }
    }

    public class BulkSendResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("totalRecipients")]
        public int TotalRecipients { get; set; }

        [JsonProperty("queued")]
        public int Queued { get; set; }

        [JsonProperty("failed")]
        public int Failed { get; set; }
    }

    #endregion
}
