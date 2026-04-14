using System;
using System.Collections.Generic;
using System.IO;

namespace Core
{
    public class Policy
    {
        public bool DisableCopy { get; set; } = false;
        public bool DisablePrint { get; set; } = false;
        public bool DisableClipboard { get; set; } = true;
        public bool DisableScreenCapture { get; set; } = true;
        public bool DisableEmail { get; set; } = false;

        public bool DisableModify { get; set; } = false;
        public bool DisableDelete { get; set; } = false;
        public bool DisableRename { get; set; } = false;
        public bool DisableExport { get; set; } = false;

        public bool DisableExecution { get; set; } = false;
        public bool DisableNewFolder { get; set; } = false;
        public bool DisableSaveAs { get; set; } = false;

        public int UsageCountLimit { get; set; } = 0;
        public DateTime? ExpirationDate { get; set; } = null;
        public string AllowedIPs { get; set; } = "";
        public string AutorunFile { get; set; } = "";

        public int CurrentUsageCount { get; set; } = 0;
    }

    public class PolicyManager
    {
        private string role;
        public Policy CurrentPolicy { get; private set; }
        public string VaultPath { get; private set; }
        private string policyFilePath;

        public PolicyManager()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            VaultPath = Path.Combine(appDir, "VaultData");
            if (!Directory.Exists(VaultPath))
            {
                var di = Directory.CreateDirectory(VaultPath);
                di.Attributes |= FileAttributes.Hidden | FileAttributes.System;
            }
            policyFilePath = Path.Combine(VaultPath, "policy.cfg");
            CurrentPolicy = new Policy();
        }

        public void SetCurrentUserRole(string r)
        {
            this.role = r;
            LoadPolicy();

            if (CurrentPolicy.UsageCountLimit > 0)
            {
                CurrentPolicy.CurrentUsageCount++;
                SavePolicy();
            }
        }

        public void SetCurrentUserRole(UserRole r)
        {
            SetCurrentUserRole(r.ToString());
        }

        public string GetCurrentUserRole() => role;

        private void LoadPolicy()
        {
            if (!File.Exists(policyFilePath)) return;
            string[] lines = File.ReadAllLines(policyFilePath);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length > 1 && parts[0] == role)
                {
                    var props = parts[1].Split(';');
                    foreach (var p in props)
                    {
                        var kv = p.Split('=');
                        if (kv.Length == 2)
                        {
                            switch (kv[0])
                            {
                                case "DisableCopy":          CurrentPolicy.DisableCopy          = bool.Parse(kv[1]); break;
                                case "DisablePrint":         CurrentPolicy.DisablePrint         = bool.Parse(kv[1]); break;
                                case "DisableClipboard":     CurrentPolicy.DisableClipboard     = bool.Parse(kv[1]); break;
                                case "DisableScreenCapture": CurrentPolicy.DisableScreenCapture = bool.Parse(kv[1]); break;
                                case "DisableEmail":         CurrentPolicy.DisableEmail         = bool.Parse(kv[1]); break;
                                case "DisableModify":        CurrentPolicy.DisableModify        = bool.Parse(kv[1]); break;
                                case "DisableDelete":        CurrentPolicy.DisableDelete        = bool.Parse(kv[1]); break;
                                case "DisableRename":        CurrentPolicy.DisableRename        = bool.Parse(kv[1]); break;
                                case "DisableExport":        CurrentPolicy.DisableExport        = bool.Parse(kv[1]); break;
                                case "DisableExecution":     CurrentPolicy.DisableExecution     = bool.Parse(kv[1]); break;
                                case "DisableNewFolder":     CurrentPolicy.DisableNewFolder     = bool.Parse(kv[1]); break;
                                case "DisableSaveAs":        CurrentPolicy.DisableSaveAs        = bool.Parse(kv[1]); break;
                                case "UsageCountLimit":      CurrentPolicy.UsageCountLimit      = int.Parse(kv[1]);  break;
                                case "CurrentUsageCount":    CurrentPolicy.CurrentUsageCount    = int.Parse(kv[1]);  break;
                                case "AllowedIPs":           CurrentPolicy.AllowedIPs           = kv[1]; break;
                                case "AutorunFile":          CurrentPolicy.AutorunFile          = kv[1]; break;
                                case "ExpirationDate":
                                    if (DateTime.TryParse(kv[1], out DateTime dt)) CurrentPolicy.ExpirationDate = dt;
                                    break;
                            }
                        }
                    }
                }
            }
        }

        public void SavePolicy()
        {
            string serialized =
                $"DisableCopy={CurrentPolicy.DisableCopy};DisablePrint={CurrentPolicy.DisablePrint};DisableClipboard={CurrentPolicy.DisableClipboard};" +
                $"DisableScreenCapture={CurrentPolicy.DisableScreenCapture};DisableEmail={CurrentPolicy.DisableEmail};DisableModify={CurrentPolicy.DisableModify};" +
                $"DisableDelete={CurrentPolicy.DisableDelete};DisableRename={CurrentPolicy.DisableRename};DisableExport={CurrentPolicy.DisableExport};" +
                $"DisableExecution={CurrentPolicy.DisableExecution};DisableNewFolder={CurrentPolicy.DisableNewFolder};DisableSaveAs={CurrentPolicy.DisableSaveAs};" +
                $"UsageCountLimit={CurrentPolicy.UsageCountLimit};CurrentUsageCount={CurrentPolicy.CurrentUsageCount};AllowedIPs={CurrentPolicy.AllowedIPs};" +
                $"AutorunFile={CurrentPolicy.AutorunFile};ExpirationDate={CurrentPolicy.ExpirationDate?.ToString("yyyy-MM-dd HH:mm:ss")}";

            string line = $"{role}|{serialized}";

            if (!File.Exists(policyFilePath))
            {
                File.WriteAllLines(policyFilePath, new string[] { line });
                return;
            }
            var lines = new List<string>(File.ReadAllLines(policyFilePath));
            var idx = lines.FindIndex(l => l.StartsWith(role + "|"));
            if (idx >= 0) lines[idx] = line;
            else lines.Add(line);
            File.WriteAllLines(policyFilePath, lines);
        }

        public bool HasAccessPermission(AccessType accessType)
        {
            if (role == "Admin") return true;

            switch (accessType)
            {
                case AccessType.Read:   return true;
                case AccessType.Write:  return !CurrentPolicy.DisableModify;
                case AccessType.Copy:   return !CurrentPolicy.DisableCopy;
                case AccessType.Delete: return !CurrentPolicy.DisableDelete;
            }
            return false;
        }
    }

    public enum AccessType
    {
        Read,
        Write,
        Copy,
        Delete
    }

    public enum UserRole
    {
        Guest,
        User,
        Admin
    }
}
