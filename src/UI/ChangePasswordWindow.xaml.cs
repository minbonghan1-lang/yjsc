using System;
using System.IO;
using System.Windows;
using Core;

namespace UI
{
    public partial class ChangePasswordWindow : Window
    {
        private string configPath;
        private byte[] currentMdk;
        private string currentRole;

        public bool IsChanged { get; private set; } = false;

        public ChangePasswordWindow(byte[] mdk, string role)
        {
            InitializeComponent();
            currentMdk = mdk;
            currentRole = role;
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            configPath = Path.Combine(appDir, "VaultData", "vault.cfg");
            pwdCurrent.Focus();
        }

        private void btnConfirm_Click(object sender, RoutedEventArgs e) => ProcessChangePassword();

        private void ProcessChangePassword()
        {
            string currentPwd = pwdCurrent.Password;
            string newPwd = pwdNew.Password;

            if (string.IsNullOrWhiteSpace(currentPwd) || string.IsNullOrWhiteSpace(newPwd))
            {
                MessageBox.Show("비밀번호를 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = KeyStore.TryGetMasterDataKey(configPath, currentPwd);
            if (result.mdk == null || result.role != currentRole)
            {
                MessageBox.Show("현재 비밀번호가 일치하지 않습니다.", "인증 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                KeyStore.ChangeUserPassword(configPath, currentMdk, currentRole, newPwd);
                IsChanged = true;
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("변경 중 오류 발생: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
