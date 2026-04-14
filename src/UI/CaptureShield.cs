using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace UI
{
    public class CaptureShield
    {
        private static bool _isRunning = false;
        private static CancellationTokenSource _cts = new CancellationTokenSource();
        public static bool DisableClipboard { get; set; } = true;

        // ─────────────────────────────────────────────────────────────────
        // 블랙리스트: 캡처·녹화·포맷·파티션 도구 차단
        // ※ 문서 뷰어(POWERPNT, hwp 등)는 제외 — 파일 열기 기능과 충돌
        // ─────────────────────────────────────────────────────────────────
        private static readonly string[] _blacklistedProcesses = new string[]
        {
            // ── 화면 캡처 / 스크린샷 도구 ──
            "SnippingTool",         // 윈도우 기본 캡처 도구
            "ScreenClippingHost",   // Win+Shift+S 캡처 스케치
            "ShareX",               // ShareX
            "Lightshot",            // 라이트샷
            "picpick",              // 픽픽
            "Snagit32",             // Snagit
            "Snagit64",             // Snagit
            "greenshot",            // Greenshot
            "Gyazo",                // Gyazo
            "Nimbus",               // Nimbus Screenshot
            "screenrec",            // Screen Recorder

            // ── 화면 녹화 도구 ──
            "obs32",                // OBS Studio
            "obs64",                // OBS Studio
            "obs",                  // OBS (일반)
            "xsplit",               // XSplit
            "camtasia",             // Camtasia
            "bandicam",             // 반디캠
            "bdcam",                // 반디캠
            "fraps",                // 프랩스
            "GifCam",               // GifCam
            "ScreenToGif",          // ScreenToGif
            "DU_Recorder",          // DU Recorder
            "ApowerREC",            // Apowersoft Recorder

            // ── USB 포맷 / 파티션 도구 ──
            "format",               // cmd format.exe
            "diskpart",             // diskpart.exe
            "diskmgmt",             // 디스크 관리
            "DiskGenius",           // DiskGenius
            "PartitionWizard",      // MiniTool Partition Wizard
            "AOMEI",                // AOMEI Partition Assistant
            "rufus",                // Rufus
            "Etcher",               // balenaEtcher
            "win32diskimager",      // Win32 Disk Imager
            "UNetbootin",           // UNetbootin
            "USBDeview",            // USB Device Viewer
        };

        // ─────────────────────────────────────────────────────────────────
        // 볼륨 잠금 API (FSCTL_LOCK_VOLUME)
        // ─────────────────────────────────────────────────────────────────
        private const uint FSCTL_LOCK_VOLUME   = 0x00090018;
        private const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
        private const uint GENERIC_READ        = 0x80000000;
        private const uint GENERIC_WRITE       = 0x40000000;
        private const uint FILE_SHARE_READ     = 0x00000001;
        private const uint FILE_SHARE_WRITE    = 0x00000002;
        private const uint OPEN_EXISTING       = 3;
        private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice, uint dwIoControlCode,
            IntPtr lpInBuffer, uint nInBufferSize,
            IntPtr lpOutBuffer, uint nOutBufferSize,
            out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private static IntPtr _volumeHandle = IntPtr.Zero;
        private static readonly object _lockObj = new object();

        public static bool LockVolume()
        {
            lock (_lockObj)
            {
                if (_volumeHandle != IntPtr.Zero && _volumeHandle != INVALID_HANDLE_VALUE)
                    return true;

                try
                {
                    string appDir = AppDomain.CurrentDomain.BaseDirectory;
                    string driveLetter = Path.GetPathRoot(appDir)?.TrimEnd('\\');
                    if (string.IsNullOrEmpty(driveLetter)) return false;

                    var driveInfo = new DriveInfo(driveLetter);
                    if (driveInfo.DriveType != DriveType.Removable &&
                        driveInfo.DriveType != DriveType.Network)
                    {
                        // 고정 드라이브(C:)는 잠금 생략
                        return false;
                    }

                    string volumePath = $@"\\.\{driveLetter}";
                    IntPtr handle = CreateFile(
                        volumePath,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        FILE_FLAG_NO_BUFFERING,
                        IntPtr.Zero);

                    if (handle == INVALID_HANDLE_VALUE) return false;

                    bool locked = DeviceIoControl(
                        handle, FSCTL_LOCK_VOLUME,
                        IntPtr.Zero, 0,
                        IntPtr.Zero, 0,
                        out _, IntPtr.Zero);

                    if (locked) _volumeHandle = handle;
                    else CloseHandle(handle);

                    return locked;
                }
                catch { return false; }
            }
        }

        public static void UnlockVolume()
        {
            lock (_lockObj)
            {
                if (_volumeHandle == IntPtr.Zero || _volumeHandle == INVALID_HANDLE_VALUE) return;
                try
                {
                    DeviceIoControl(_volumeHandle, FSCTL_UNLOCK_VOLUME,
                        IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                    CloseHandle(_volumeHandle);
                }
                catch { }
                finally { _volumeHandle = IntPtr.Zero; }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 캡처 쉴드 시작 / 중지
        // ─────────────────────────────────────────────────────────────────
        public static void StartShield()
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = new CancellationTokenSource();

            // 전역 키보드 후킹 (PrintScreen 차단)
            try
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule? curModule = curProcess.MainModule)
                {
                    if (curModule != null)
                    {
                        _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                            GetModuleHandle(curModule.ModuleName), 0);
                    }
                }
            }
            catch { }

            // 백그라운드 감시 루프
            Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    KillBlacklistedProcesses();

                    if (DisableClipboard)
                    {
                        try
                        {
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                Clipboard.Clear();
                            });
                        }
                        catch { }
                    }

                    await Task.Delay(200, _cts.Token);
                }
            }, _cts.Token);
        }

        public static void StopShield()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _cts?.Cancel();
            if (_hookID != IntPtr.Zero)
                UnhookWindowsHookEx(_hookID);
        }

        private static void KillBlacklistedProcesses()
        {
            foreach (var processName in _blacklistedProcesses)
            {
                try
                {
                    var procs = Process.GetProcessesByName(processName);
                    foreach (var p in procs)
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Low-Level Keyboard Hook (PrintScreen 차단)
        // ─────────────────────────────────────────────────────────────────
        private const int WH_KEYBOARD_LL = 13;
        private const int VK_SNAPSHOT    = 0x2C;
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == VK_SNAPSHOT)
                    return (IntPtr)1; // PrintScreen 차단
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
    }
}
