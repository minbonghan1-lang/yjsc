using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Core;

namespace UI
{
    public partial class MainWindow : Window
    {
        // 화면 캡처 방지 API
        const uint WDA_NONE               = 0x00000000;
        const uint WDA_MONITOR            = 0x00000001;
        const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        // 자연 정렬 (1,2,3...10,11 순)
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string x, string y);

        private PolicyManager policyManager;
        private byte[] vaultKey;
        private string currentVaultPath;

        public MainWindow(byte[] mdk, string role)
        {
            InitializeComponent();
            vaultKey = mdk;
            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;

            policyManager = new PolicyManager();
            policyManager.SetCurrentUserRole(role);
            currentVaultPath = policyManager.VaultPath;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LogViewerWindow.WriteLog($"[{policyManager.GetCurrentUserRole()}] 로그인을 완료하고 보안 탐색기에 접근했습니다.");

            // 만료일/사용 횟수 체크
            if (policyManager.CurrentPolicy.ExpirationDate.HasValue && DateTime.Now > policyManager.CurrentPolicy.ExpirationDate.Value)
            {
                MessageBox.Show("보안 정책: 이 계정의 사용 기한이 만료되었습니다.", "사용 불가", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }
            if (policyManager.CurrentPolicy.UsageCountLimit > 0 && policyManager.CurrentPolicy.CurrentUsageCount > policyManager.CurrentPolicy.UsageCountLimit)
            {
                MessageBox.Show("보안 정책: 이 계정의 최대 사용 횟수를 초과했습니다.", "사용 불가", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            // 화면 캡처 방지 적용
            if (policyManager.CurrentPolicy.DisableScreenCapture)
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE) != 0)
                    SetWindowDisplayAffinity(hwnd, WDA_MONITOR);
            }

            // CaptureShield 시작
            if (policyManager.CurrentPolicy.DisableScreenCapture || policyManager.CurrentPolicy.DisableClipboard)
            {
                CaptureShield.DisableClipboard = policyManager.CurrentPolicy.DisableClipboard;
                CaptureShield.StartShield();
            }

            txtUserRole.Text = policyManager.GetCurrentUserRole().ToString();
            LoadVaultDirectory();
        }

        private void LoadVaultDirectory()
        {
            fileListView.Items.Clear();

            string relativePath = currentVaultPath.Length >= policyManager.VaultPath.Length
                ? currentVaultPath.Substring(policyManager.VaultPath.Length) : "";
            txtCurrentPath.Text = string.IsNullOrEmpty(relativePath)
                ? "현재 위치: /"
                : "현재 위치: /" + relativePath.TrimStart('\\').Replace('\\', '/');

            if (policyManager.HasAccessPermission(AccessType.Read))
            {
                if (Directory.Exists(currentVaultPath))
                {
                    // 폴더 목록: 자연 정렬
                    var dirs = new List<DirectoryInfo>(new DirectoryInfo(currentVaultPath).GetDirectories());
                    dirs.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));

                    foreach (var dir in dirs)
                    {
                        fileListView.Items.Add(new FileItem
                        {
                            Name         = "📁 " + dir.Name,
                            Size         = "",
                            DateModified = dir.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                            IsDirectory  = true,
                            ActualName   = dir.Name
                        });
                    }

                    // 파일 목록: 자연 정렬
                    var files = new List<FileInfo>(new DirectoryInfo(currentVaultPath).GetFiles());
                    files.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));

                    foreach (var file in files)
                    {
                        // 시스템 파일 숨김
                        if (currentVaultPath == policyManager.VaultPath &&
                            (file.Name == "vault.cfg" || file.Name == "policy.cfg" ||
                             file.Name == "autologin.cfg" || file.Name == "access.log"))
                            continue;

                        fileListView.Items.Add(new FileItem
                        {
                            Name         = "📄 " + file.Name,
                            Size         = file.Length >= 1024 * 1024
                                           ? $"{file.Length / 1024 / 1024:F1} MB"
                                           : $"{file.Length / 1024} KB",
                            DateModified = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                            IsDirectory  = false,
                            ActualName   = file.Name
                        });
                    }
                }
            }
            else
            {
                MessageBox.Show("접근 권한이 없습니다!", "보안 경고", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnUp_Click(object sender, RoutedEventArgs e)
        {
            if (currentVaultPath.Length > policyManager.VaultPath.Length)
            {
                DirectoryInfo parent = Directory.GetParent(currentVaultPath);
                if (parent != null)
                {
                    currentVaultPath = parent.FullName;
                    LoadVaultDirectory();
                }
            }
        }

        // 열어본 암호화 해제된 임시 폴더 목록 (삭제 시 UUID 폴더 전체 제거)
        private List<string> tempDecryptedDirs = new List<string>();

        private void btnChangePassword_Click(object sender, RoutedEventArgs e)
        {
            ChangePasswordWindow pwdWindow = new ChangePasswordWindow(vaultKey, policyManager.GetCurrentUserRole());
            if (pwdWindow.ShowDialog() == true && pwdWindow.IsChanged)
            {
                MessageBox.Show("비밀번호가 성공적으로 변경되었습니다.", "변경 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void btnFormat_Click(object sender, RoutedEventArgs e)
        {
            if (policyManager.GetCurrentUserRole() != "Admin")
            {
                MessageBox.Show("볼륨 포맷은 관리자(Admin) 계정만 사용할 수 있습니다.", "접근 거부", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            PasswordPromptWindow prompt = new PasswordPromptWindow(requireAdminOnly: true);
            if (prompt.ShowDialog() == true && prompt.IsAuthorized)
            {
                var confirm = MessageBox.Show("⚠️ 볼륨의 모든 파일이 영구 삭제됩니다.\n정말 포맷하시겠습니까?",
                    "최종 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;

                try
                {
                    var dir = new DirectoryInfo(policyManager.VaultPath);

                    foreach (var file in dir.GetFiles())
                    {
                        if (file.Name != "vault.cfg" && file.Name != "policy.cfg" &&
                            file.Name != "autologin.cfg" && file.Name != "access.log")
                            file.Delete();
                    }

                    foreach (var subDir in dir.GetDirectories())
                    {
                        try { subDir.Delete(recursive: true); } catch { }
                    }

                    LogViewerWindow.WriteLog($"[Admin] 볼륨 포맷 실행됨.");
                    MessageBox.Show("볼륨 포맷이 완료되었습니다.", "초기화", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadVaultDirectory();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("초기화 중 오류 발생: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e) => LoadVaultDirectory();

        private void ResetProgressUI()
        {
            txtStatus.Text = "대기 중...";
            progressBar.Visibility = Visibility.Hidden;
            txtProgressPercent.Visibility = Visibility.Hidden;
            progressBar.Value = 0;
            txtProgressPercent.Text = "0%";
        }

        private Progress<int> CreateProgressReporter(string actionName)
        {
            txtStatus.Text = actionName;
            progressBar.Visibility = Visibility.Visible;
            txtProgressPercent.Visibility = Visibility.Visible;
            progressBar.Value = 0;
            txtProgressPercent.Text = "0%";

            return new Progress<int>(percent =>
            {
                progressBar.Value = percent;
                txtProgressPercent.Text = $"{percent}%";
            });
        }

        private void btnAddCombined_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn != null && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private async void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!policyManager.HasAccessPermission(AccessType.Write))
            {
                MessageBox.Show("쓰기 권한이 없습니다.", "보안 경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Multiselect = true;
            dlg.Title = "보안 드라이브에 추가할 파일 선택";

            if (dlg.ShowDialog() == true)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                foreach (string file in dlg.FileNames)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            string destFile = Path.Combine(currentVaultPath, Path.GetFileName(file));
                            var progress = CreateProgressReporter($"암호화 중: {Path.GetFileName(file)}");
                            await CryptoManager.EncryptFileAsync(file, destFile, vaultKey, progress);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"파일 암호화 중 오류 발생: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                ResetProgressUI();
                Mouse.OverrideCursor = null;
                LoadVaultDirectory();
            }
        }

        private async void btnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!policyManager.HasAccessPermission(AccessType.Write))
            {
                MessageBox.Show("쓰기 권한이 없습니다.", "보안 경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Microsoft.Win32.OpenFolderDialog dlg = new Microsoft.Win32.OpenFolderDialog();
            dlg.Multiselect = true;
            dlg.Title = "보안 드라이브에 추가할 폴더 선택";

            if (dlg.ShowDialog() == true)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                foreach (string folder in dlg.FolderNames)
                {
                    try
                    {
                        if (Directory.Exists(folder))
                            await ProcessAndEncryptDirectoryAsync(folder, currentVaultPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"폴더 암호화 중 오류 발생: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                ResetProgressUI();
                Mouse.OverrideCursor = null;
                LoadVaultDirectory();
            }
        }

        private async System.Threading.Tasks.Task ProcessAndEncryptDirectoryAsync(string sourceDirPath, string targetVaultDir)
        {
            DirectoryInfo sourceDir = new DirectoryInfo(sourceDirPath);
            string newTargetDir = Path.Combine(targetVaultDir, sourceDir.Name);
            if (!Directory.Exists(newTargetDir)) Directory.CreateDirectory(newTargetDir);

            foreach (var file in sourceDir.GetFiles())
            {
                string destFile = Path.Combine(newTargetDir, file.Name);
                var progress = CreateProgressReporter($"암호화 중: {file.Name}");
                await CryptoManager.EncryptFileAsync(file.FullName, destFile, vaultKey, progress);
            }

            foreach (var subDir in sourceDir.GetDirectories())
                await ProcessAndEncryptDirectoryAsync(subDir.FullName, newTargetDir);
        }

        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (policyManager.CurrentPolicy.DisableDelete)
            {
                MessageBox.Show("보안 정책에 의해 파일 삭제가 금지되어 있습니다.", "권한 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (fileListView.SelectedItem is FileItem selectedItem)
            {
                PasswordPromptWindow prompt = new PasswordPromptWindow();
                if (prompt.ShowDialog() == true && prompt.IsAuthorized)
                {
                    LogViewerWindow.WriteLog($"[{policyManager.GetCurrentUserRole()}] 항목 삭제 시도: {selectedItem.ActualName}");
                    string targetPath = Path.Combine(currentVaultPath, selectedItem.ActualName);
                    if (selectedItem.IsDirectory && Directory.Exists(targetPath))
                    {
                        Directory.Delete(targetPath, true);
                        MessageBox.Show("폴더가 삭제되었습니다.", "삭제", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else if (!selectedItem.IsDirectory && File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                        MessageBox.Show("파일이 삭제되었습니다.", "삭제", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    LoadVaultDirectory();
                }
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (!policyManager.HasAccessPermission(AccessType.Write))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!policyManager.HasAccessPermission(AccessType.Write))
            {
                MessageBox.Show("쓰기 권한이 없습니다.", "보안 경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] items = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string item in items)
                {
                    try
                    {
                        if (Directory.Exists(item))
                            await ProcessAndEncryptDirectoryAsync(item, currentVaultPath);
                        else if (File.Exists(item))
                        {
                            string destFile = Path.Combine(currentVaultPath, Path.GetFileName(item));
                            var progress = CreateProgressReporter($"암호화 중: {Path.GetFileName(item)}");
                            await CryptoManager.EncryptFileAsync(item, destFile, vaultKey, progress);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"동작 중 오류 발생: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                ResetProgressUI();
                LoadVaultDirectory();
            }
        }
else if (useExternalExecution)
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName        = tempPath,
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        System.Diagnostics.Process.Start(psi);
    }
// ... 이하 생략
                    if (policyManager.CurrentPolicy.DisableExecution)
                    {
                        MessageBox.Show("보안 정책에 의해 파일 실행이 금지되어 있습니다.", "접근 거부", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string encryptedPath = Path.Combine(currentVaultPath, selectedItem.ActualName);
                    if (!File.Exists(encryptedPath))
                    {
                        MessageBox.Show($"파일을 찾을 수 없습니다.\n경로: {encryptedPath}", "파일 없음", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    try
                    {
                        Mouse.OverrideCursor = Cursors.Wait;

                        // [최종 수정] 
                        // 1. USB(LockVolume) 내부에서 벗어남
                        // 2. Office의 강력한 '제한된 보기(Protected View)'가 Temp/AppData 경로를 악성 경로로 
                        //    오인하여 차단하는 문제를 완전히 우회하기 위해 신뢰할 수 있는 '내 문서(My Documents)' 하위 폴더 사용
                        string tempUuid = Guid.NewGuid().ToString("N").Substring(0, 8);
                        string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        string baseTemp = Path.Combine(myDocs, "YJSC_SecureTemp");
                        string tempDir = Path.Combine(baseTemp, tempUuid);
                        try 
                        { 
                            if (!Directory.Exists(baseTemp))
                            {
                                Directory.CreateDirectory(baseTemp);
                            }
                            Directory.CreateDirectory(tempDir); 
                        }
                        catch (Exception dirEx)
                        {
                            Mouse.OverrideCursor = null;
                            MessageBox.Show($"임시 폴더 생성 실패:\n{dirEx.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        // Office 계열은 파일명을 원본 그대로 유지해야 정상 오픈됨
                        string tempPath = Path.Combine(tempDir, selectedItem.ActualName);

                        LogViewerWindow.WriteLog($"[{policyManager.GetCurrentUserRole()}] 파일 열기: {selectedItem.ActualName}");
                        var progress = CreateProgressReporter($"복호화 중: {selectedItem.ActualName}");
                        await CryptoManager.DecryptFileAsync(encryptedPath, tempPath, vaultKey, progress);
                        ResetProgressUI();

                        if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                        {
                            Mouse.OverrideCursor = null;
                            MessageBox.Show("복호화 실패: 파일이 손상되었거나 암호화 키가 올바르지 않습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                            return;
                        }

                        tempDecryptedDirs.Add(tempDir);
                        Mouse.OverrideCursor = null;

                        string ext = Path.GetExtension(tempPath).ToLower();

                        bool useSecureViewer =
                            ext == ".jpg" || ext == ".jpeg" || ext == ".png" ||
                            ext == ".bmp" || ext == ".gif"  || ext == ".webp" ||
                            ext == ".txt" || ext == ".log"  || ext == ".csv" ||
                            ext == ".pdf";

                        bool useExternalExecution =
                            ext == ".ppt" || ext == ".pptx" ||
                            ext == ".hwp" || ext == ".hwpx" ||
                            ext == ".doc" || ext == ".docx" ||
                            ext == ".xls" || ext == ".xlsx";

                        if (useSecureViewer)
                        {
                            try
                            {
                                SecureViewerWindow viewer = new SecureViewerWindow(tempPath);
                                viewer.Show();
                            }
                            catch (Exception viewEx)
                            {
                                MessageBox.Show($"뷰어 창 열기 실패:\n{viewEx.Message}", "뷰어 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        else if (useExternalExecution)
                        {
                            try
                            {
                                var psi = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName        = tempPath,
                                    UseShellExecute = true,
                                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                                };
                                System.Diagnostics.Process.Start(psi);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"연결된 프로그램을 열 수 없습니다:\n{ex.Message}", "실행 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        else
                        {
                            MessageBox.Show($"'{ext}' 형식은 지원되지 않는 파일 형식입니다.\n관리자에게 문의하세요.",
                                "열기 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                            if (File.Exists(tempPath)) File.Delete(tempPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Mouse.OverrideCursor = null;
                        ResetProgressUI();
                        MessageBox.Show($"파일 열기 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            foreach (var dir in tempDecryptedDirs)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
        }

        // 윈도우 메시지 후킹 (클립보드 복사 방지)
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(WndProc);
        }

        private const int WM_COPY = 0x0301;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_COPY)
            {
                if (policyManager.CurrentPolicy.DisableCopy)
                {
                    handled = true;
                    MessageBox.Show("보안 정책에 의해 복사가 금지되어 있습니다.", "접근 거부", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            return IntPtr.Zero;
        }

        private void btnExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void btnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (policyManager.CurrentPolicy.DisableCopy)
            {
                MessageBox.Show("보안 정책에 의해 복사가 금지되어 있습니다.", "권한 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            MessageBox.Show("복사 기능 (파일 지정됨)", "알림");
        }

        private void btnPaste_Click(object sender, RoutedEventArgs e)
        {
            if (!policyManager.HasAccessPermission(AccessType.Write))
            {
                MessageBox.Show("쓰기 권한이 금지되어 있습니다.", "권한 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            MessageBox.Show("붙여넣기 기능", "알림");
        }

        private void btnRename_Click(object sender, RoutedEventArgs e)
        {
            if (policyManager.CurrentPolicy.DisableRename)
            {
                MessageBox.Show("보안 정책에 의해 이름 바꾸기가 금지되어 있습니다.", "권한 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (fileListView.SelectedItem is FileItem selectedItem)
            {
                LogViewerWindow.WriteLog($"[{policyManager.GetCurrentUserRole()}] 이름 변경 시도: {selectedItem.ActualName}");
                MessageBox.Show($"이름 바꾸기 허용됨: {selectedItem.ActualName}", "알림");
            }
        }

        private void btnViewLog_Click(object sender, RoutedEventArgs e)
        {
            LogViewerWindow lw = new LogViewerWindow();
            lw.Show();
        }

        private void btnUnprotect_Click(object sender, RoutedEventArgs e)
        {
            if (policyManager.GetCurrentUserRole() != "Admin")
            {
                MessageBox.Show("이 기능은 관리자(Admin)만 사용할 수 있습니다.", "접근 거부", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            PasswordPromptWindow prompt = new PasswordPromptWindow();
            if (prompt.ShowDialog() == true && prompt.IsAuthorized)
            {
                CaptureShield.StopShield();
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                SetWindowDisplayAffinity(hwnd, WDA_NONE);
                LogViewerWindow.WriteLog($"[Admin] 전체 보안 환경 해제(Unprotect) 실행됨.");
                MessageBox.Show("보안 환경이 해제되었습니다.\n(재시작 시 정책이 다시 로드됩니다.)", "해제 성공", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void btnNewFolder_Click(object sender, RoutedEventArgs e)
        {
            if (policyManager.CurrentPolicy.DisableNewFolder)
            {
                MessageBox.Show("보안 정책에 의해 새 폴더 생성이 금지되어 있습니다.", "권한 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                LogViewerWindow.WriteLog($"[{policyManager.GetCurrentUserRole()}] 새 폴더 생성 시도.");
                Directory.CreateDirectory(Path.Combine(currentVaultPath, "새 폴더"));
                LoadVaultDirectory();
            }
            catch { }
        }
    }

    // ListView 바인딩 모델
    public class FileItem
    {
        public string Name { get; set; }
        public string ActualName { get; set; }
        public string Size { get; set; }
        public string DateModified { get; set; }
        public bool IsDirectory { get; set; }
    }
}
