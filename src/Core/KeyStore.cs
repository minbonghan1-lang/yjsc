using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Core
{
    public class KeyStore
    {
        public static void InitializeVaultData(string configPath, string adminPassword)
        {
            byte[] mdk = CryptoManager.GenerateRandomBytes(32);
            byte[] adminSalt = CryptoManager.GenerateRandomBytes(16);
            byte[] adminKek = CryptoManager.DeriveKeyFromPassword(adminPassword, adminSalt);
            byte[] adminWrappedMdk = CryptoManager.WrapKey(mdk, adminKek);

            string line = $"Admin|{Convert.ToBase64String(adminSalt)}|{Convert.ToBase64String(adminWrappedMdk)}";
            File.WriteAllLines(configPath, new string[] { line });
        }

        public static (byte[] mdk, string role) TryGetMasterDataKey(string configPath, string password)
        {
            if (!File.Exists(configPath)) return (null, null);

            string[] lines = File.ReadAllLines(configPath);
            foreach (string line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length == 3)
                {
                    string role = parts[0];
                    byte[] salt = Convert.FromBase64String(parts[1]);
                    byte[] wrappedMdk = Convert.FromBase64String(parts[2]);

                    try
                    {
                        byte[] kek = CryptoManager.DeriveKeyFromPassword(password, salt);
                        byte[] mdk = CryptoManager.UnwrapKey(wrappedMdk, kek);
                        if (mdk != null && mdk.Length == 32) return (mdk, role);
                    }
                    catch { }
                }
            }
            return (null, null);
        }

        public static void ChangeUserPassword(string configPath, byte[] mdk, string roleToChange, string newPassword)
        {
            if (!File.Exists(configPath)) throw new FileNotFoundException("vault.cfg not found");

            var lines = File.ReadAllLines(configPath).ToList();
            byte[] newSalt = CryptoManager.GenerateRandomBytes(16);
            byte[] newKek = CryptoManager.DeriveKeyFromPassword(newPassword, newSalt);
            byte[] newWrappedMdk = CryptoManager.WrapKey(mdk, newKek);
            string newLine = $"{roleToChange}|{Convert.ToBase64String(newSalt)}|{Convert.ToBase64String(newWrappedMdk)}";

            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith(roleToChange + "|"))
                {
                    lines[i] = newLine;
                    found = true;
                    break;
                }
            }
            if (!found) lines.Add(newLine);
            File.WriteAllLines(configPath, lines);
        }

        public static void DeleteUser(string configPath, string roleToDelete)
        {
            if (!File.Exists(configPath)) return;
            var lines = File.ReadAllLines(configPath).Where(l => !l.StartsWith(roleToDelete + "|")).ToArray();
            File.WriteAllLines(configPath, lines);
        }

        public static void SaveAutoLogin(string password)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string alPath = Path.Combine(appDir, "VaultData", "autologin.cfg");
            try { File.WriteAllText(alPath, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password))); } catch { }
        }

        public static string GetAutoLogin()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string alPath = Path.Combine(appDir, "VaultData", "autologin.cfg");
            try { if (File.Exists(alPath)) return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(File.ReadAllText(alPath))); } catch { }
            return null;
        }

        public static void ClearAutoLogin()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string alPath = Path.Combine(appDir, "VaultData", "autologin.cfg");
            if (File.Exists(alPath)) File.Delete(alPath);
        }
    }
}
