using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Core;

namespace UI
{
    public partial class PasswordPromptWindow : Window
    {
        private string configPath;

        /// <summary>
        /// true: Admin 계정 비밀번호만 허용 (포맷 등 고권한 작업용)
        /// false: 등록된 계정 중 어느 것이든 허용 (기본 동작)
        /// </summary>
        private bool _requireAdminOnly;

        public bool IsAuthorized { get; private set; } = false;

        public PasswordPromptWindow(bool requireAdminOnly = false)
        {
            InitializeComponent();
            _requireAdminOnly = requireAdminOnly;
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            configPath = Path.Combine(appDir, "VaultData", "vault.cfg");
            pwdBox.Focus();
        }

        private void btnConfirm_Click(object sender, RoutedEventArgs e) => Verify();
        private void pwdBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Verify(); }

        private void Verify()
        {
            string inputPassword = pwdBox.Password;

            if (_requireAdminOnly)
            {
                bool adminOk = VerifyAdminPassword(inputPassword);
                if (adminOk)
                {
                    IsAuthorized = true;
                    this.DialogResult = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show("관리자 비밀번호가 일치하지 않습니다.", "인증 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                    pwdBox.Clear();
                }
            }
            else
            {
                var result = KeyStore.TryGetMasterDataKey(configPath, inputPassword);
                if (result.mdk != null)
                {
                    IsAuthorized = true;
                    this.DialogResult = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show("비밀번호가 일치하지 않습니다.", "인증 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                    pwdBox.Clear();
                }
            }
        }

        private bool VerifyAdminPassword(string password)
        {
            if (!File.Exists(configPath)) return false;

            string[] lines = File.ReadAllLines(configPath);
            foreach (string line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length == 3 && parts[0] == "Admin")
                {
                    try
                    {
                        byte[] salt       = Convert.FromBase64String(parts[1]);
                        byte[] wrappedMdk = Convert.FromBase64String(parts[2]);
                        byte[] kek        = CryptoManager.DeriveKeyFromPassword(password, salt);
                        byte[] mdk        = CryptoManager.UnwrapKey(wrappedMdk, kek);
                        if (mdk != null && mdk.Length == 32) return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
