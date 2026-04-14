using System;
using System.IO;
using System.Windows;

namespace UI
{
    public partial class LogViewerWindow : Window
    {
        public LogViewerWindow()
        {
            InitializeComponent();
            LoadLogs();
        }

        private void LoadLogs()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string logPath = Path.Combine(appDir, "VaultData", "access.log");

            if (File.Exists(logPath))
            {
                var lines = File.ReadAllLines(logPath);
                foreach (var line in lines)
                    lstLogs.Items.Add(line);
            }
            else
            {
                lstLogs.Items.Add("[System] 로그 파일이 존재하지 않습니다.");
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e) => this.Close();

        public static void WriteLog(string message)
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string logPath = Path.Combine(appDir, "VaultData", "access.log");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(logPath, logEntry + Environment.NewLine);
            }
            catch { }
        }
    }
}
