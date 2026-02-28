using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace SteamAutoShutdown
{
    public partial class MiniWindow : Window
    {
        public event Action? RestoreRequested;
        public event Action? CloseRequested;
        public event Action? CancelShutdownRequested;

        public MiniWindow()
        {
            InitializeComponent();
        }

        public void UpdateProgress(long downloaded, long total, string gameInfo)
        {
            TxtSpeed.Text = !string.IsNullOrEmpty(gameInfo) ? gameInfo : "监测中...";

            if (total > 0)
            {
                double percent = (double)downloaded / total * 100;
                TxtSize.Text = $"{FormatSize(downloaded)} / {FormatSize(total)} ({percent:F1}%)";
                SetProgress((double)downloaded / total);
            }
            else
            {
                TxtSize.Text = "等待进度信息...";
                SetProgress(0);
            }

            if (!string.IsNullOrEmpty(gameInfo))
                TxtMiniStatus.Text = "下载中";
        }

        public void SetFinished()
        {
            TxtSpeed.Text = "下载完成";
            TxtSpeed.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0x3B, 0x3B));
            MiniDot.Fill = new SolidColorBrush(Color.FromRgb(0xCD, 0x3B, 0x3B));
            TxtMiniStatus.Text = "即将关机";
            SetProgress(1);
        }

        public void UpdateCountdown(int secondsRemaining)
        {
            var red = new SolidColorBrush(Color.FromRgb(0xCD, 0x3B, 0x3B));
            MiniDot.Fill = red;
            BtnCancelShutdown.Visibility = Visibility.Visible;
            SetProgress(1);

            if (secondsRemaining <= 0)
            {
                TxtSpeed.Text = "正在关机...";
                TxtSpeed.Foreground = red;
                TxtMiniStatus.Text = "关机中";
                TxtSize.Text = "";
                BtnCancelShutdown.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtSpeed.Text = $"{secondsRemaining} 秒";
                TxtSpeed.Foreground = red;
                TxtMiniStatus.Text = "即将关机";
                TxtSize.Text = "下载完成，倒计时关机";
            }
        }

        public void SetCancelled()
        {
            var blue = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
            TxtSpeed.Text = "已取消";
            TxtSpeed.Foreground = blue;
            MiniDot.Fill = blue;
            TxtMiniStatus.Text = "关机已取消";
            TxtSize.Text = "";
            BtnCancelShutdown.Visibility = Visibility.Collapsed;
            SetProgress(0);
        }

        public void SetStopped()
        {
            TxtSpeed.Text = "已停止";
            TxtSpeed.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0));
            MiniDot.Fill = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0));
            TxtMiniStatus.Text = "已停止";
            BtnCancelShutdown.Visibility = Visibility.Collapsed;
            SetProgress(0);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            RestoreRequested?.Invoke();
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            RestoreRequested?.Invoke();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke();
        }

        private void MenuRestore_Click(object sender, RoutedEventArgs e)
        {
            RestoreRequested?.Invoke();
        }

        private void MenuClose_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke();
        }

        private void BtnCancelShutdown_Click(object sender, RoutedEventArgs e)
        {
            CancelShutdownRequested?.Invoke();
        }

        private void SetProgress(double fraction)
        {
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            ((ScaleTransform)ProgressFill.RenderTransform).ScaleX = fraction;
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
    }
}
