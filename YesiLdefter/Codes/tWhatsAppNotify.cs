using System;
using System.Data;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Tkn_Variable;
using Tkn_ToolBox;
using Tkn_Save;
using Tkn_UstadAPI;

namespace YesiLdefter.Codes
{
    /// <summary>
    /// WhatsApp notification helper that reuses the existing bildirimPaketi / CrsBildirim* pipeline
    /// and sends messages via WhatsAppApiClient instead of SMS.
    /// </summary>
    public class tWhatsAppNotify : tBase
    {
        private readonly tToolBox t = new tToolBox();

        /// <summary>
        /// Core bulk send implementation. Iterates notification lines and sends WhatsApp messages.
        /// Mirrors tSMS.bildirimleriSMSKanaliylaGonder_ logic but calls WhatsAppApiClient.
        /// </summary>
        public bool bildirimleriWhatsAppKanaliylaGonder_(Form tForm, bildirimPaketi tBildirim_, DataSet dsLines, DataNavigator dNLines)
        {
            bool onay = true;

            if (dsLines == null || dsLines.Tables.Count == 0 || dsLines.Tables[0].Rows.Count == 0)
            {
                MessageBox.Show("Gönderilecek WhatsApp bildirimi bulunamadı.", "WhatsApp Bildirimleri",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            // WhatsApp API client uses current JWT token and firm GUID
            if (string.IsNullOrEmpty(v.tUser.JwtToken) || string.IsNullOrEmpty(v.tMainFirm.FirmGuid))
            {
                MessageBox.Show("WhatsApp gönderimi için geçerli oturum bulunamadı. Lütfen yeniden giriş yapın.",
                    "WhatsApp Bildirimleri", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            int toplam = dsLines.Tables[0].Rows.Count;
            int basarili = 0;
            int hatali = 0;

            try
            {
                using (var client = new WhatsAppApiClient(GetBaseUrl(), v.tUser.JwtToken, v.tMainFirm.FirmGuid))
                {
                    for (int i = 0; i < toplam; i++)
                    {
                        dNLines.Position = i;

                        DataRow row = dsLines.Tables[0].Rows[dNLines.Position];

                        // StateTypeId < 2  => not sent yet (same semantics as SMS)
                        short stateTypeId = t.myInt16(row["StateTypeId"].ToString());
                        if (stateTypeId >= 2)
                            continue;

                        string telefonNo = string.Empty;
                        string mesajMetni = string.Empty;

                        // Try standard column names first
                        if (dsLines.Tables[0].Columns.Contains("TelefonNo"))
                            telefonNo = row["TelefonNo"].ToString();
                        if (dsLines.Tables[0].Columns.Contains("Mesaj"))
                            mesajMetni = row["Mesaj"].ToString();

                        // Fallback: try bildirimPaketi mapping if available
                        if (string.IsNullOrEmpty(telefonNo) && !string.IsNullOrEmpty(tBildirim_.hedefTelefonNoFName)
                            && dsLines.Tables[0].Columns.Contains(tBildirim_.hedefTelefonNoFName))
                        {
                            telefonNo = row[tBildirim_.hedefTelefonNoFName].ToString();
                        }

                        if (string.IsNullOrEmpty(mesajMetni) && !string.IsNullOrEmpty(tBildirim_.secilenBildirimMetni))
                        {
                            mesajMetni = tBildirim_.secilenBildirimMetni;
                        }

                        if (!t.IsNotNull(telefonNo) || !t.IsNotNull(mesajMetni))
                        {
                            hatali++;
                            continue;
                        }

                        try
                        {
                            // Fire WhatsApp send (synchronously wait inside loop)
                            SendMessageResponse resp = Task.Run(
                                () => client.SendMessage(telefonNo, mesajMetni, false)).Result;

                            if (resp != null && resp.Success)
                            {
                                basarili++;
                                row["StateTypeId"] = 2; // Gönderildi
                                if (dsLines.Tables[0].Columns.Contains("ServisinCevabi"))
                                    row["ServisinCevabi"] = $"WA:{resp.MessageId}:{resp.Status}";
                                if (dsLines.Tables[0].Columns.Contains("IsLock"))
                                    row["IsLock"] = 1;

                                tSave sv = new tSave();
                                sv.tDataSave(tForm, dsLines, dNLines, dNLines.Position);
                            }
                            else
                            {
                                hatali++;
                                if (dsLines.Tables[0].Columns.Contains("ServisinCevabi"))
                                    row["ServisinCevabi"] = "WA:ERROR";
                            }
                        }
                        catch (Exception exSend)
                        {
                            hatali++;
                            if (dsLines.Tables[0].Columns.Contains("ServisinCevabi"))
                                row["ServisinCevabi"] = "WA:EX:" + exSend.Message;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("WhatsApp bildirimleri gönderilirken hata oluştu." + v.ENTER2 + ex.Message,
                    "WhatsApp Bildirimleri", MessageBoxButtons.OK, MessageBoxIcon.Error);
                onay = false;
            }

            MessageBox.Show(
                $"WhatsApp gönderim işlemi tamamlandı.{v.ENTER2}" +
                $"Toplam: {toplam}{v.ENTER}" +
                $"Başarılı: {basarili}{v.ENTER}" +
                $"Hatalı: {hatali}",
                "WhatsApp Bildirimleri",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return onay;
        }

        private string GetBaseUrl()
        {
            return tApiConfig.GetWhatsAppApiBaseUrl();
        }
    }
}

