using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SatisfactorySaveEditor.Util
{
    /// <summary>
    /// Windows 11 / 10 (1809+) の DWM immersive dark mode を WPF Window のタイトルバーへ適用する。
    /// アプリ本体が macOS Tahoe Dark なのに対し、WPF のクロムは OS が描くため設定しないと白いまま残る。
    /// </summary>
    public static class WindowDarkMode
    {
        // Windows 10 1809 (build 17763) で導入され、19H1 で番号が 20 に固定された
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        // 1809 ベータでの旧番号 (fallback として両方試す)
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>
        /// Application.MainWindow を含む全 Window の SourceInitialized でタイトルバーをダーク化するように登録する。
        /// </summary>
        public static void RegisterGlobalHook()
        {
            // Window.LoadedEvent は RoutedEvent なので EventManager に登録可能。
            // SourceInitialized は CLR イベントなので EventManager から hook できないため Loaded を使う。
            EventManager.RegisterClassHandler(typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, e) => Apply((Window)s)));
        }

        public static void Apply(Window window)
        {
            if (window == null) return;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int useDark = 1;
            // 19H1 以降の番号で試して失敗したら 1809 ベータの旧番号にフォールバック。どちらも失敗したら静かに無視
            var hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            if (hr != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, sizeof(int));
            }
        }
    }
}
