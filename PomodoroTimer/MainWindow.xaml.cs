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
        public const int grid_margin = 8;
        public const int resize_border_size = 4;
        public const int min_window_size = 16 + (grid_margin * 2);

        private struct Preset
        {
            public string Text;
            public TimeSpan Duration;
            public Brush ForegroundBrush;
            public Brush BackgroundBrush;
            public Brush TimeoutForegroundBrush;
            public Brush TimeoutBackgroundBrush;

            public override string ToString()
            {
                return Text;
            }
        };

        private class TimeText
        {
            public string Text { get; set; }
            public TimeSpan TimeSpan { get; set; }

            public TimeText(TimeSpan span)
            {
                Text = TimeSpanToText(span);
                TimeSpan = span;
            }

            public TimeText(string text)
                : this(TextToTimeSpan(text))
            {
            }

            private static TimeSpan TextToTimeSpan(string text)
            {
                {
                    var m = Regex.Match(text, @"^(?:(?<dd>\d+)d)?(?:(?<hh>\d+)h)?(?:(?<mm>\d+)*m)?(?:(?<ss>\d+)*s)?$",　RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        int dd = m.Groups["dd"].Success ? int.Parse(m.Groups["dd"].Value) : 0;
                        int hh = m.Groups["hh"].Success ? int.Parse(m.Groups["hh"].Value) : 0;
                        int mm = m.Groups["mm"].Success ? int.Parse(m.Groups["mm"].Value) : 0;
                        int ss = m.Groups["ss"].Success ? int.Parse(m.Groups["ss"].Value) : 0;
                        return TimeSpan.FromSeconds(((dd * 24 + hh) * 60 + mm) * 60 + ss);
                    }
                }
                {
                    var m = Regex.Match(text, @"^(?:(?:(?:(?<dd>[^:]+):)?(?<hh>[^:]+):)?(?<mm>[^:]+):)?(?<ss>[^:]+)$");
                    int dd = m.Groups["dd"].Success ? int.Parse(m.Groups["dd"].Value) : 0;
                    int hh = m.Groups["hh"].Success ? int.Parse(m.Groups["hh"].Value) : 0;
                    int mm = m.Groups["mm"].Success ? int.Parse(m.Groups["mm"].Value) : 0;
                    int ss = m.Groups["ss"].Success ? int.Parse(m.Groups["ss"].Value) : 0;
                    return TimeSpan.FromSeconds(((dd * 24 + hh) * 60 + mm) * 60 + ss);
                }
            }

            private static string TimeSpanToText(TimeSpan span)
            {
                int ss = (int) Math.Ceiling(span.TotalSeconds);

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
        };

        private IConfigurationRoot app_settings;
        public Brush CustomForegroundBrush;
        public Brush CustomBackgroundBrush;
        public Brush CustomTimeoutForegroundBrush;
        public Brush CustomTimeoutBackgroundBrush;
        private DateTime? time_limit;
        Preset? preset;

        private DateTime last_tick_time = DateTime.Now;
        private DateTime last_top_most_time = DateTime.Now;

        private Point? drag_start_cursor_pos;
        private Rect? drag_start_window_rect;

        public MainWindow()
        {
            InitializeComponent();
            
            app_settings = new ConfigurationBuilder()
                .SetBasePath(System.AppContext.BaseDirectory)
                .AddJsonFile(path:"appsettings.json", optional:true, reloadOnChange:true)
                .Build();

            ChangeToken.OnChange(
                () => app_settings.GetReloadToken(),
                () => Dispatcher.Invoke(ReloadFont));

            ReloadFont();

            ContextMenu = CreateContextMenu();

            DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        void ReloadFont()
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

        Preset[] LoadPresets()
        {
            var presets = new List<Preset>();

            CustomForegroundBrush = new SolidColorBrush((Color) ColorConverter.ConvertFromString(app_settings["CustomForegroundColor"]));
            CustomBackgroundBrush = new SolidColorBrush((Color) ColorConverter.ConvertFromString(app_settings["CustomBackgroundColor"]));
            CustomTimeoutForegroundBrush = new SolidColorBrush((Color) ColorConverter.ConvertFromString(app_settings["CustomTimeoutForegroundColor"]));
            CustomTimeoutBackgroundBrush = new SolidColorBrush((Color) ColorConverter.ConvertFromString(app_settings["CustomTimeoutBackgroundColor"]));

            for (int i = 0; ; i++) {
                var text = app_settings["Presets:" + i + ":Text"];
                var duration = app_settings["Presets:" + i + ":Duration"];
                var text_color = app_settings["Presets:" + i + ":ForegroundColor"];
                var background_color = app_settings["Presets:" + i + ":BackgroundColor"];
                var timeout_text_color = app_settings["Presets:" + i + ":TimeoutForegroundColor"];
                var timeout_background_color = app_settings["Presets:" + i + ":TimeoutBackgroundColor"];

                if (text == null || duration == null || text_color == null || background_color == null) break;

                Preset p = new Preset();
                p.Text = text;
                p.Duration = new TimeText(duration).TimeSpan;
                p.ForegroundBrush = new SolidColorBrush((Color) ColorConverter.ConvertFromString(text_color));
                p.BackgroundBrush = new SolidColorBrush((Color) ColorConverter.ConvertFromString(background_color));
                p.TimeoutForegroundBrush = new SolidColorBrush((Color) ColorConverter.ConvertFromString(timeout_text_color));
                p.TimeoutBackgroundBrush = new SolidColorBrush((Color) ColorConverter.ConvertFromString(timeout_background_color));
                presets.Add(p);
            }

            return presets.ToArray();
        }

        Image CreateColorSampleImage(Brush brush)
        {
            GeometryGroup geometry = new GeometryGroup();
            geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, 100, 100), 8, 8));

            var geometry_drawing = new GeometryDrawing();
            geometry_drawing.Geometry = geometry;
            geometry_drawing.Brush = brush;
            geometry_drawing.Pen = new Pen(Brushes.Gray, 1);

            var drawing_image = new DrawingImage(geometry_drawing);
            drawing_image.Freeze();

            Image image = new Image();
            image.Source = drawing_image;
            image.HorizontalAlignment = HorizontalAlignment.Center;

            return image;
        }

        ContextMenu CreateContextMenu()
        {
            ContextMenu menu = new ContextMenu();

            void add_menu_item(object header, Brush brush, RoutedEventHandler handler)
            {
                MenuItem item = new MenuItem();
                item.Header = header;
                if (brush != null) item.Icon = CreateColorSampleImage(brush);
                item.Click += handler;
                menu.Items.Add(item);
            }

            void add_menu_separator()
            {
                menu.Items.Add(new Separator());
            }

            if (time_limit.HasValue) {
                add_menu_item("Stop", null, MenuItem_StopTimer_Click);

                add_menu_separator();
            }

            foreach (var preset in LoadPresets()) {
                add_menu_item(preset, preset.BackgroundBrush, MenuItem_StartTimer_Click);
            }

            add_menu_item("Custom", null, MenuItem_Custom_Click);

            add_menu_separator();

            add_menu_item("Exit", null, MenuItem_Exit_Click);

            return menu;
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

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

        void StartTimer(TimeSpan span)
        {
            time_limit = DateTime.Now.Add(span);

            UpdateTimeText();
        }

        void StopTimer()
        {
            time_limit = default;
            preset = default;

            UpdateTimeText();
        }

        void UpdateTimeText()
        {
            TimeSpan span = time_limit.HasValue ? time_limit.Value.Subtract(DateTime.Now) : TimeSpan.Zero;
            TextBlock_Time.Text = new TimeText(span).Text;

            bool is_timeout()
            {
                if (time_limit.HasValue) {
                    if (span.TotalMilliseconds < 0) {
                        if (((int) span.TotalMilliseconds / 500) % 2 == 0) {
                            return true;
                        }
                    }
                }
                return false;
            }

            if (is_timeout()) {
                if (preset.HasValue) {
                    TextBlock_Time.Foreground = preset.Value.TimeoutForegroundBrush;
                    Panel.Fill = preset.Value.TimeoutBackgroundBrush;
                } else {
                    TextBlock_Time.Foreground = CustomTimeoutForegroundBrush;
                    Panel.Fill = CustomTimeoutBackgroundBrush;
                }
            } else {
                if (preset.HasValue) {
                    TextBlock_Time.Foreground = preset.Value.ForegroundBrush;
                    Panel.Fill = preset.Value.BackgroundBrush;
                } else {
                    TextBlock_Time.Foreground = CustomForegroundBrush;
                    Panel.Fill = CustomBackgroundBrush;
                }
            }
        }

        private void Window_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            ContextMenu = CreateContextMenu();
        }

        void MenuItem_StopTimer_Click(object sender, RoutedEventArgs e)
        {
            StopTimer();
        }

        void MenuItem_StartTimer_Click(object sender, RoutedEventArgs e)
        {
            preset = (Preset) ((MenuItem) sender).Header;

            StartTimer(preset.Value.Duration);
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
            if (e.Key == Key.Enter || e.Key == Key.Escape) {
                if (e.Key == Key.Enter) {
                    preset = default;
                    try {
                        StartTimer(new TimeText(TextBox_Time.Text).TimeSpan);
                    } catch (Exception) {
                        StartTimer(TimeSpan.Zero);
                    }
                }

                TextBlock_Time.Visibility = Visibility.Visible;
                TextBox_Time.Visibility = Visibility.Hidden;
                TextBox_Time.Text = "";
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
