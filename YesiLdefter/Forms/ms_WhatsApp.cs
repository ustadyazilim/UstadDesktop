using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using YesiLdefter.Codes;
using Tkn_Variable;
using Tkn_UstadAPI;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace YesiLdefter.Forms
{
    public partial class ms_WhatsApp : Form
    {
        private WhatsAppApiClient _apiClient;
        private Timer _refreshTimer;
        private string _selectedConversationId;
        private string _selectedUserPhone;
        private string _selectedUserName;
        private DateTime _lastRefresh;
        private bool _webViewReady;
        private string _pendingSearch;
        private bool _pendingFilterUnread;
        private long _lastRefreshLatencyMs;
        private string _backendBaseUrl;
        private const int REFRESH_INTERVAL = 10000;

        public ms_WhatsApp()
        {
            InitializeComponent();
            InitializeForm();
        }

        private void InitializeForm()
        {
            if (string.IsNullOrEmpty(v.tUser.JwtToken))
            {
                MessageBox.Show(
                    "JWT token bulunamadı. Lütfen tekrar giriş yapın.",
                    "Kimlik Doğrulama Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                this.DialogResult = DialogResult.Cancel;
                return;
            }

            if (string.IsNullOrEmpty(v.tMainFirm.FirmGuid))
            {
                MessageBox.Show(
                    "Firma bilgisi bulunamadı. Lütfen tekrar giriş yapın.",
                    "Firma Bilgisi Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                this.DialogResult = DialogResult.Cancel;
                return;
            }

            bool isProd = string.Equals(tApiConfig.GetEnvironment(), tApiConfig.ENV_PRODUCTION, StringComparison.OrdinalIgnoreCase);
            InitializeApiClient(tApiConfig.GetWhatsAppApiBaseUrl());

            _refreshTimer = new Timer();
            _refreshTimer.Interval = REFRESH_INTERVAL;
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            this.Load += Ms_WhatsApp_Load;
            this.FormClosing += Ms_WhatsApp_FormClosing;
            _lastRefresh = DateTime.Now;
        }

        private void InitializeApiClient(string baseUrl)
        {
            _backendBaseUrl = baseUrl ?? tApiConfig.GetWhatsAppApiBaseUrl();
            try
            {
                _apiClient?.Dispose();
                _apiClient = new WhatsAppApiClient(_backendBaseUrl, v.tUser.JwtToken, v.tMainFirm.FirmGuid);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"API istemcisi başlatılamadı: {ex.Message}",
                    "Başlatma Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        public void UpdateToken(string newToken)
        {
            if (_apiClient != null && !string.IsNullOrEmpty(newToken))
            {
                try
                {
                    _apiClient.UpdateToken(newToken);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Token update error: {ex.Message}");
                }
            }
        }

        private async void Ms_WhatsApp_Load(object sender, EventArgs e)
        {
            try
            {
                await InitializeWebViewAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"WebView başlatılamadı: {ex.Message}",
                    "Yükleme Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void Ms_WhatsApp_FormClosing(object sender, FormClosingEventArgs e)
        {
            _refreshTimer?.Stop();
            _refreshTimer?.Dispose();
            _apiClient?.Dispose();
        }

        private async Task InitializeWebViewAsync()
        {
            if (webView == null) return;

            await webView.EnsureCoreWebView2Async(null);
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            string html = LoadWhatsAppTemplate();
            webView.CoreWebView2.NavigateToString(html);
        }

        private string LoadWhatsAppTemplate()
        {
            var asm = Assembly.GetExecutingAssembly();
            string[] names = asm.GetManifestResourceNames();
            string resourceName = names.FirstOrDefault(n =>
                n.EndsWith("WhatsAppTemplate.html", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Templates") && n.Contains("WhatsApp") && n.EndsWith(".html"));

            if (!string.IsNullOrEmpty(resourceName))
            {
                using (var stream = asm.GetManifestResourceStream(resourceName))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }

            string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Forms", "Templates", "WhatsAppTemplate.html");
            if (File.Exists(fallback))
                return File.ReadAllText(fallback, Encoding.UTF8);

            fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates", "WhatsAppTemplate.html");
            if (File.Exists(fallback))
                return File.ReadAllText(fallback, Encoding.UTF8);

            return "<!DOCTYPE html><html><body style='font-family:Segoe UI;padding:20px;'><p>WhatsAppTemplate.html bulunamadı.</p></body></html>";
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(raw)) return;

                var payload = JObject.Parse(raw);
                string action = payload["action"]?.ToString();

                switch (action)
                {
                    case "send":
                        HandleSendFromWeb(payload);
                        break;
                    case "refresh":
                        _ = RefreshDataAsync();
                        break;
                    case "selectConversation":
                        HandleSelectConversationFromWeb(payload["conversationId"]?.ToString());
                        break;
                    case "search":
                        _pendingSearch = payload["search"]?.ToString() ?? "";
                        _ = LoadConversationsAsync();
                        break;
                    case "filterUnread":
                        _pendingFilterUnread = payload["filterUnread"]?.ToObject<bool>() ?? false;
                        _ = LoadConversationsAsync();
                        break;
                    case "changeEnvironment":
                        HandleChangeEnvironmentFromWeb(payload["env"]?.ToString());
                        break;
                    case "newChat":
                        // Modal is opened by JS; no C# action needed except optional prefill
                        break;
                    case "webViewReady":
                        _webViewReady = true;
                        _ = RefreshDataAsync();
                        break;
                    default:
                        System.Diagnostics.Debug.WriteLine($"[ms_WhatsApp] Unknown action: {action}");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ms_WhatsApp] WebMessage error: {ex.Message}");
                PushErrorToWebView(ex.Message);
            }
        }

        private async void HandleSendFromWeb(JObject payload)
        {
            string userPhone = payload["userPhone"]?.ToString()?.Trim();
            string message = payload["message"]?.ToString()?.Trim();
            bool isAI = payload["isAI"]?.ToObject<bool>() ?? false;

            if (string.IsNullOrWhiteSpace(userPhone) || string.IsNullOrWhiteSpace(message))
            {
                PushErrorToWebView("Telefon ve mesaj gerekli.");
                return;
            }

            if (_apiClient == null)
            {
                PushErrorToWebView("API istemcisi başlatılmamış.");
                return;
            }

            try
            {
                var response = await _apiClient.SendMessage(userPhone, message, isAI);
                if (response?.Success == true)
                {
                    await RefreshDataAsync();
                    if (!string.IsNullOrEmpty(_selectedConversationId))
                        await LoadThreadAsync(_selectedConversationId);
                }
                else
                    PushErrorToWebView("Mesaj gönderilemedi.");
            }
            catch (WhatsAppApiClient.WhatsAppRateLimitException rateEx)
            {
                int retrySec = Math.Max(1, Math.Min(60, rateEx.RetryAfterSeconds));
                PushErrorToWebView($"Rate limit uyarısı: Mesajınız {retrySec} saniye içinde tekrar denenecek...");
                await Task.Delay(retrySec * 1000).ConfigureAwait(true);
                try
                {
                    var retryResponse = await _apiClient.SendMessage(userPhone, message, isAI);
                    if (retryResponse?.Success == true)
                    {
                        await RefreshDataAsync();
                        if (!string.IsNullOrEmpty(_selectedConversationId))
                            await LoadThreadAsync(_selectedConversationId);
                    }
                    else
                        PushErrorToWebView("Mesaj gönderilemedi.");
                }
                catch (Exception ex2)
                {
                    PushErrorToWebView($"Gönderme hatası: {ex2.Message}");
                }
            }
            catch (Exception ex)
            {
                PushErrorToWebView($"Gönderme hatası: {ex.Message}");
            }
        }

        private async void HandleSelectConversationFromWeb(string conversationId)
        {
            if (string.IsNullOrEmpty(conversationId)) return;
            _selectedConversationId = conversationId;
            try
            {
                await LoadThreadAsync(conversationId);
                await LoadConversationsAsync();
                await UpdateUnreadCountAsync();
                PushStatusToWebView();
            }
            catch (Exception ex)
            {
                PushErrorToWebView($"Mesajlar yüklenirken hata: {ex.Message}");
            }
        }

        private async void HandleChangeEnvironmentFromWeb(string env)
        {
            bool isProd = string.Equals(env, "prod", StringComparison.OrdinalIgnoreCase);
            tApiConfig.SetEnvironment(isProd ? tApiConfig.ENV_PRODUCTION : tApiConfig.ENV_DEVELOPMENT);
            InitializeApiClient(tApiConfig.GetWhatsAppApiBaseUrl());
            await RefreshDataAsync();
        }

        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (_apiClient == null) return;
            try
            {
                await RefreshDataAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-refresh error: {ex.Message}");
            }
        }

        private async Task RefreshDataAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await LoadConversationsAsync();
                if (!string.IsNullOrEmpty(_selectedConversationId))
                    await LoadThreadAsync(_selectedConversationId);
                await UpdateUnreadCountAsync();
                await UpdateSessionStatusAsync();
                _lastRefresh = DateTime.Now;
                _lastRefreshLatencyMs = sw.ElapsedMilliseconds;
                if (string.IsNullOrEmpty(_backendBaseUrl))
                    _backendBaseUrl = tApiConfig.GetWhatsAppApiBaseUrl();
                PushStatusToWebView();
                PushPulseToWebView(_lastRefreshLatencyMs, _lastRefresh, _backendBaseUrl);
            }
            catch
            {
                _lastRefreshLatencyMs = sw.ElapsedMilliseconds;
                PushPulseToWebView(_lastRefreshLatencyMs, _lastRefresh, _backendBaseUrl ?? tApiConfig.GetWhatsAppApiBaseUrl());
                // Silently fail during auto-refresh
            }
        }

        private async Task LoadConversationsAsync()
        {
            if (_apiClient == null) return;

            try
            {
                string search = _pendingSearch ?? "";
                bool filterUnread = _pendingFilterUnread;

                var response = await _apiClient.GetInbox(page: 1, pageSize: 100, search: search, filterUnread: filterUnread);

                if (response?.Success == true && response.Data != null)
                {
                    var payload = new
                    {
                        conversations = response.Data.Conversations ?? new List<Conversation>(),
                        total = response.Data.Total,
                        page = response.Data.Page,
                        pageSize = response.Data.PageSize,
                        message = (string)null
                    };
                    PushInboxToWebView(payload);
                }
                else
                {
                    PushInboxToWebView(new
                    {
                        conversations = new List<Conversation>(),
                        total = 0,
                        page = 1,
                        pageSize = 20,
                        message = response?.Data?.ToString() ?? "Yükleme başarısız."
                    });
                }
            }
            catch (Exception ex)
            {
                PushInboxToWebView(new
                {
                    conversations = new List<Conversation>(),
                    total = 0,
                    page = 1,
                    pageSize = 20,
                    message = ex.Message
                });
                PushErrorToWebView($"Konuşmalar yüklenirken hata: {ex.Message}");
            }
        }

        private async Task LoadThreadAsync(string conversationId)
        {
            if (_apiClient == null || string.IsNullOrEmpty(conversationId)) return;

            try
            {
                var response = await _apiClient.GetThread(conversationId);

                if (response?.Success == true && response.Data != null)
                {
                    _selectedUserPhone = response.Data.UserPhone;
                    _selectedUserName = response.Data.UserName;
                    var payload = new
                    {
                        conversationId = response.Data.ConversationId,
                        userPhone = response.Data.UserPhone,
                        userName = response.Data.UserName,
                        lastMessageAt = response.Data.LastMessageAt,
                        onWhatsApp = response.Data.OnWhatsApp,
                        messages = response.Data.Messages ?? new List<WhatsAppMessage>(),
                        total = response.Data.Total,
                        message = (string)null
                    };
                    PushThreadToWebView(payload);
                }
                else
                    PushThreadToWebView(null);
            }
            catch (Exception ex)
            {
                PushErrorToWebView($"Mesajlar yüklenirken hata: {ex.Message}");
                PushThreadToWebView(null);
            }
        }

        private async Task UpdateUnreadCountAsync()
        {
            if (_apiClient == null) return;
            try
            {
                int count = await _apiClient.GetUnreadCount();
                PushStatusToWebView(sessionStatus: null, unreadCount: count, lastSync: null);
            }
            catch { /* ignore */ }
        }

        private async Task UpdateSessionStatusAsync()
        {
            if (_apiClient == null) return;
            try
            {
                string status = await _apiClient.GetSessionStatus();
                PushStatusToWebView(sessionStatus: status, unreadCount: null, lastSync: null);
            }
            catch
            {
                PushStatusToWebView(sessionStatus: "DISCONNECTED", unreadCount: null, lastSync: null);
            }
        }

        private void PushInboxToWebView(object payload)
        {
            if (webView?.CoreWebView2 == null || !_webViewReady) return;
            try
            {
                string json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                string script = $"window.__ustadUpdateInbox && window.__ustadUpdateInbox(JSON.parse({JsonEscape(json)}));";
                webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PushInbox error: {ex.Message}");
            }
        }

        private void PushThreadToWebView(object payload)
        {
            if (webView?.CoreWebView2 == null || !_webViewReady) return;
            try
            {
                string json = payload == null ? "null" : JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                string script = payload == null
                    ? "window.__ustadUpdateThread && window.__ustadUpdateThread(null);"
                    : $"window.__ustadUpdateThread && window.__ustadUpdateThread(JSON.parse({JsonEscape(json)}));";
                webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PushThread error: {ex.Message}");
            }
        }

        private string _lastStatusSession;
        private int? _lastStatusUnread;
        private DateTime? _lastStatusSync;

        private void PushStatusToWebView(string sessionStatus = null, int? unreadCount = null, DateTime? lastSync = null)
        {
            if (webView?.CoreWebView2 == null || !_webViewReady) return;

            string s = sessionStatus ?? _lastStatusSession ?? "";
            int u = unreadCount ?? _lastStatusUnread ?? 0;
            DateTime? sync = lastSync ?? _lastStatusSync ?? _lastRefresh;

            if (sessionStatus != null) _lastStatusSession = sessionStatus;
            if (unreadCount != null) _lastStatusUnread = unreadCount;
            if (lastSync != null) _lastStatusSync = lastSync;
            else _lastStatusSync = _lastRefresh;

            try
            {
                string syncArg = sync.HasValue ? JsonEscape(sync.Value.ToUniversalTime().ToString("o")) : "null";
                string script = $"window.__ustadUpdateStatus && window.__ustadUpdateStatus({JsonEscape(s)}, {u}, {syncArg});";
                webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PushStatus error: {ex.Message}");
            }
        }

        private void PushErrorToWebView(string message)
        {
            if (webView?.CoreWebView2 == null || !_webViewReady) return;
            try
            {
                string script = $"window.__ustadShowError && window.__ustadShowError({JsonEscape(message ?? "Hata")});";
                webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PushError error: {ex.Message}");
            }
        }

        private void PushPulseToWebView(long latencyMs, DateTime? lastSync, string backendBaseUrl)
        {
            if (webView?.CoreWebView2 == null || !_webViewReady) return;
            try
            {
                string syncArg = lastSync.HasValue ? JsonEscape(lastSync.Value.ToUniversalTime().ToString("o")) : "null";
                string urlArg = JsonEscape(backendBaseUrl ?? "");
                string script = $"window.__ustadUpdatePulse && window.__ustadUpdatePulse({latencyMs}, {syncArg}, {urlArg});";
                webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PushPulse error: {ex.Message}");
            }
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return "null";
            return JsonConvert.SerializeObject(s);
        }
    }
}
