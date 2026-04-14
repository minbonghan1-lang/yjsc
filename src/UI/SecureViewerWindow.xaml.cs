using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace UI
{
    public partial class SecureViewerWindow : Window
    {
        private const uint WDA_NONE               = 0x00000000;
        private const uint WDA_MONITOR            = 0x00000001;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        private string _tempFilePath;

        public SecureViewerWindow(string tempFilePath)
        {
            InitializeComponent();
            _tempFilePath = tempFilePath;
            // CaptureShield.StartShield()는 MainWindow_Loaded에서 이미 시작됨
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyCaptureProtection();
            this.Topmost = true;
            await LoadFileAsync();
        }

        private void ApplyCaptureProtection()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                uint result = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
                if (result != 0)
                    SetWindowDisplayAffinity(hwnd, WDA_MONITOR);
            }
            catch { }
        }

        private async System.Threading.Tasks.Task LoadFileAsync()
        {
            try
            {
                string ext = Path.GetExtension(_tempFilePath).ToLower();

                // ── 이미지 파일 ──────────────────────────────────────────────
                if (ext == ".jpg" || ext == ".png" || ext == ".bmp"
                    || ext == ".jpeg" || ext == ".gif" || ext == ".webp")
                {
                    imgViewer.Visibility = Visibility.Visible;
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(new Uri(_tempFilePath).AbsoluteUri);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    imgViewer.Source = bitmap;
                }
                // ── 텍스트 파일 ──────────────────────────────────────────────
                else if (ext == ".txt" || ext == ".log" || ext == ".csv")
                {
                    txtViewer.Visibility = Visibility.Visible;
                    txtViewer.Text = File.ReadAllText(_tempFilePath);
                }
                // ── PDF: WebView2로 렌더링 ───────────────────────────────────
                else if (ext == ".pdf")
                {
                    pdfViewer.Visibility = Visibility.Visible;

                    string userDataPath = Path.Combine(Path.GetTempPath(), "YJSC_SecureTemp", "WebView2Data");
                    var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataPath);
                    await pdfViewer.EnsureCoreWebView2Async(env);

                    ApplyCaptureProtection();

                    pdfViewer.CoreWebView2.Settings.AreDefaultContextMenusEnabled   = false;
                    pdfViewer.CoreWebView2.Settings.AreDevToolsEnabled              = false;
                    pdfViewer.CoreWebView2.Settings.IsStatusBarEnabled              = false;
                    pdfViewer.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

                    pdfViewer.CoreWebView2.DownloadStarting += (s, ev) => ev.Cancel = true;

                    // 한글/공백 경로 대응: WebView2는 로컬 파일 경로(예: C:\Temp\문서.pdf)를 그대로 받을 수 있습니다.
                    // AbsoluteUri를 사용하면 '%20' 등으로 인코딩되어 오히려 로컬 파일을 찾지 못합니다.
                    pdfViewer.CoreWebView2.Navigate(_tempFilePath);

                    pdfViewer.CoreWebView2.NavigationCompleted += async (s, ev) =>
                    {
                        try
                        {
                            await pdfViewer.CoreWebView2.ExecuteScriptAsync(@"
                                (function() {
                                    document.addEventListener('contextmenu', e => e.preventDefault(), true);
                                    document.addEventListener('keydown', function(e) {
                                        if (e.ctrlKey && (e.key === 's' || e.key === 'p' ||
                                            e.key === 'u' || e.key === 'a')) {
                                            e.preventDefault(); e.stopPropagation();
                                        }
                                        if (e.key === 'F12') { e.preventDefault(); e.stopPropagation(); }
                                        if (e.ctrlKey && e.shiftKey && e.key === 'I') {
                                            e.preventDefault(); e.stopPropagation();
                                        }
                                        if (e.key === 'PrintScreen') { e.preventDefault(); e.stopPropagation(); }
                                    }, true);
                                    document.addEventListener('selectstart', e => e.preventDefault(), true);
                                    document.addEventListener('dragstart',   e => e.preventDefault(), true);
                                    document.body.style.userSelect = 'none';
                                })();
                            ");
                        }
                        catch { }
                    };
                }

                else
                {
                    MessageBox.Show($"'{ext}' 형식은 보안 뷰어에서 지원되지 않습니다.",
                        "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파일을 불러오는 중 오류가 발생했습니다:\n{ex.Message}",
                    "에러", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.PrintScreen || e.SystemKey == Key.PrintScreen)
            { e.Handled = true; return; }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                && (e.Key == Key.C || e.Key == Key.P || e.Key == Key.S))
            { e.Handled = true; return; }

            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt
                && (e.Key == Key.PrintScreen || e.SystemKey == Key.PrintScreen))
            { e.Handled = true; }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                if (pdfViewer?.CoreWebView2 != null)
                    pdfViewer.Dispose();
            }
            catch { }

            imgViewer.Source = null;

            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(1000);
                try
                {
                    // UUID 서브폴더 전체 제거 (Office 파일 닫힌 대기)
                    string parentDir = Path.GetDirectoryName(_tempFilePath);
                    string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    string yjscTemp = Path.Combine(myDocs, "YJSC_SecureTemp");
                    if (Directory.Exists(parentDir) && parentDir.StartsWith(yjscTemp, StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(parentDir, true);
                    else if (File.Exists(_tempFilePath))
                        File.Delete(_tempFilePath);
                }
                catch { }
            });
        }
    }
}
