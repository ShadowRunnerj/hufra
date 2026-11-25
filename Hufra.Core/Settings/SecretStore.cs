using System;
using System.Security.Cryptography;
using System.Text;

namespace Hufra.Core.Settings
{
    /// <summary>يشفّر الأسرار (مثل توكن البوت) بحساب ويندوز الحالي (DPAPI) قبل حفظها على القرص.</summary>
    public static class SecretStore
    {
        private const string Prefix = "dpapi:";

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(data);
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix)) return null;
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(Prefix.Length)), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return null; }
        }
    }
}
