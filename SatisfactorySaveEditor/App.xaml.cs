using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using SatisfactorySaveEditor.Util;
using SuperLightLogger;

namespace SatisfactorySaveEditor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // log フィールドが生成される前にロギングを構成する（静的フィールド初期化は宣言順に実行される）
        private static readonly object configureSentinel = InitConfigureSentinel();
        private static readonly ILog log = LogManager.GetCurrentClassLogger();

        private static object InitConfigureSentinel()
        {
            ConfigureLogging();
            return new object();
        }

        // 明示的な静的コンストラクタを定義して beforefieldinit 最適化を無効化する。
        // これにより new App() 時点で必ず静的フィールド初期化（= LogManager.Configure）が走る。
        // 省くと CLR が静的初期化を遅延し、MainViewModel が先に GetCurrentClassLogger を呼んで
        // 「Configure 未呼び出し」警告 → NullLoggerFactory 確定 → 以降のログが全部消える事故になる。
        static App() { }

        private static void ConfigureLogging()
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "log.txt");

            // 起動時に旧ログを削除する（旧 NLog の deleteOldFileOnStartup 相当）。削除失敗は無視する
            try { File.Delete(logPath); } catch { /* ignore */ }

            LogManager.Configure(builder =>
            {
                builder.SetMinimumLevel("Debug");
                builder.AddSuperLightFile(opt =>
                {
                    opt.FileName = logPath;
                    opt.Layout = @"${longdate} - ${level:uppercase=true}: ${message}${onexception:${newline}EXCEPTION\: ${exception:format=tostring}}";
                    // クラッシュ時に直近のログが失われないよう、書き込みごとにフラッシュする（旧 NLog の既定挙動相当）
                    opt.AutoFlush = true;
                });
            });
        }

        protected override void OnStartup(StartupEventArgs ev)
        {
            base.OnStartup(ev);

            AppDomain.CurrentDomain.UnhandledException += (s, e) => log.Error(e.ExceptionObject);
            DispatcherUnhandledException += (s, e) => log.Error(e.Exception);
            TaskScheduler.UnobservedTaskException += (s, e) => log.Error(e.Exception);

            ApplyStoredCulture();
            WindowDarkMode.RegisterGlobalHook();

            log.Info($"Application started ({System.Reflection.Assembly.GetExecutingAssembly().GetName().Version})");
        }

        private static void ApplyStoredCulture()
        {
            // 保存済みカルチャがあれば復元、無ければ OS の UI 言語をベースに supported のみへフォールバック
            var stored = SatisfactorySaveEditor.Properties.Settings.Default.Culture;
            CultureInfo target;
            if (!string.IsNullOrWhiteSpace(stored))
            {
                try { target = CultureInfo.GetCultureInfo(stored); }
                catch (CultureNotFoundException) { target = ResolveDefaultCulture(); }
            }
            else
            {
                target = ResolveDefaultCulture();
            }
            LocalizationService.Instance.CurrentCulture = target;
        }

        private static CultureInfo ResolveDefaultCulture()
        {
            var ui = CultureInfo.CurrentUICulture;
            // 日本語環境なら ja、それ以外は en にする
            return ui.TwoLetterISOLanguageName == "ja"
                ? CultureInfo.GetCultureInfo("ja")
                : CultureInfo.GetCultureInfo("en");
        }
    }
}
