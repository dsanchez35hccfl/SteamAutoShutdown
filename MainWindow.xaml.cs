using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SteamAutoShutdown
{
    public partial class MainWindow : Window
    {
        private bool isMonitoring;
        private Storyboard? _pulseStoryboard;

        // 监测定时器
        private DispatcherTimer? _speedTimer;
        private bool _calculating;

        // ACF 进度缓存（定期刷新）
        private int _acfTickCounter;
        private const int AcfReadIntervalTicks = 5; // 每 5 秒读取一次 .acf
        private long _cachedDownloaded;
        private long _cachedTotal;
        private string _currentGameInfo = string.Empty;

        // 监测路径
        private string _monitoringPath = string.Empty;  // downloading 文件夹
        private string _steamappsPath = string.Empty;    // steamapps 文件夹（上级目录）

        // 完成检测（downloading 子目录清空）
        private int _finishConfirmCount;
        private const int FinishConfirmThreshold = 5;
        private bool _hasSeenDownload;

        // 迷你窗口
        private MiniWindow? _miniWindow;

        // 关机倒计时
        private DispatcherTimer? _countdownTimer;
        private int _shutdownRemaining;
        private const int ShutdownDelaySeconds = 60;

        // 系统托盘
        private Forms.NotifyIcon? _trayIcon;

        public MainWindow()
        {
            InitializeComponent();
            InitPulseAnimation();
            InitTrayIcon();
            SizeChanged += OnWindowSizeChanged;
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.HeightChanged && WindowState == WindowState.Normal)
            {
                var screen = SystemParameters.WorkArea;
                Left = (screen.Width - ActualWidth) / 2 + screen.Left;
                Top = (screen.Height - ActualHeight) / 2 + screen.Top;
            }
        }

        private void InitPulseAnimation()
        {
            var fadeOut = new DoubleAnimation(0.9, 0.15, TimeSpan.FromMilliseconds(900))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            _pulseStoryboard = new Storyboard();
            Storyboard.SetTarget(fadeOut, StatusGlow);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
            _pulseStoryboard.Children.Add(fadeOut);
        }

        private void InitTrayIcon()
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "favicon.ico");
            var icon = File.Exists(iconPath)
                ? new Icon(iconPath)
                : SystemIcons.Application;

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = icon,
                Text = "Steam 自动关机助手",
                Visible = false
            };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("还原主窗口", null, (_, _) => Dispatcher.Invoke(TrayRestore));
            menu.Items.Add("退出程序", null, (_, _) => Dispatcher.Invoke(TrayExit));

            _trayIcon.MouseDoubleClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                    Dispatcher.Invoke(TrayRestore);
            };

            _trayIcon.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Right)
                    menu.Show(Forms.Cursor.Position);
            };
        }

        // ═══════════ 最小化到托盘 ═══════════

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized)
            {
                ShowTrayIcon();
                Hide();
            }
        }

        private void TrayRestore()
        {
            CloseMiniWindow();
            HideTrayIcon();
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void TrayExit()
        {
            _speedTimer?.Stop();
            CloseMiniWindow();
            HideTrayIcon();
            System.Windows.Application.Current.Shutdown();
        }

        private void ShowTrayIcon()
        {
            if (_trayIcon != null)
                _trayIcon.Visible = true;
        }

        private void HideTrayIcon()
        {
            if (_trayIcon != null)
                _trayIcon.Visible = false;
        }

        private void SetStatus(string text, SolidColorBrush brush, bool pulsing)
        {
            TxtStatus.Text = text;
            TxtStatus.Foreground = brush;
            StatusDot.Fill = brush;
            StatusStripe.Background = brush;

            if (pulsing)
            {
                StatusGlow.Fill = brush;
                _pulseStoryboard?.Begin();
            }
            else
            {
                _pulseStoryboard?.Stop();
                StatusGlow.Opacity = 0;
            }
        }

        // ═══════════ 主窗口下载信息面板 ═══════════

        private void UpdateMainDownloadInfo(long downloaded, long total, string gameInfo)
        {
            if (!string.IsNullOrEmpty(gameInfo))
            {
                PanelDownloadInfo.Visibility = Visibility.Visible;
                TxtGameName.Text = $"🎮 {gameInfo}";

                if (total > 0)
                {
                    double percent = (double)downloaded / total * 100;
                    TxtProgress.Text = $"{FormatSize(downloaded)} / {FormatSize(total)}  ({percent:F1}%)";
                    SetMainProgress((double)downloaded / total);
                }
                else
                {
                    TxtProgress.Text = "等待进度信息...";
                    SetMainProgress(0);
                }
            }
            else
            {
                TxtGameName.Text = "";
                TxtProgress.Text = "等待进度信息...";
                SetMainProgress(0);
            }
        }

        private void SetMainProgress(double fraction)
        {
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            ((ScaleTransform)MainProgressFill.RenderTransform).ScaleX = fraction;
        }

        private void HideMainDownloadInfo()
        {
            PanelDownloadInfo.Visibility = Visibility.Collapsed;
            TxtGameName.Text = "";
            TxtProgress.Text = "";
            SetMainProgress(0);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "--";

            return bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
                < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
                _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
            };
        }

        // ═══════════ 浏览 ═══════════

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog
            {
                Title = "请选择 Steam 的 downloading 文件夹"
            };

            if (Directory.Exists(TxtPath.Text))
            {
                dialog.InitialDirectory = TxtPath.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                TxtPath.Text = dialog.FolderName;
            }
        }

        // ═══════════ 开始监测 ═══════════

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtPath.Text.Trim();

            if (!Directory.Exists(path))
            {
                System.Windows.MessageBox.Show("找不到该路径，请确认 Steam 下载目录是否正确！\n通常以 steamapps\\downloading 结尾。",
                    "路径错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _monitoringPath = path;
            _steamappsPath = Path.GetDirectoryName(path) ?? path;

            _acfTickCounter = AcfReadIntervalTicks - 1; // 首次 tick 立即读取 ACF
            _cachedDownloaded = 0;
            _cachedTotal = 0;
            _currentGameInfo = string.Empty;
            _finishConfirmCount = 0;
            _hasSeenDownload = false;
            isMonitoring = true;

            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled = true;
            BtnMini.IsEnabled = true;
            SetStatus("正在监测中...", (SolidColorBrush)FindResource("AccentGreenBrush"), pulsing: true);

            PanelDownloadInfo.Visibility = Visibility.Visible;
            TxtGameName.Text = "";
            TxtProgress.Text = "等待进度信息...";
            SetMainProgress(0);

            StartSpeedTimer();
        }

        // ═══════════ 停止监测 ═══════════

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopMonitoring();
            SetStatus("已手动停止", (SolidColorBrush)FindResource("TextSecondaryBrush"), pulsing: false);
            HideMainDownloadInfo();
            _miniWindow?.SetStopped();
        }

        private void StopMonitoring()
        {
            isMonitoring = false;
            _speedTimer?.Stop();
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            BtnMini.IsEnabled = false;
        }

        // ═══════════ 迷你悬浮窗 ═══════════

        private void BtnMini_Click(object sender, RoutedEventArgs e)
        {
            ShowMiniWindow();
        }

        private void ShowMiniWindow()
        {
            if (_miniWindow != null) return;

            _miniWindow = new MiniWindow
            {
                Left = SystemParameters.WorkArea.Right - 360,
                Top = SystemParameters.WorkArea.Top + 20
            };

            _miniWindow.RestoreRequested += OnMiniRestore;
            _miniWindow.CloseRequested += OnMiniClose;
            _miniWindow.CancelShutdownRequested += OnMiniCancelShutdown;
            _miniWindow.Show();

            ShowTrayIcon();
            Hide();
        }

        private void OnMiniRestore()
        {
            CloseMiniWindow();
            HideTrayIcon();
            Show();
            Activate();
        }

        private void OnMiniClose()
        {
            _speedTimer?.Stop();
            CloseMiniWindow();
            HideTrayIcon();
            _trayIcon?.Dispose();
            _trayIcon = null;
            System.Windows.Application.Current.Shutdown();
        }

        private void CloseMiniWindow()
        {
            if (_miniWindow == null) return;
            _miniWindow.RestoreRequested -= OnMiniRestore;
            _miniWindow.CloseRequested -= OnMiniClose;
            _miniWindow.CancelShutdownRequested -= OnMiniCancelShutdown;
            _miniWindow.Close();
            _miniWindow = null;
        }

        // ═══════════ 速度计算定时器 ═══════════

        private void StartSpeedTimer()
        {
            _speedTimer?.Stop();
            _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _speedTimer.Tick += SpeedTimer_Tick;
            _speedTimer.Start();
        }

        private async void SpeedTimer_Tick(object? sender, EventArgs e)
        {
            if (!isMonitoring || _calculating) return;
            _calculating = true;

            try
            {
                bool readAcf = (++_acfTickCounter >= AcfReadIntervalTicks);
                if (readAcf) _acfTickCounter = 0;

                string steamapps = _steamappsPath;
                string downloading = _monitoringPath;

                // I/O 放到后台线程
                var result = await Task.Run(() =>
                {
                    List<DownloadInfo>? acf = readAcf ? GetDownloadProgress(steamapps, downloading) : null;
                    bool hasActive = HasActiveDownloads(downloading);
                    return (acf, hasActive);
                });

                if (!isMonitoring) return;

                // ACF 游戏名 + 下载进度（每 N 秒刷新）
                if (result.acf != null)
                {
                    _cachedDownloaded = 0;
                    _cachedTotal = 0;
                    var names = new List<string>();
                    foreach (var p in result.acf)
                    {
                        _cachedDownloaded += p.Downloaded;
                        _cachedTotal += p.Total;
                        if (!string.IsNullOrEmpty(p.Name))
                            names.Add(p.Name);
                    }
                    _currentGameInfo = string.Join("、", names);
                }

                // 更新主窗口下载信息面板
                UpdateMainDownloadInfo(_cachedDownloaded, _cachedTotal, _currentGameInfo);

                // 更新迷你悬浮窗
                _miniWindow?.UpdateProgress(_cachedDownloaded, _cachedTotal, _currentGameInfo);

                // 完成检测：downloading 文件夹是否已清空子目录
                if (result.hasActive)
                {
                    _hasSeenDownload = true;
                    _finishConfirmCount = 0;
                }
                else if (_hasSeenDownload)
                {
                    _finishConfirmCount++;
                    if (_finishConfirmCount >= FinishConfirmThreshold)
                    {
                        OnDownloadFinished();
                    }
                }
            }
            finally
            {
                _calculating = false;
            }
        }

        private void OnDownloadFinished()
        {
            StopMonitoring();
            // 下载完成，进度条拉满
            PanelDownloadInfo.Visibility = Visibility.Visible;
            TxtGameName.Text = "✅ 下载完成";
            TxtProgress.Text = "";
            SetMainProgress(1);
            StartCountdown();
        }

        // ═══════════ 关机倒计时 ═══════════

        private void StartCountdown()
        {
            _shutdownRemaining = ShutdownDelaySeconds;
            UpdateCountdownUI();

            _countdownTimer?.Stop();
            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();
        }

        private void StopCountdown()
        {
            _countdownTimer?.Stop();
            _countdownTimer = null;
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            _shutdownRemaining--;
            if (_shutdownRemaining <= 0)
            {
                StopCountdown();
                SetStatus("系统正在关机...", (SolidColorBrush)FindResource("DangerRedBrush"), pulsing: false);
                _miniWindow?.UpdateCountdown(0);
                ExecuteShutdown();
                return;
            }
            UpdateCountdownUI();
        }

        private void UpdateCountdownUI()
        {
            var text = $"将在 {_shutdownRemaining} 秒后关机";
            SetStatus(text, (SolidColorBrush)FindResource("DangerRedBrush"), pulsing: false);
            _miniWindow?.UpdateCountdown(_shutdownRemaining);
        }

        // ═══════════ 紧急取消关机 ═══════════

        private void BtnCancelShutdown_Click(object sender, RoutedEventArgs e)
        {
            DoCancelShutdown();
        }

        private void OnMiniCancelShutdown()
        {
            DoCancelShutdown();
        }

        private void DoCancelShutdown()
        {
            bool wasActive = _countdownTimer?.IsEnabled == true;
            StopCountdown();

            if (wasActive)
            {
                SetStatus("关机已取消", (SolidColorBrush)FindResource("AccentBlueBrush"), pulsing: false);
                HideMainDownloadInfo();
                _miniWindow?.SetCancelled();
                System.Windows.MessageBox.Show("已成功取消自动关机！", "取消成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("当前没有正在进行的关机计划。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ═══════════ 读取 Steam .acf 清单文件 ═══════════

        private record struct DownloadInfo(string Name, long Downloaded, long Total);

        /// <summary>
        /// 遍历 downloading 文件夹的子目录（每个子目录名是 AppID），
        /// 然后从 steamapps 下对应的 appmanifest_{AppID}.acf 中提取下载进度。
        /// </summary>
        private static List<DownloadInfo> GetDownloadProgress(string steamappsPath, string downloadingPath)
        {
            var results = new List<DownloadInfo>();
            try
            {
                if (!Directory.Exists(downloadingPath)) return results;

                foreach (var dir in Directory.GetDirectories(downloadingPath))
                {
                    string appId = Path.GetFileName(dir);
                    string acfPath = Path.Combine(steamappsPath, $"appmanifest_{appId}.acf");
                    if (!File.Exists(acfPath)) continue;

                    try
                    {
                        var kv = ParseAcf(acfPath);
                        string name = kv.GetValueOrDefault("name") ?? appId;
                        long downloaded = long.TryParse(kv.GetValueOrDefault("BytesDownloaded"), out var d) ? d : 0;
                        long total = long.TryParse(kv.GetValueOrDefault("BytesToDownload"), out var t) ? t : 0;
                        results.Add(new DownloadInfo(name, downloaded, total));
                    }
                    catch { /* 跳过无法读取的 acf 文件 */ }
                }
            }
            catch { /* 目录枚举失败 */ }
            return results;
        }

        /// <summary>
        /// 简易解析 Valve VDF 格式的 .acf 文件，只读取第一层键值对。
        /// </summary>
        private static Dictionary<string, string> ParseAcf(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int depth = 0;

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed == "{") { depth++; continue; }
                if (trimmed == "}") { depth--; continue; }
                if (depth != 1) continue; // 只读 "AppState" 下的直接子键

                // 格式: "key"\t\t"value"
                int q1 = trimmed.IndexOf('"');
                if (q1 < 0) continue;
                int q2 = trimmed.IndexOf('"', q1 + 1);
                if (q2 < 0) continue;

                string key = trimmed[(q1 + 1)..q2];

                int q3 = trimmed.IndexOf('"', q2 + 1);
                if (q3 < 0) continue;
                int q4 = trimmed.IndexOf('"', q3 + 1);
                if (q4 < 0) continue;

                string value = trimmed[(q3 + 1)..q4];
                dict[key] = value;
            }

            return dict;
        }

        // ═══════════ downloading 文件夹检测 ═══════════

        private static bool HasActiveDownloads(string downloadingPath)
        {
            try
            {
                return Directory.Exists(downloadingPath)
                    && Directory.GetDirectories(downloadingPath).Length > 0;
            }
            catch { return false; }
        }

        // ═══════════ 触发关机 ═══════════

        private void ExecuteShutdown()
        {
            ProcessStartInfo psi = new ProcessStartInfo("shutdown", "/s /t 0")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi);
        }

        // ═══════════ 窗口关闭时清理 ═══════════

        protected override void OnClosed(EventArgs e)
        {
            _speedTimer?.Stop();
            StopCountdown();
            CloseMiniWindow();
            HideTrayIcon();
            _trayIcon?.Dispose();
            _trayIcon = null;
            base.OnClosed(e);
        }
    }
}
