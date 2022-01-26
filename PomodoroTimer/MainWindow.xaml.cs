using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PomodoroTimer
{
    public partial class MainWindow : Window
    {
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        private struct TimePeriod
        {
            public static implicit operator TimePeriod(TimeSpan TimeSpan_)
            {
                TimePeriod t = new TimePeriod();
                t.TimeSpan = TimeSpan_;
                return t;
            }

            public static TimePeriod FromString(string s)
            {
                try {
                    Match m = Regex.Match(s, @"^(?:(?:(?:(?<dd>[-+]?\d+):)?(?<hh>[-+]?\d+):)?(?<mm>[-+]?\d+):)?(?<ss>[-+]?\d+)$");
                    int dd = m.Groups["dd"].Success ? int.Parse(m.Groups["dd"].Value) : 0;
                    int hh = m.Groups["hh"].Success ? int.Parse(m.Groups["hh"].Value) : 0;
                    int mm = m.Groups["mm"].Success ? int.Parse(m.Groups["mm"].Value) : 0;
                    int ss = m.Groups["ss"].Success ? int.Parse(m.Groups["ss"].Value) : 0;
                    return (TimePeriod) TimeSpan.FromSeconds(((dd * 24 + hh) * 60 + mm) * 60 + ss);
                } catch (Exception) {
                    return (TimePeriod) TimeSpan.Zero;
                }
            }

            public override string ToString()
            {
                int ss = (int) Math.Ceiling(TimeSpan.TotalSeconds);

                string sign = ss < 0 ? "-" : "";
                ss = Math.Abs(ss);

                int hh = ss / (60 * 60);
                ss %= 60 * 60;

                int mm = ss / 60;
                ss %= 60;

                string s = sign;
                
                return String.Format(
                    "{0}{1}{2:00}:{3:00}",
                    sign,
                    hh > 0 ? hh.ToString() + ":" : "",
                    mm, ss);
            }

            public TimeSpan TimeSpan { get; set; }
        };

        public const int grid_margin = 8;
        public const int resize_border_size = 4;
        public const int min_window_size = 16 + (grid_margin * 2);

        private Point? drag_start_cursor_pos;
        private Rect? drag_start_window_rect;

        private DateTime? time_limit;
        private DateTime last_tick_time = DateTime.Now;
        private DateTime last_top_most_time = DateTime.Now;

        private IConfigurationRoot app_settings;

        public MainWindow()
        {
            InitializeComponent();
            
            app_settings = new ConfigurationBuilder()
                .SetBasePath(System.AppContext.BaseDirectory)
                .AddJsonFile(path:"appsettings.json", optional:true, reloadOnChange:true)
                .Build();

            ChangeToken.OnChange(
                () => app_settings.GetReloadToken(),
                () => Dispatcher.Invoke(LoadFromAppSettings));

            LoadFromAppSettings();

            ContextMenu = CreateContextMenu();

            DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        void LoadFromAppSettings()
        {
            var font_name = app_settings["FontFamily"];
            if (font_name != null) {
                try {
                    var font = new FontFamily(font_name);
                    TextBox_Time.FontFamily = font;
                    TextBlock_Time.FontFamily = font;
                } catch (Exception) {
                }
            }
        }

        TimePeriod[] LoadPresets()
        {
            var presets = new List<TimePeriod>();

            if (app_settings != null) {
                for (int i = 0; ; i++) {
                    var p = app_settings["Presets:" + i];
                    if (p == null) break;
                    presets.Add(TimePeriod.FromString(p));
                }
            }

            return presets.ToArray();
        }

        ContextMenu CreateContextMenu()
        {
            ContextMenu menu = new ContextMenu();

            void add_menu_item(object header, RoutedEventHandler handler)
            {
                MenuItem item = new MenuItem();
                item.Header = header;
                item.Click += handler;
                menu.Items.Add(item);
            }

            void add_menu_separator()
            {
                menu.Items.Add(new Separator());
            }

            if (time_limit.HasValue) {
                add_menu_item("Stop", MenuItem_StopTimer_Click);

                add_menu_separator();
            }

            foreach (var preset in LoadPresets())
            {
                add_menu_item(preset, MenuItem_StartTimer_Click);
            }

            add_menu_item("Custom", MenuItem_Custom_Click);

            add_menu_separator();

            add_menu_item("Exit", MenuItem_Exit_Click);

            return menu;
        }

        void SetTopMost()
        {
            SetWindowPos(new WindowInteropHelper(this).Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE);
        }

        void PlayTimeoutSound()
        {
            try {
                var path = Environment.ExpandEnvironmentVariables(app_settings["TimeoutSound"]);
                new SoundPlayer(path).Play();
            } catch (Exception) {
            }
        }

        void UpdateTimeText()
        {
            TimePeriod period = time_limit.HasValue ? time_limit.Value.Subtract(DateTime.Now) : TimeSpan.Zero;
            TextBlock_Time.Text = period.ToString();

            if (time_limit.HasValue) {
                if (period.TimeSpan.TotalMilliseconds < 0) {
                    if (((int) period.TimeSpan.TotalMilliseconds / 500) % 2 == 0) {
                        Panel.Fill = Brushes.Yellow;
                    } else {
                        Panel.Fill = Brushes.White;
                    }
                } else {
                    Panel.Fill = Brushes.White;
                }
            } else {
                Panel.Fill = Brushes.White;
            }
        }

        private void Window_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            ContextMenu = CreateContextMenu();
        }

        void MenuItem_StopTimer_Click(object sender, RoutedEventArgs e)
        {
            time_limit = default;
        }

        void MenuItem_StartTimer_Click(object sender, RoutedEventArgs e)
        {
            var item = (MenuItem) sender;
            var period = (TimePeriod) item.Header;
            time_limit = DateTime.Now.Add(period.TimeSpan);
        }

        void MenuItem_Custom_Click(object sender, RoutedEventArgs e)
        {
            TextBox_Time.Text = TextBlock_Time.Text;
            TextBlock_Time.Visibility = Visibility.Hidden;
            TextBox_Time.Visibility = Visibility.Visible;
            TextBox_Time.SelectAll();
            TextBox_Time.Focus();
        }

        void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TextBox_Time_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) {
                time_limit = DateTime.Now.Add(TimePeriod.FromString(TextBox_Time.Text).TimeSpan);

                TextBlock_Time.Visibility = Visibility.Visible;
                TextBox_Time.Text = "";
                TextBox_Time.Visibility = Visibility.Hidden;
            } else if (e.Key == Key.Escape) {
                TextBlock_Time.Visibility = Visibility.Visible;
                TextBox_Time.Text = "";
                TextBox_Time.Visibility = Visibility.Hidden;
            }
        }

        void Timer_Tick(object sender, EventArgs e)
        {
            UpdateTimeText();

            if (time_limit.HasValue && last_tick_time <= time_limit.Value && DateTime.Now >= time_limit.Value) {
                PlayTimeoutSound();
            }
            last_tick_time = DateTime.Now;

            if (DateTime.Now.Subtract(last_top_most_time) > TimeSpan.FromSeconds(1)) {
                SetTopMost();
                last_top_most_time = DateTime.Now;
            }
        }

        [Flags]
        private enum ResizingFlag
        {
            None        = 0,
            LeftEdge    = 1 << 0,
            TopEdge     = 1 << 1,
            RightEdge   = 1 << 2,
            BottomEdge  = 1 << 3,
        }

        private ResizingFlag GetResizingFlags(Rect window_rect, Point cursor_pos)
        {
            ResizingFlag flags = ResizingFlag.None;

            if (cursor_pos.X <= window_rect.Left + grid_margin + resize_border_size) flags |= ResizingFlag.LeftEdge;
            if (cursor_pos.Y <= window_rect.Top + grid_margin + resize_border_size) flags |= ResizingFlag.TopEdge;
            if (cursor_pos.X >= window_rect.Right - grid_margin - resize_border_size) flags |= ResizingFlag.RightEdge;
            if (cursor_pos.Y >= window_rect.Bottom - grid_margin - resize_border_size) flags |= ResizingFlag.BottomEdge;

            return flags;
        }

        private Rect GetWindowRect()
        {
            return new Rect(Left, Top, Width, Height);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            drag_start_cursor_pos = PointToScreen(e.GetPosition(this));
            drag_start_window_rect = GetWindowRect();

            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            Point cursor_pos = PointToScreen(e.GetPosition(this));

            {
                var flags = drag_start_cursor_pos.HasValue && drag_start_window_rect.HasValue
                    ? GetResizingFlags(drag_start_window_rect.Value, drag_start_cursor_pos.Value)
                    : GetResizingFlags(GetWindowRect(), cursor_pos);

                bool W = flags.HasFlag(ResizingFlag.LeftEdge);
                bool E = flags.HasFlag(ResizingFlag.RightEdge);
                bool N = flags.HasFlag(ResizingFlag.TopEdge);
                bool S = flags.HasFlag(ResizingFlag.BottomEdge);
                if ((N && W) || (S && E)) Cursor = Cursors.SizeNWSE;
                else if ((N && E) || (S && W)) Cursor = Cursors.SizeNESW;
                else if (N || S) Cursor = Cursors.SizeNS;
                else if (W || E) Cursor = Cursors.SizeWE;
                else Cursor = null;
            }

            if (drag_start_cursor_pos.HasValue && drag_start_window_rect.HasValue) {
                var delta = cursor_pos - drag_start_cursor_pos.Value;
                var window_rect = drag_start_window_rect.Value;

                var flags = GetResizingFlags(window_rect, drag_start_cursor_pos.Value);
                if (flags == ResizingFlag.None) {
                    Left = window_rect.Left + delta.X;
                    Top = window_rect.Top + delta.Y;
                } else {
                    if (flags.HasFlag(ResizingFlag.LeftEdge)) {
                        Left = window_rect.Left + delta.X;
                        Width = Math.Max(window_rect.Width - delta.X, min_window_size);
                    }
                    if (flags.HasFlag(ResizingFlag.TopEdge)) {
                        Top = window_rect.Top + delta.Y;
                        Height = Math.Max(window_rect.Height - delta.Y, min_window_size);
                    }
                    if (flags.HasFlag(ResizingFlag.RightEdge)) {
                        Width = Math.Max(window_rect.Width + delta.X, min_window_size);
                    }
                    if (flags.HasFlag(ResizingFlag.BottomEdge)) {
                        Height = Math.Max(window_rect.Height + delta.Y, min_window_size);
                    }
                }
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            drag_start_cursor_pos = default;
            drag_start_window_rect = default;

            ReleaseMouseCapture();
        }
    }
}
