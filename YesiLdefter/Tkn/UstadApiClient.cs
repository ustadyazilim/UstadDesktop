using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tkn_Variable;
using Newtonsoft.Json;
using System.Security.Cryptography;

namespace Tkn_UstadAPI
{
    /// <summary>
    /// Secure API client for Ustad.API authentication and user operations
    /// No database connection strings are exposed - all operations go through the API
    /// </summary>
    public class UstadApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiBaseUrl;
        private bool _disposed = false;

        // Default aligns with Ustad.API dev settings (Kestrel http://localhost:5000)
        public UstadApiClient(string apiBaseUrl = "http://localhost:5001")
        {
            _apiBaseUrl = apiBaseUrl.TrimEnd('/');
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_apiBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>
        /// Login with email and password, returns JWT token and user info
        /// </summary>
        public async Task<LoginResponse> LoginAsync(string email, string password)
        {
            try
            {
                var request = new LoginRequest
                {
                    UserName = email,
                    Password = password,
                    TurnstileToken = "" // Optional - can be added if needed
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/auth/login", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    var statusCode = (int)response.StatusCode;
                    var httpStatusMessage = response.StatusCode.ToString();
                    var ex = new Exception($"Login failed (HTTP {statusCode} {httpStatusMessage}): {errorContent}");
                    ex.Data["StatusCode"] = statusCode;
                    ex.Data["StatusMessage"] = httpStatusMessage;
                    ex.Data["ErrorContent"] = errorContent;
                    throw ex;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var loginResponse = JsonConvert.DeserializeObject<LoginResponse>(responseContent);
                
                // Store token for subsequent requests
                if (!string.IsNullOrEmpty(loginResponse?.Token))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResponse.Token);
                }

                return loginResponse;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                // Network-level HTTP exception (connection issues)
                throw new Exception($"API connection error to {_apiBaseUrl}: {ex.Message}", ex);
            }
            catch (TaskCanceledException)
            {
                throw new Exception("API request timeout. Please check if the API is running.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Login error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Check if user email exists in the system
        /// </summary>
        public async Task<UserExistsResponse> CheckUserExistsAsync(string email)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email))
                {
                    throw new Exception("Email is required.");
                }

                var response = await _httpClient.GetAsync($"/auth/user/exists?email={Uri.EscapeDataString(email)}");

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return new UserExistsResponse { Exists = false };
                    }
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Check user failed: {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<UserExistsResponse>(responseContent);
            }
            catch (Exception ex)
            {
                throw new Exception($"Check user error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Change user password (requires old password verification)
        /// </summary>
        public async Task<bool> ChangePasswordAsync(string email, string oldPassword, string newPassword)
        {
            try
            {
                var request = new ChangePasswordRequest
                {
                    Email = email,
                    OldPassword = oldPassword,
                    NewPassword = newPassword
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/auth/changepassword", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Password change failed: {errorContent}");
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Change password error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Request password reset (sends email with reset token)
        /// </summary>
        public async Task<bool> RequestPasswordResetAsync(string email)
        {
            try
            {
                var request = new ResetPasswordRequestRequest { UserName = email };
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/auth/resetPasswordRequest", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Password reset request failed: {errorContent}");
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Password reset request error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Get user firms list using UserGUID
        /// </summary>
        public async Task<List<FirmInfo>> GetUserFirmsAsync(string userGUID)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/UstadFirm/user/{Uri.EscapeDataString(userGUID)}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Get user firms failed: {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var firms = JsonConvert.DeserializeObject<List<FirmInfo>>(responseContent);
                
                return firms ?? new List<FirmInfo>();
            }
            catch (Exception ex)
            {
                throw new Exception($"Get user firms error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Get firm details by FirmGUID
        /// </summary>
        public async Task<FirmDetails> GetFirmDetailsAsync(string firmGUID)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/UstadFirm/{Uri.EscapeDataString(firmGUID)}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Get firm details failed: {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<FirmDetails>(responseContent);
            }
            catch (Exception ex)
            {
                throw new Exception($"Get firm details error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Select firm - validates user access to firm and issues new token with firm claim
        /// </summary>
        public async Task<LoginResponse> SelectFirmAsync(string firmGUID)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(firmGUID))
                {
                    throw new Exception("FirmGUID is required.");
                }

                // Ensure we have a valid auth token before making the request
                if (_httpClient.DefaultRequestHeaders.Authorization == null)
                {
                    System.Diagnostics.Debug.WriteLine("[UstadApiClient] Warning: No authorization token set before SelectFirmAsync call");
                }

                var request = new SelectFirmRequest
                {
                    FirmGUID = firmGUID.Trim()
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                System.Diagnostics.Debug.WriteLine($"[UstadApiClient] Calling /auth/select-firm with FirmGUID: {firmGUID}");
                var response = await _httpClient.PostAsync("/auth/select-firm", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    var statusCode = (int)response.StatusCode;
                    var httpStatusMessage = response.StatusCode.ToString();
                    
                    System.Diagnostics.Debug.WriteLine($"[UstadApiClient] SelectFirmAsync failed: HTTP {statusCode} {httpStatusMessage}");
                    System.Diagnostics.Debug.WriteLine($"[UstadApiClient] Error content: {errorContent}");
                    
                    // If 401, check if token is missing or invalid
                    if (statusCode == 401)
                    {
                        var authHeader = _httpClient.DefaultRequestHeaders.Authorization;
                        if (authHeader == null)
                        {
                            throw new Exception("Authentication token is missing. Please login again.");
                        }
                        else
                        {
                            throw new Exception($"Authentication failed. Token may be expired or invalid. Please login again. Details: {errorContent}");
                        }
                    }
                    
                    var ex = new Exception($"Select firm failed (HTTP {statusCode} {httpStatusMessage}): {errorContent}");
                    ex.Data["StatusCode"] = statusCode;
                    ex.Data["StatusMessage"] = httpStatusMessage;
                    ex.Data["ErrorContent"] = errorContent;
                    throw ex;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var selectFirmResponse = JsonConvert.DeserializeObject<LoginResponse>(responseContent);
                
                // Update token for subsequent requests
                if (!string.IsNullOrEmpty(selectFirmResponse?.Token))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", selectFirmResponse.Token);
                    System.Diagnostics.Debug.WriteLine("[UstadApiClient] Token updated after firm selection");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[UstadApiClient] Warning: SelectFirmAsync returned empty token");
                }

                return selectFirmResponse;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UstadApiClient] HttpRequestException in SelectFirmAsync: {ex.Message}");
                throw new Exception($"API connection error to {_apiBaseUrl}: {ex.Message}", ex);
            }
            catch (TaskCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[UstadApiClient] TaskCanceledException in SelectFirmAsync (timeout)");
                throw new Exception("API request timeout. Please check if the API is running.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UstadApiClient] Exception in SelectFirmAsync: {ex.Message}");
                throw new Exception($"Select firm error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Set authorization token for authenticated requests
        /// </summary>
        public void SetAuthToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                _httpClient.DefaultRequestHeaders.Authorization = null;
            else
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        /// <summary>
        /// Get database connection information from API (requires authentication)
        /// Returns encrypted connection strings that need to be decrypted using the JWT key
        /// </summary>
        public async Task<DatabaseConnectionInfo> GetDatabaseConnectionInfoAsync(string jwtKey)
        {
            try
            {
                var response = await _httpClient.GetAsync("/auth/db-connection-info");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Get database connection info failed: {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var dbInfo = JsonConvert.DeserializeObject<DatabaseConnectionInfo>(responseContent);
                
                if (dbInfo != null && !string.IsNullOrEmpty(jwtKey))
                {
                    // Decrypt connection strings
                    if (!string.IsNullOrEmpty(dbInfo.EncryptedUstadCrmConnectionString))
                    {
                        dbInfo.UstadCrmConnectionString = DecryptConnectionString(dbInfo.EncryptedUstadCrmConnectionString, jwtKey);
                    }
                    if (!string.IsNullOrEmpty(dbInfo.EncryptedManagerConnectionString))
                    {
                        dbInfo.ManagerConnectionString = DecryptConnectionString(dbInfo.EncryptedManagerConnectionString, jwtKey);
                    }
                }

                return dbInfo;
            }
            catch (Exception ex)
            {
                throw new Exception($"Get database connection info error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Decrypt connection string using AES decryption with key derived from JWT secret
        /// </summary>
        private string DecryptConnectionString(string encryptedText, string key)
        {
            try
            {
                // Derive a 32-byte key from the JWT key (same as encryption)
                byte[] keyBytes = Encoding.UTF8.GetBytes(key);
                if (keyBytes.Length < 32)
                {
                    Array.Resize(ref keyBytes, 32);
                }
                else if (keyBytes.Length > 32)
                {
                    byte[] truncated = new byte[32];
                    Array.Copy(keyBytes, truncated, 32);
                    keyBytes = truncated;
                }

                byte[] encryptedBytes = Convert.FromBase64String(encryptedText);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = keyBytes;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    // Extract IV (first 16 bytes)
                    byte[] iv = new byte[16];
                    Array.Copy(encryptedBytes, 0, iv, 0, 16);
                    aes.IV = iv;

                    // Extract encrypted data (remaining bytes)
                    byte[] cipherBytes = new byte[encryptedBytes.Length - 16];
                    Array.Copy(encryptedBytes, 16, cipherBytes, 0, cipherBytes.Length);

                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    {
                        byte[] decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                        return Encoding.UTF8.GetString(decryptedBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error decrypting connection string: {ex.Message}", ex);
            }
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

        #region Data Models

        public class LoginRequest
        {
            public string UserName { get; set; }
            public string Password { get; set; }
            public string TurnstileToken { get; set; }
        }

        public class LoginResponse
        {
            [JsonProperty("token")]
            public string Token { get; set; }

            [JsonProperty("userId")]
            public int UserId { get; set; }

            [JsonProperty("operatorId")]
            public int OperatorId { get; set; }

            [JsonProperty("userGUID")]
            public string UserGUID { get; set; }

            [JsonProperty("fullName")]
            public string FullName { get; set; }

            [JsonProperty("role")]
            public string Role { get; set; }

            [JsonProperty("firmId")]
            public int FirmId { get; set; }

            [JsonProperty("dbTypeId")]
            public short DbTypeId { get; set; }
        }

        public class UserExistsResponse
        {
            public bool Exists { get; set; }
            public int? UserId { get; set; }
            public string UserFullName { get; set; }
            public bool IsActive { get; set; }
        }

        public class ChangePasswordRequest
        {
            public string Email { get; set; }
            public string OldPassword { get; set; }
            public string NewPassword { get; set; }
        }

        public class ResetPasswordRequestRequest
        {
            public string UserName { get; set; }
        }

        public class SelectFirmRequest
        {
            public string FirmGUID { get; set; } = string.Empty;
        }

        public class FirmInfo
        {
            public int Id { get; set; }
            public int FirmId { get; set; }
            public string FirmGUID { get; set; }
            public string FirmLongName { get; set; }
            public int? UserId { get; set; }
            public string UserGUID { get; set; }
            public string UserFullName { get; set; }
            public bool IsActive { get; set; }
            public string FirmShortName { get; set; }
            public string MenuCode { get; set; }
            public short? SectorTypeId { get; set; }
            public string DatabaseName { get; set; }
            public string ServerNameIP { get; set; }
            public string DbLoginName { get; set; }
            public string DbPass { get; set; }
            public short? DbTypeId { get; set; }
            public int? DistrictTypeId { get; set; }
            public int? CityTypeId { get; set; }
            public string MebbisCode { get; set; }
            public string MebbisPass { get; set; }

        }

        public class FirmDetails
        {
            public Firm Firm { get; set; }
        }

        public class Firm
        {
            public int FirmId { get; set; }
            public string FirmGUID { get; set; }
            public string FirmLongName { get; set; }
            public int UserCount { get; set; }
            public UserInfo[] Users { get; set; }
        }

        public class UserInfo
        {
            public int Id { get; set; }
            public int UserId { get; set; }
            public string UserGUID { get; set; }
            public string UserFullName { get; set; }
            public string UserEMail { get; set; }
            public bool IsActive { get; set; }
        }

        public class DatabaseConnectionInfo
        {
            public string UstadCrmServer { get; set; }
            public string UstadCrmPort { get; set; }
            public string UstadCrmDatabase { get; set; }
            public string UstadCrmUsername { get; set; }
            public string ManagerServer { get; set; }
            public string ManagerDatabase { get; set; }
            public string ManagerUsername { get; set; }
            public string EncryptedUstadCrmConnectionString { get; set; }
            public string EncryptedManagerConnectionString { get; set; }
            // Decrypted connection strings (set after decryption)
            public string UstadCrmConnectionString { get; set; }
            public string ManagerConnectionString { get; set; }
        }

        #endregion
    }
}

