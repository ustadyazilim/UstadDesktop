using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using YesiLdefter.Codes;
using Tkn_Variable;
using Tkn_UstadAPI;
using System.Net.Http;

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
            // Sync combo with centralized environment (Development = 0, Production = 1)
            bool isProd = string.Equals(tApiConfig.GetEnvironment(), tApiConfig.ENV_PRODUCTION, StringComparison.OrdinalIgnoreCase);
            comboBoxEnvironment.SelectedIndex = isProd ? 1 : 0;
            InitializeApiClient(tApiConfig.GetWhatsAppApiBaseUrl());
            
            // Initialize status bar
            whatsAppStatusBar.UpdateStatus("Başlatılıyor...", Color.Gray);
            whatsAppStatusBar.UpdateUnreadCount(0);
            
            _refreshTimer = new Timer();
            _refreshTimer.Interval = REFRESH_INTERVAL;
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
            this.Load += Ms_WhatsApp_Load;
            this.FormClosing += Ms_WhatsApp_FormClosing;
            dataGridViewConversations.SelectionChanged += DataGridViewConversations_SelectionChanged;
            buttonSend.Click += ButtonSend_Click;
            buttonRefresh.Click += ButtonRefresh_Click;
            comboBoxEnvironment.SelectedIndexChanged += ComboBoxEnvironment_SelectedIndexChanged;
            textBoxMessageInput.KeyDown += TextBoxMessageInput_KeyDown;
            textBoxSearch.TextChanged += TextBoxSearch_TextChanged;
            checkBoxFilterUnread.CheckedChanged += CheckBoxFilterUnread_CheckedChanged;
            buttonNewChat.Click += ButtonNewChat_Click;
            _lastRefresh = DateTime.Now;
        }

        private void InitializeApiClient(string baseUrl)
        {
            try
            {
                _apiClient?.Dispose();
                _apiClient = new WhatsAppApiClient(baseUrl, v.tUser.JwtToken, v.tMainFirm.FirmGuid);
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

        /// <summary>
        /// Update JWT token if it changes during runtime
        /// </summary>
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
                await LoadConversationsAsync();
                await UpdateUnreadCountAsync();
                await UpdateSessionStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Başlangıç verileri yüklenirken hata oluştu: {ex.Message}",
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
            try
            {
                await LoadConversationsAsync();
                if (!string.IsNullOrEmpty(_selectedConversationId))
                {
                    await LoadThreadAsync(_selectedConversationId);
                }

                await UpdateUnreadCountAsync();
                await UpdateSessionStatusAsync();

                _lastRefresh = DateTime.Now;
                UpdateStatusBar();
            }
            catch
            {
                // Silently fail during auto-refresh
            }
        }

        private async Task LoadConversationsAsync()
        {
            if (_apiClient == null) return;

            try
            {
                buttonRefresh.Enabled = false;
                string search = textBoxSearch.Text.Trim();
                bool filterUnread = checkBoxFilterUnread.Checked;

                var response = await _apiClient.GetInbox(page: 1, pageSize: 100, search: search, filterUnread: filterUnread);

                if (response?.Success == true && response.Data != null)
                {
                    // Update DataGridView
                    var conversations = response.Data.Conversations ?? new List<Conversation>();
                    dataGridViewConversations.DataSource = conversations;

                    // Format columns
                    FormatConversationsGrid();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Konuşmalar yüklenirken hata oluştu: {ex.Message}",
                    "Yükleme Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                buttonRefresh.Enabled = true;
            }
        }

        private void FormatConversationsGrid()
        {
            if (dataGridViewConversations.Columns.Count == 0) return;

            if (dataGridViewConversations.Columns["FirmGUID"] != null)
                dataGridViewConversations.Columns["FirmGUID"].Visible = false;
            if (dataGridViewConversations.Columns["ConversationId"] != null)
                dataGridViewConversations.Columns["ConversationId"].Visible = false;

            if (dataGridViewConversations.Columns["UserName"] != null)
            {
                dataGridViewConversations.Columns["UserName"].HeaderText = "İsim";
                dataGridViewConversations.Columns["UserName"].Width = 150;
            }

            if (dataGridViewConversations.Columns["UserPhone"] != null)
            {
                dataGridViewConversations.Columns["UserPhone"].HeaderText = "Telefon";
                dataGridViewConversations.Columns["UserPhone"].Width = 120;
            }

            if (dataGridViewConversations.Columns["LastMessage"] != null)
            {
                dataGridViewConversations.Columns["LastMessage"].HeaderText = "Son Mesaj";
                dataGridViewConversations.Columns["LastMessage"].Width = 250;
            }

            if (dataGridViewConversations.Columns["LastMessageAt"] != null)
            {
                dataGridViewConversations.Columns["LastMessageAt"].HeaderText = "Tarih";
                dataGridViewConversations.Columns["LastMessageAt"].Width = 120;
                dataGridViewConversations.Columns["LastMessageAt"].DefaultCellStyle.Format = "dd.MM.yyyy HH:mm";
            }

            if (dataGridViewConversations.Columns["UnreadCount"] != null)
            {
                dataGridViewConversations.Columns["UnreadCount"].HeaderText = "Okunmamış";
                dataGridViewConversations.Columns["UnreadCount"].Width = 80;
            }

            foreach (DataGridViewRow row in dataGridViewConversations.Rows)
            {
                if (row.DataBoundItem is Conversation conv && conv.UnreadCount > 0)
                {
                    row.DefaultCellStyle.Font = new Font(dataGridViewConversations.Font, FontStyle.Bold);
                    row.DefaultCellStyle.ForeColor = Color.Blue;
                }
            }
        }

        private async void DataGridViewConversations_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridViewConversations.SelectedRows.Count == 0) return;

            try
            {
                var selectedRow = dataGridViewConversations.SelectedRows[0];
                if (selectedRow.DataBoundItem is Conversation conversation)
                {
                    _selectedConversationId = conversation.ConversationId ?? conversation.UserPhone;
                    _selectedUserPhone = conversation.UserPhone;
                    _selectedUserName = conversation.UserName;

                    labelThreadHeader.Text = $"{conversation.UserName} ({conversation.UserPhone})";

                    await LoadThreadAsync(_selectedConversationId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Mesaj geçmişi yüklenirken hata oluştu: {ex.Message}",
                    "Yükleme Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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
                    DisplayMessages(response.Data.Messages ?? new List<WhatsAppMessage>());
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Mesajlar yüklenirken hata oluştu: {ex.Message}",
                    "Yükleme Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void DisplayMessages(List<WhatsAppMessage> messages)
        {
            richTextBoxMessages.Clear();

            foreach (var msg in messages.OrderBy(m => m.CreatedAt))
            {
                var color = msg.Direction == "incoming" ? Color.Blue : Color.Green;
                var prefix = msg.Direction == "incoming" ? "Kullanıcı" : "Siz";
                if (msg.IsAI)
                    prefix = "AI";

                var timeStr = msg.CreatedAt.ToString("HH:mm");
                var statusStr = msg.Status != "read" ? $" [{msg.Status}]" : "";

                richTextBoxMessages.SelectionStart = richTextBoxMessages.TextLength;
                richTextBoxMessages.SelectionLength = 0;
                richTextBoxMessages.SelectionColor = color;
                richTextBoxMessages.AppendText($"[{timeStr}] {prefix}: {msg.Message}{statusStr}\n");
                richTextBoxMessages.SelectionColor = richTextBoxMessages.ForeColor;
            }

            richTextBoxMessages.ScrollToCaret();
        }

        private async void ButtonSend_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedConversationId))
            {
                MessageBox.Show(
                    "Lütfen önce bir konuşma seçin.",
                    "Seçim Gerekli",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var message = textBoxMessageInput.Text.Trim();
            if (string.IsNullOrEmpty(message))
                return;

            if (_apiClient == null)
            {
                MessageBox.Show(
                    "API istemcisi başlatılmamış.",
                    "Hata",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            try
            {
                buttonSend.Enabled = false;
                textBoxMessageInput.Clear();

                var response = await _apiClient.SendMessage(_selectedUserPhone, message, isAI: false);

                if (response?.Success == true)
                {
                    await LoadThreadAsync(_selectedConversationId);
                    await LoadConversationsAsync();
                }
                else
                {
                    MessageBox.Show(
                        "Mesaj gönderilemedi.",
                        "Gönderme Hatası",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    textBoxMessageInput.Text = message;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Mesaj gönderilirken hata oluştu: {ex.Message}",
                    "Gönderme Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                textBoxMessageInput.Text = message;
            }
            finally
            {
                buttonSend.Enabled = true;
                textBoxMessageInput.Focus();
            }
        }

        private void TextBoxMessageInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                ButtonSend_Click(sender, e);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private async void ButtonNewChat_Click(object sender, EventArgs e)
        {
            if (_apiClient == null)
            {
                MessageBox.Show(
                    "API istemcisi başlatılmamış.",
                    "Hata",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var phone = textBoxNewPhone.Text.Trim();
            var message = textBoxMessageInput.Text.Trim();

            if (string.IsNullOrEmpty(phone))
            {
                MessageBox.Show(
                    "Lütfen telefon numarası girin.",
                    "Eksik Bilgi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                MessageBox.Show(
                    "Lütfen gönderilecek mesajı yazın.",
                    "Eksik Bilgi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                buttonNewChat.Enabled = false;
                buttonSend.Enabled = false;

                var response = await _apiClient.SendMessage(phone, message, isAI: false);

                if (response?.Success == true)
                {
                    textBoxMessageInput.Clear();

                    // Refresh inbox and try to select the conversation with this phone
                    await LoadConversationsAsync();

                    foreach (DataGridViewRow row in dataGridViewConversations.Rows)
                    {
                        if (row.DataBoundItem is Conversation conv &&
                            string.Equals(conv.UserPhone, _apiClient.NormalizeForComparison(phone),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            row.Selected = true;
                            dataGridViewConversations.CurrentCell = row.Cells["UserPhone"];
                            break;
                        }
                    }
                }
                else
                {
                    MessageBox.Show(
                        "Mesaj gönderilemedi.",
                        "Gönderme Hatası",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Mesaj gönderilirken hata oluştu: {ex.Message}",
                    "Gönderme Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                buttonNewChat.Enabled = true;
                buttonSend.Enabled = true;
                textBoxMessageInput.Focus();
            }
        }

        private async void ButtonRefresh_Click(object sender, EventArgs e)
        {
            await RefreshDataAsync();
        }

        private async void ComboBoxEnvironment_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxEnvironment.SelectedIndex < 0) return;

            bool isProd = comboBoxEnvironment.SelectedIndex == 1;
            tApiConfig.SetEnvironment(isProd ? tApiConfig.ENV_PRODUCTION : tApiConfig.ENV_DEVELOPMENT);
            InitializeApiClient(tApiConfig.GetWhatsAppApiBaseUrl());

            await RefreshDataAsync();
        }

        private async void TextBoxSearch_TextChanged(object sender, EventArgs e)
        {
            await Task.Delay(500);
            if (textBoxSearch.Focused)
            {
                await LoadConversationsAsync();
            }
        }

        private async void CheckBoxFilterUnread_CheckedChanged(object sender, EventArgs e)
        {
            await LoadConversationsAsync();
        }

        private async Task UpdateUnreadCountAsync()
        {
            if (_apiClient == null) return;

            try
            {
                var count = await _apiClient.GetUnreadCount();
                whatsAppStatusBar.UpdateUnreadCount(count);
            }
            catch
            {
                // Silently fail
            }
        }

        private async Task UpdateSessionStatusAsync()
        {
            if (_apiClient == null) return;

            try
            {
                var status = await _apiClient.GetSessionStatus();
                
                // Color code status
                var color = status == "CONNECTED" ? Color.Green :
                           status == "NEEDS_QR" ? Color.Orange :
                           status == "CONNECTING" ? Color.Yellow : Color.Red;
                
                whatsAppStatusBar.UpdateStatus(status, color);
            }
            catch
            {
                whatsAppStatusBar.UpdateStatus("Bilinmiyor", Color.Gray);
            }
        }

        private void UpdateStatusBar()
        {
            whatsAppStatusBar.UpdateLastSync(_lastRefresh);
        }
    }
}
