using System;
using Tkn_Registry;
using Tkn_Variable;

namespace Tkn_UstadAPI
{
    /// <summary>
    /// API Configuration Helper
    /// NOTE(@Janberk): Centralized configuration management for API settings.
    /// Stores Environment (Development/Production), API base URL and JWT key in Windows Registry.
    /// Development: Ustad API localhost:5001, WhatsApp localhost:8080.
    /// Production: Ustad API 143.198.228.153:8080, WhatsApp 143.198.228.153:8080/api.
    /// </summary>
    public static class tApiConfig
    {
        private const string REGISTRY_KEY_ENVIRONMENT = "Environment";
        private const string REGISTRY_KEY_API_BASE_URL = "ApiBaseUrl";
        private const string REGISTRY_KEY_JWT_KEY = "JwtKey";

        public const string ENV_DEVELOPMENT = "Development";
        public const string ENV_PRODUCTION = "Production";

        // Development: Ustad API (auth, firms, etc.)
        private const string DEV_API_BASE_URL = "http://localhost:5001";
        // Production: Ustad API (same host as WhatsApp)
        private const string PROD_API_BASE_URL = "http://143.198.228.153:8080";
        // Development: WhatsApp integration API
        private const string DEV_WHATSAPP_BASE_URL = "http://localhost:8080";
        // Production: WhatsApp integration API
        private const string PROD_WHATSAPP_BASE_URL = "http://143.198.228.153:8080/api";

        // Default values (fallback if not in registry or environment)
        // Env vars: USTAD_API_BASE_URL, USTAD_JWT_KEY, USTAD_ENVIRONMENT (Development|Production)
        private static readonly string DEFAULT_API_BASE_URL =
            Environment.GetEnvironmentVariable("USTAD_API_BASE_URL") ?? "http://localhost:5001/";
        private static readonly string DEFAULT_JWT_KEY =
            Environment.GetEnvironmentVariable("USTAD_JWT_KEY") ?? string.Empty;

        /// <summary>
        /// Get current environment: Development or Production
        /// </summary>
        public static string GetEnvironment()
        {
            try
            {
                var reg = new tRegistry();
                var value = reg.getRegistryValue(REGISTRY_KEY_ENVIRONMENT);
                if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                {
                    var env = value.ToString().Trim();
                    if (env.Equals(ENV_PRODUCTION, StringComparison.OrdinalIgnoreCase))
                        return ENV_PRODUCTION;
                    return ENV_DEVELOPMENT;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading Environment from registry: {ex.Message}");
            }
            var envVar = Environment.GetEnvironmentVariable("USTAD_ENVIRONMENT");
            if (!string.IsNullOrWhiteSpace(envVar) && envVar.Equals(ENV_PRODUCTION, StringComparison.OrdinalIgnoreCase))
                return ENV_PRODUCTION;
            return ENV_DEVELOPMENT;
        }

        /// <summary>
        /// Set environment (Development or Production) in registry
        /// </summary>
        public static void SetEnvironment(string environment)
        {
            if (string.IsNullOrWhiteSpace(environment))
                throw new ArgumentException("Environment cannot be empty", nameof(environment));
            var normalized = environment.Trim().Equals(ENV_PRODUCTION, StringComparison.OrdinalIgnoreCase)
                ? ENV_PRODUCTION : ENV_DEVELOPMENT;
            try
            {
                var reg = new tRegistry();
                reg.SetUstadRegistry(REGISTRY_KEY_ENVIRONMENT, normalized);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error writing Environment to registry: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get Ustad API base URL (auth, firms, core). Uses Environment when ApiBaseUrl is not overridden in registry.
        /// </summary>
        public static string GetApiBaseUrl()
        {
            try
            {
                var reg = new tRegistry();
                var value = reg.getRegistryValue(REGISTRY_KEY_API_BASE_URL);
                if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                {
                    return value.ToString().Trim().TrimEnd('/');
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading API base URL from registry: {ex.Message}");
            }
            bool isProd = GetEnvironment().Equals(ENV_PRODUCTION, StringComparison.OrdinalIgnoreCase);
            return isProd ? PROD_API_BASE_URL : (DEFAULT_API_BASE_URL?.Trim().TrimEnd('/') ?? DEV_API_BASE_URL);
        }

        /// <summary>
        /// Get WhatsApp integration API base URL based on current Environment
        /// </summary>
        public static string GetWhatsAppApiBaseUrl()
        {
            bool isProd = GetEnvironment().Equals(ENV_PRODUCTION, StringComparison.OrdinalIgnoreCase);
            return isProd ? PROD_WHATSAPP_BASE_URL : DEV_WHATSAPP_BASE_URL;
        }

        /// <summary>
        /// Set API base URL in registry (optional override; when set, GetApiBaseUrl returns this instead of environment-derived URL)
        /// </summary>
        public static void SetApiBaseUrl(string apiBaseUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(apiBaseUrl))
                {
                    throw new ArgumentException("API base URL cannot be empty", nameof(apiBaseUrl));
                }
                var reg = new tRegistry();
                reg.SetUstadRegistry(REGISTRY_KEY_API_BASE_URL, apiBaseUrl.Trim());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error writing API base URL to registry: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// Get JWT key from registry or return default
        /// NOTE(@Janberk): JWT key is used for encrypting/decrypting connection strings.
        /// This is NOT a password but an encryption key - it must match the API's JWT key.
        /// </summary>
        public static string GetJwtKey()
        {
            try
            {
                var reg = new tRegistry();
                var value = reg.getRegistryValue(REGISTRY_KEY_JWT_KEY);
                if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                {
                    return value.ToString().Trim();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading JWT key from registry: {ex.Message}");
            }
            
            return DEFAULT_JWT_KEY;
        }
        /// <summary>
        /// Set JWT key in registry
        /// NOTE(@Janberk): JWT key must match the API's JWT key for encryption/decryption to work.
        /// Registry path: HKEY_CURRENT_USER\Software\Üstad\YesiLdefter\JwtKey
        /// </summary>
        public static void SetJwtKey(string jwtKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(jwtKey))
                {
                    throw new ArgumentException("JWT key cannot be empty", nameof(jwtKey));
                }
                if (jwtKey.Length < 32)
                {
                    throw new ArgumentException("JWT key must be at least 32 characters long", nameof(jwtKey));
                }
                var reg = new tRegistry();
                reg.SetUstadRegistry(REGISTRY_KEY_JWT_KEY, jwtKey.Trim());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error writing JWT key to registry: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Initialize default API configuration if not already set
        /// Sets Environment to Development, optional ApiBaseUrl override, and JWT key.
        /// </summary>
        public static void InitializeDefaults()
        {
            try
            {
                var reg = new tRegistry();
                var env = reg.getRegistryValue(REGISTRY_KEY_ENVIRONMENT);
                if (env == null || string.IsNullOrWhiteSpace(env.ToString()))
                {
                    SetEnvironment(ENV_DEVELOPMENT);
                }
                var apiUrl = reg.getRegistryValue(REGISTRY_KEY_API_BASE_URL);
                if (apiUrl == null || string.IsNullOrWhiteSpace(apiUrl.ToString()))
                {
                    SetApiBaseUrl(DEV_API_BASE_URL);
                }
                var jwtKey = reg.getRegistryValue(REGISTRY_KEY_JWT_KEY);
                if (jwtKey == null || string.IsNullOrWhiteSpace(jwtKey.ToString()))
                {
                    SetJwtKey(DEFAULT_JWT_KEY);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing API configuration defaults: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset to Development: set Environment to Development and clear API base URL override
        /// </summary>
        public static void ResetApiBaseUrlToDefault()
        {
            SetEnvironment(ENV_DEVELOPMENT);
            try
            {
                var reg = new tRegistry();
                reg.SetUstadRegistry(REGISTRY_KEY_API_BASE_URL, DEV_API_BASE_URL);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting API base URL: {ex.Message}");
            }
        }
    }
}

