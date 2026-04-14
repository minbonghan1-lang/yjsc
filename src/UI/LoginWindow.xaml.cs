using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace UI
{
    public partial class LoginWindow : Window
    {
        private string configPath;
        private bool isFirstRun;

        public LoginWindow()
        {
            InitializeComponent();

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string vaultPath = Path.Combine(appDir, "VaultData");
            if (!Directory.Exists(vaultPath))
            {
                var di = Directory.CreateDirectory(vaultPath);
                di.Attributes |= FileAttributes.Hidden | FileAttributes.System;
            }
            else
            {
                var di = new DirectoryInfo(vaultPath);
                di.Attributes |= FileAttributes.Hidden | FileAttributes.System;
            }

            configPath = Path.Combine(vaultPath, "vault.cfg");
            isFirstRun = !File.Exists(configPath);

            if (isFirstRun)
            {
                if (Directory.GetFiles(vaultPath).Length > 0)
                {
                    MessageBox.Show("보안 설정 데이터가 손상되었습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                    return;
                }

                txtTitle.Text = "관리자 비밀번호를 설정하세요.";
                btnLogin.Content = "설정 및 시작";
                txtConfirmTitle.Visibility = Visibility.Visible;
                pwdConfirmBox.Visibility = Visibility.Visible;
                chkAutoLogin.Visibility = Visibility.Collapsed;
            }
            else
            {
                string savedPwd = Core.KeyStore.GetAutoLogin();
                if (!string.IsNullOrEmpty(savedPwd))
                {
                    pwdBox.Password = savedPwd;
                    chkAutoLogin.IsChecked = true;
                    ProcessLogin();
                }
            }

            pwdBox.Focus();
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            ProcessLogin();
        }

        private void pwdBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ProcessLogin();
        }

        private void ProcessLogin()
        {
            string inputPwd = pwdBox.Password;
            if (string.IsNullOrWhiteSpace(inputPwd))
            {
                MessageBox.Show("비밀번호를 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (isFirstRun)
            {
                string confirmPwd = pwdConfirmBox.Password;
                if (inputPwd != confirmPwd)
                {
                    MessageBox.Show("비밀번호가 서로 일치하지 않습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    Core.KeyStore.InitializeVaultData(configPath, inputPwd);
                    var result = Core.KeyStore.TryGetMasterDataKey(configPath, inputPwd);
                    if (chkAutoLogin.IsChecked == true) Core.KeyStore.SaveAutoLogin(inputPwd);

                    MessageBox.Show("관리자 계정이 설정되었습니다.", "설정 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                    OpenMainWindow(result.mdk, result.role);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("오류 발생: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                var result = Core.KeyStore.TryGetMasterDataKey(configPath, inputPwd);
                if (result.mdk != null)
                {
                    if (chkAutoLogin.IsChecked == true)
                        Core.KeyStore.SaveAutoLogin(inputPwd);
                    else
                        Core.KeyStore.ClearAutoLogin();

                    OpenMainWindow(result.mdk, result.role);
                }
                else
                {
                    MessageBox.Show("비밀번호가 일치하지 않습니다.", "인증 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                    pwdBox.Clear();
                    Core.KeyStore.ClearAutoLogin();
                }
            }
        }

        private void OpenMainWindow(byte[] mdk, string role)
        {
            MainWindow mainWindow = new MainWindow(mdk, role);
            mainWindow.Show();
            this.Close();
        }
    }
}
