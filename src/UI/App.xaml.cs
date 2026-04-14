using System;
using System.IO;
using System.Windows;

namespace UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. USB 볼륨 잠금 — 외부 포맷(탐색기 우클릭, format.exe, diskpart) 차단
        CaptureShield.LockVolume();

        // 2. USB 내 비실행 파일/폴더 숨김 처리
        HideNonExecutableFiles();

        // 3. 로그인 창 표시
        LoginWindow loginWindow = new LoginWindow();
        loginWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 볼륨 잠금 해제 → 정상 종료 후 USB 제거 가능
        CaptureShield.UnlockVolume();

        // 캡처 쉴드 종료
        CaptureShield.StopShield();

        base.OnExit(e);
    }

    /// <summary>
    /// 앱 실행 디렉토리에서 .exe 외 모든 파일과 VaultData를 제외한 폴더를 숨김 처리합니다.
    /// </summary>
    private static void HideNonExecutableFiles()
    {
        try
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (string filePath in Directory.GetFiles(appDir))
            {
                string ext = Path.GetExtension(filePath).ToLower();
                if (ext == ".exe") continue;

                try
                {
                    FileInfo fi = new FileInfo(filePath);
                    if ((fi.Attributes & FileAttributes.Hidden) == 0 ||
                        (fi.Attributes & FileAttributes.System) == 0)
                    {
                        fi.Attributes |= FileAttributes.Hidden | FileAttributes.System;
                    }
                }
                catch { }
            }

            foreach (string dirPath in Directory.GetDirectories(appDir))
            {
                try
                {
                    DirectoryInfo di = new DirectoryInfo(dirPath);
                    if ((di.Attributes & FileAttributes.Hidden) == 0 ||
                        (di.Attributes & FileAttributes.System) == 0)
                    {
                        di.Attributes |= FileAttributes.Hidden | FileAttributes.System;
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
