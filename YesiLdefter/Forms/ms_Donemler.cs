using DevExpress.XtraCharts;
using DevExpress.XtraEditors;
using DevExpress.XtraReports.Native.Templates;
using Microsoft.JScript;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Linq.Mapping;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tkn_ToolBox;
using Tkn_Variable;
using Tkn_UstadAPI;

namespace YesiLdefter
{
    public partial class ms_Donemler : Form
    {
        tToolBox t = new tToolBox();

        DataSet ds_KursiyerList = null;
        DataNavigator dN_KursiyerList = null;

        string TableIPCode = string.Empty;
        string menuName = "MENU_" + "UST/MEB/SRC/Donemler";
        string buttonESrc = "ButtonESrcEntegrasyonu";
        string buttonWhatsApp = "ButtonWhatsApp";

        public ms_Donemler()
        {
            InitializeComponent();
        }

        private void ms_Donemler_Shown(object sender, EventArgs e)
        {
            // Create WhatsApp button programmatically if it doesn't exist
            CreateWhatsAppButtonIfNeeded();
            
            // Try to attach click handlers
            t.Find_Button_AddClick(this, menuName, buttonESrc, myNavElementClick);
            t.Find_Button_AddClick(this, menuName, buttonWhatsApp, myNavElementClick);

            if (ds_KursiyerList == null)
            {
                if (v.SP_Firm_SectorTypeId == 201) // Mtsk
                    TableIPCode = "UST/MEB/MtskAdayTalep.DonemlerTumListesi";

                if (v.SP_Firm_SectorTypeId == 203) // Src
                    TableIPCode = "UST/MEB/SrcAdayTalep.DonemlerDonemListesi";

                if (v.SP_Firm_SectorTypeId == 204) // Src5
                    TableIPCode = "UST/MEB/SrcAdayTalep.DonemlerDonemListesiSrc5";

                t.Find_DataSet(this, ref ds_KursiyerList, ref dN_KursiyerList, TableIPCode);
            }
        }

        private void CreateWhatsAppButtonIfNeeded()
        {
            try
            {
                string[] controls = new string[] { };
                Control menuControl = t.Find_Control(this, menuName, "", controls);
                
                if (menuControl != null && menuControl.GetType().ToString() == "DevExpress.XtraBars.Navigation.TileNavPane")
                {
                    var tileNavPane = menuControl as DevExpress.XtraBars.Navigation.TileNavPane;
                    
                    // Check if button already exists
                    bool buttonExists = false;
                    for (int i = 0; i < tileNavPane.Buttons.Count; i++)
                    {
                        if (tileNavPane.Buttons[i].Element.Name == buttonWhatsApp)
                        {
                            buttonExists = true;
                            break;
                        }
                    }
                    
                    // Create button if it doesn't exist
                    if (!buttonExists)
                    {
                        var whatsAppButton = new DevExpress.XtraBars.Navigation.NavButton
                        {
                            Name = buttonWhatsApp,
                            Caption = "WhatsApp",
                            // Try to find a glyph, or use null if not available
                            Glyph = t.Find_Glyph("WHATSAPP16") ?? t.Find_Glyph("MESSAGE16") ?? null
                        };
                        
                        // Add click handler
                        whatsAppButton.ElementClick += myNavElementClick;
                        
                        // Add to the pane
                        tileNavPane.Buttons.Add(whatsAppButton);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating WhatsApp button: {ex.Message}");
            }
        }

        private void myNavElementClick(object sender, DevExpress.XtraBars.Navigation.NavElementEventArgs e)
        {
            if (sender.GetType().ToString() == "DevExpress.XtraBars.Navigation.NavButton")
            {
                if (((DevExpress.XtraBars.Navigation.NavButton)sender).Name == buttonESrc) eSrcEntegrasyonu();
                if (((DevExpress.XtraBars.Navigation.NavButton)sender).Name == buttonWhatsApp) OpenWhatsAppForm();
            }

            if (sender.GetType().ToString() == "DevExpress.XtraBars.Navigation.TileNavItem")
            {
                //if (((DevExpress.XtraBars.Navigation.TileNavItem)sender).Name == buttonInsertPaketOlustur) InsertPaketOlustur();
                //if (((DevExpress.XtraBars.Navigation.TileNavItem)sender).Name == buttonPaketiGonder) PaketiGonder();
            }
        }

        private void OpenWhatsAppForm()
        {
            try
            {
                // Validate JWT and Firm context (similar to checkedInputApi pattern)
                if (string.IsNullOrEmpty(v.tUser.JwtToken))
                {
                    MessageBox.Show(
                        "JWT token bulunamadı. Lütfen tekrar giriş yapın.",
                        "Kimlik Doğrulama Hatası",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                if (string.IsNullOrEmpty(v.tMainFirm.FirmGuid))
                {
                    MessageBox.Show(
                        "Firma bilgisi bulunamadı. Lütfen tekrar giriş yapın.",
                        "Firma Bilgisi Hatası",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                // Open WhatsApp form using the form opening pattern
                string FormName = "ms_WhatsApp";
                string FormCode = "UST/PMS/PMS/WhatsApp";
                t.OpenFormPreparing(FormName, FormCode, v.formType.Child);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"WhatsApp formu açılırken hata oluştu: {ex.Message}",
                    "Hata",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        
        private void eSrcEntegrasyonu()
        {
            if (v.SP_Firm_SectorTypeId == 201)
            {
                MessageBox.Show("Mtsk Entegrasyon butonuna tıklandı.");
                //sendESrcApi();
            }

            if (v.SP_Firm_SectorTypeId == 203)
            {
                //MessageBox.Show("Src Entegrasyon butonuna tıklandı.");
                sendESrcApi();
            }

            if (v.SP_Firm_SectorTypeId == 204)
            {
                MessageBox.Show("Src5 Entegrasyon butonuna tıklandı.");
                //sendESrcApi();
            }

        }

        private bool KullaniciOnayi()
        {
            if (ds_KursiyerList == null) return false;
            if (t.IsNotNull(ds_KursiyerList) == false) return false;

            string Donem = ds_KursiyerList.Tables[0].Rows[0]["Lkp_DonemTipi"].ToString();
            bool onay = false;
            string soru = Donem + " dönemi kursiyerleri Soru Bankasına gönderilecek, Onaylıyor musunuz ?";
            DialogResult cevap = t.mySoru(soru);
            if (DialogResult.Yes == cevap)
            {
                onay = true;
            }
            return onay;
        }


        private void sendESrcApi()
        {
            
            if (KullaniciOnayi() == false) return;

            bool onaylimi = Onaylimi(ds_KursiyerList);
            bool onay = false;

            // Mtsk Aday Talep Donemler Tablosu
            // Src Aday Talep Donemler Tablosu      

            // 1. Listeyi döngüden önce tanımlayın
            var studentList = new List<StudentDataModel>();

            foreach (DataRow row in ds_KursiyerList.Tables[0].Rows) // Mevcut döngünüz
            {
                onay = row["LKP_ONAY"].ToString().ToUpper() == "TRUE" ? true : false;

                if ((onaylimi == false) || (onaylimi == true && onay))
                {
                    // 2. Her öğrenci için yeni bir nesne örneği oluşturun
                    var item = new StudentDataModel();

                    item.KURSMAIL = t.Set(v.tMainFirm.eSrcEnt_KursKodu, v.tMainFirm.FirmCode, v.tMainFirm.DatabaseName);
                    item.PASS = "test"; //v.tMainFirm.eSrcEnt_Pass;
                    item.TC = row["Lkp_TcNo"].ToString();
                    item.ADI = row["Lkp_Adi"].ToString();
                    item.SOYADI = row["Lkp_Soyadi"].ToString();
                    item.EMAIL = row["Lkp_Eposta"].ToString();
                    item.IL = "";
                    item.ILCE = "";
                    item.ADRES = row["Lkp_Adres"].ToString();
                    item.GSM = row["Lkp_CepTelefonu"].ToString();
                    item.IMG = "no-image-given";
                    item.BELGE = row["Lkp_IstenenSertifikaTipi"].ToString();
                    item.CINSIYET = row["Lkp_CinsiyetTipi"].ToString();
                    item.BAKIYE = 0;
                    item.SUBESI = row["Lkp_DonemTipi"].ToString();
                    item.GRUP = row["Lkp_GrupTipi"].ToString();
                    item.DONEM = row["Lkp_SubeTipi"].ToString();

                    // 3. Hazırlanan nesneyi listeye ekleyin
                    studentList.Add(item);
                }
            }

            if (studentList.Count == 0)
            {
                MessageBox.Show("Senkronize edilecek öğrenci bulunamadı.", "e-src", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // POST to Ustad.API sync-batch (e-src logic and credentials live in Ustad.API)
            try
            {
                string baseUrl = tApiConfig.GetApiBaseUrl()?.TrimEnd('/') ?? "http://localhost:5001";
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(baseUrl);
                    client.Timeout = TimeSpan.FromSeconds(90);
                    client.DefaultRequestHeaders.Add("Accept", "application/json");

                    var requestBody = new { Students = studentList };
                    var json = JsonConvert.SerializeObject(requestBody);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = client.PostAsync("/api/esrc-external-data/sync-batch", content).GetAwaiter().GetResult();

                    string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        MessageBox.Show($"e-src senkronizasyon hatası: HTTP {(int)response.StatusCode}\n{responseBody}", "e-src", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var batchResponse = JsonConvert.DeserializeObject<ESrcBatchSyncResponseDto>(responseBody);
                    if (batchResponse?.Results == null || batchResponse.Results.Count == 0)
                    {
                        MessageBox.Show("Sunucudan sonuç alınamadı.", "e-src", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    string html = BuildESrcResultHtml(batchResponse.Results);
                    ShowESrcResultDialog(html);
                }
            }
            catch (HttpRequestException ex)
            {
                MessageBox.Show($"Bağlantı hatası: {ex.Message}", "e-src", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"e-src senkronizasyon hatası: {ex.Message}", "e-src", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string BuildESrcResultHtml(List<ESrcStudentSyncResultDto> results)
        {
            // UstadDesignTokens-style colors: success #16a34a, warning #fbbc04, error #ea4335
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><style>");
            sb.Append("body{font-family:'Segoe UI',sans-serif;padding:16px;background:#fcfcff;}");
            sb.Append(".box{border-radius:8px;padding:12px;margin:8px 0;}");
            sb.Append(".success{background:rgba(22,163,74,0.08);border-left:4px solid #16a34a;}");
            sb.Append(".warning{background:rgba(251,188,4,0.08);border-left:4px solid #fbbc04;}");
            sb.Append(".error{background:rgba(234,67,53,0.08);border-left:4px solid #ea4335;}");
            sb.Append(".name{font-weight:600;color:#111827;} .msg{color:#374151;margin-top:4px;}");
            sb.Append(".msg.success{background:rgba(22,163,74,0.08);color:#0d5c2e;padding:4px 8px;border-radius:4px;margin-top:4px;}");
            sb.Append(".msg.warning{background:rgba(251,188,4,0.08);color:#b8860b;padding:4px 8px;border-radius:4px;margin-top:4px;}");
            sb.Append(".msg.error{background:rgba(234,67,53,0.08);color:#c5221f;padding:4px 8px;border-radius:4px;margin-top:4px;}");
            sb.Append("h3{margin:0 0 12px 0;color:#295c00;}");
            sb.Append("</style></head><body><h3>e-src Senkronizasyon Sonuçları</h3>");

            foreach (var r in results)
            {
                string css = r.Success ? "success" : "error";
                if (!r.Success && (r.Message?.Contains("uyarı") == true || r.Message?.Contains("Warning") == true))
                    css = "warning";
                sb.Append($"<div class='box {css}'><div class='name'>{System.Net.WebUtility.HtmlEncode(r.StudentName ?? r.TcNo ?? "")}</div>");
                sb.Append($"<div class='msg'>{System.Net.WebUtility.HtmlEncode(r.Message ?? "")}</div>");
                if (r.ESrcMessages != null)
                {
                    foreach (var m in r.ESrcMessages)
                    {
                        string text = null;
                        string lineClass = "msg";
                        if (!string.IsNullOrEmpty(m.MessageSuccess)) { text = m.MessageSuccess; lineClass = "msg success"; }
                        else if (!string.IsNullOrEmpty(m.MessageWarning)) { text = m.MessageWarning; lineClass = "msg warning"; }
                        else if (!string.IsNullOrEmpty(m.MessageError)) { text = m.MessageError; lineClass = "msg error"; }
                        else if (!string.IsNullOrEmpty(m.MessagesDanger)) { text = m.MessagesDanger; lineClass = "msg error"; }
                        if (!string.IsNullOrEmpty(text))
                            sb.Append($"<div class='{lineClass}'>{System.Net.WebUtility.HtmlEncode(text)}</div>");
                    }
                }
                sb.Append("</div>"); // close box
            }
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static void ShowESrcResultDialog(string html)
        {
            var form = new Form
            {
                Text = "e-src Senkronizasyon Sonuçları",
                Size = new Size(520, 420),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable
            };
            var webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            form.Controls.Add(webView);
            form.Load += async (s, ev) =>
            {
                try
                {
                    await webView.EnsureCoreWebView2Async(null);
                    webView.CoreWebView2.NavigateToString(html);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Sonuç penceresi açılamadı: {ex.Message}", "e-src", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            form.ShowDialog();
        }

        // DTOs for Ustad.API sync-batch response (PascalCase to match .NET JSON)
        private class ESrcBatchSyncResponseDto
        {
            [JsonProperty("Results")]
            public List<ESrcStudentSyncResultDto> Results { get; set; }
        }

        private class ESrcStudentSyncResultDto
        {
            [JsonProperty("TcNo")] public string TcNo { get; set; }
            [JsonProperty("StudentName")] public string StudentName { get; set; }
            [JsonProperty("Success")] public bool Success { get; set; }
            [JsonProperty("Message")] public string Message { get; set; }
            [JsonProperty("FromCache")] public bool FromCache { get; set; }
            [JsonProperty("ESrcMessages")] public List<MsgBoxDto> ESrcMessages { get; set; }
        }

        private class MsgBoxDto
        {
            [JsonProperty("MessageSuccess")] public string MessageSuccess { get; set; }
            [JsonProperty("MessageWarning")] public string MessageWarning { get; set; }
            [JsonProperty("MessageError")] public string MessageError { get; set; }
            [JsonProperty("MessagesDanger")] public string MessagesDanger { get; set; }
        }
        
        private bool Onaylimi(DataSet ds)
        { 
            bool onay = false;
            string value = "";
            
            foreach (DataRow dr in ds.Tables[0].Rows)
            {
                value = "";
                try
                {
                    value = dr["LKP_ONAY"].ToString().ToUpper();
                    if (value == "TRUE") 
                    { 
                        onay = true; 
                        break;
                    }
                }
                catch (Exception)
                {
                    break;
                    //throw;
                }
            }

            return onay; 
        }


    }
}
