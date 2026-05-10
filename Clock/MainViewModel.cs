using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Clock
{
    public class TargetWindowInfo : IEquatable<TargetWindowInfo>
    {
        public string Title { get; set; }
        public string ClassName { get; set; }
        public string ProcessName { get; set; }
        public string ExecutablePath { get; set; }

        public TargetWindowInfo() { }

        public TargetWindowInfo(string title, string className, string processName, string executablePath)
        {
            Title = title;
            ClassName = className;
            ProcessName = processName;
            ExecutablePath = executablePath;
        }

        public override string ToString() => string.IsNullOrWhiteSpace(Title) ? $"{ProcessName} ({ClassName})" : Title;
        public bool Equals(TargetWindowInfo? other)
        {
            if (other is null) return false;
            return ClassName == other.ClassName && ProcessName == other.ProcessName && ExecutablePath == other.ExecutablePath;
        }
        public override bool Equals(object obj) => Equals(obj as TargetWindowInfo);
        public override int GetHashCode() => HashCode.Combine(ClassName, ProcessName, ExecutablePath);
    }

    public partial class MainViewModel : ObservableObject
    {
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private DispatcherTimer _mainTimer;
        private TimeSpan _currentSessionTime = TimeSpan.Zero;
        private TimeSpan _totalTime = TimeSpan.Zero;
        private DateTime? _targetDate = null;
        private int _cycleCounter = 0;

        [ObservableProperty]
        private string _mainTimerText = "00 : 00";

        [ObservableProperty]
        private string _totalTimerText = "总计时间: 0h 0m | 循环计数: 0";

        [ObservableProperty]
        private string _dayCountdownText = "目标日倒计时: -- 天";

        [ObservableProperty]
        private bool _isSettingsVisible = false;

        [ObservableProperty]
        private int _cycleMinutes = 60;

        [ObservableProperty]
        private TargetWindowInfo _selectedWindowInComboBox;

        [ObservableProperty]
        private TargetWindowInfo _selectedWindowInListBox;

        [ObservableProperty]
        private string _addFocusedWindowText = "添加当前 Focus 窗口(倒数5s)";

        [ObservableProperty]
        private bool _isAddingFocusedWindow = false;

        [ObservableProperty]
        private string _slackingTimerText = "00 : 00";

        [ObservableProperty]
        private int _slackingNotificationInterval = 15;

        private TimeSpan _currentSlackingTime = TimeSpan.Zero;
        private int _lastNotifiedSlackingMinutes = 0;

        [ObservableProperty]
        private bool _isRestOverlayVisible = false;

        [ObservableProperty]
        private int _restInputMinutes = 5;

        [ObservableProperty]
        private string _totalRestText = "今天你休息了0分钟";

        [ObservableProperty]
        private string _restButtonText = "休息";

        private bool _isResting = false;
        private TimeSpan _restTimeRemaining = TimeSpan.Zero;
        private TimeSpan _totalRestTime = TimeSpan.Zero;

        [ObservableProperty]
        private SolidColorBrush _timingStatusBrush = new SolidColorBrush(Colors.Gold);

        public ObservableCollection<TargetWindowInfo> TargetWindows { get; } = new ObservableCollection<TargetWindowInfo>();
        public ObservableCollection<TargetWindowInfo> AvailableWindows { get; } = new ObservableCollection<TargetWindowInfo>();

        public MainViewModel()
        {
            LoadData();
            UpdateTimersUI();
            UpdateDayCountdown();

            _mainTimer = new DispatcherTimer();
            _mainTimer.Interval = TimeSpan.FromSeconds(1);
            _mainTimer.Tick += MainTimer_Tick;
            _mainTimer.Start();
        }

        private void MainTimer_Tick(object sender, EventArgs e)
        {
            UpdateDayCountdown();

            if (_isResting)
            {
                _restTimeRemaining = _restTimeRemaining.Subtract(TimeSpan.FromSeconds(1));
                if (_restTimeRemaining.TotalSeconds <= 0)
                {
                    EndRest(auto: true);
                }
                else
                {
                    SlackingTimerText = $"还有{Math.Ceiling(_restTimeRemaining.TotalMinutes)}分钟休息结束，好好休息吧";
                }
                TimingStatusBrush = new SolidColorBrush(Colors.Gray);
                return;
            }

            bool isTiming = IsUserActive() && IsTargetWindowFocused();
            TimingStatusBrush = isTiming ? new SolidColorBrush(Colors.LimeGreen) : new SolidColorBrush(Colors.Gold);

            if (isTiming)
            {
                _currentSessionTime = _currentSessionTime.Add(TimeSpan.FromSeconds(1));

                if (_currentSlackingTime.TotalSeconds > 0)
                {
                    _currentSlackingTime = _currentSlackingTime.Subtract(TimeSpan.FromSeconds(1));
                    int currentMins = (int)_currentSlackingTime.TotalMinutes;
                    if (currentMins < _lastNotifiedSlackingMinutes)
                    {
                        _lastNotifiedSlackingMinutes = currentMins;
                    }
                }

                if (_currentSessionTime.TotalMinutes >= CycleMinutes)
                {
                    _totalTime = _totalTime.Add(_currentSessionTime);
                    _currentSessionTime = TimeSpan.Zero;
                    _cycleCounter++;
                }
                UpdateTimersUI();
            }
            else
            {
                _currentSlackingTime = _currentSlackingTime.Add(TimeSpan.FromSeconds(1));
                int currentSlackingMins = (int)_currentSlackingTime.TotalMinutes;

                if (SlackingNotificationInterval > 0 && 
                    currentSlackingMins > 0 && 
                    currentSlackingMins % SlackingNotificationInterval == 0 && 
                    currentSlackingMins != _lastNotifiedSlackingMinutes)
                {
                    _lastNotifiedSlackingMinutes = currentSlackingMins;
                    new ToastContentBuilder()
                        .AddText($"你已经摸鱼了{currentSlackingMins}分钟了！")
                        .Show();
                }
            }

            SlackingTimerText = $"{(int)_currentSlackingTime.TotalMinutes:D2} : {_currentSlackingTime.Seconds:D2}";
        }

        private bool IsUserActive()
        {
            LASTINPUTINFO lastInput = new LASTINPUTINFO();
            lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);
            if (GetLastInputInfo(ref lastInput))
            {
                uint idleTime = (uint)Environment.TickCount - lastInput.dwTime;
                return idleTime < 10 * 60 * 1000;
            }
            return true;
        }

        private TargetWindowInfo GetWindowInfo(IntPtr hWnd)
        {
            var sbTitle = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sbTitle, 256);

            var sbClass = new System.Text.StringBuilder(256);
            GetClassName(hWnd, sbClass, 256);

            GetWindowThreadProcessId(hWnd, out uint pid);
            string processName = "";
            string executablePath = "";
            try
            {
                var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
                executablePath = proc.MainModule?.FileName ?? "";
            }
            catch { }

            return new TargetWindowInfo(
                sbTitle.ToString(),
                sbClass.ToString(),
                processName,
                executablePath
            );
        }

        private bool IsTargetWindowFocused()
        {
            if (TargetWindows.Count == 0) return false;
            IntPtr handle = GetForegroundWindow();
            var info = GetWindowInfo(handle);

            return TargetWindows.Any(w => w.Equals(info) && (!string.IsNullOrEmpty(w.ClassName) || !string.IsNullOrEmpty(w.ProcessName) || !string.IsNullOrEmpty(w.ExecutablePath)));
        }

        private void UpdateTimersUI()
        {
            MainTimerText = $"{(int)_currentSessionTime.TotalHours:D2} : {_currentSessionTime.Minutes:D2}";
            TotalTimerText = $"总计时间: {(int)_totalTime.TotalHours}h {_totalTime.Minutes}m | 循环计数: {_cycleCounter}";
        }

        private void UpdateDayCountdown()
        {
            if (_targetDate.HasValue)
            {
                var diff = _targetDate.Value.Date - DateTime.Now.Date;
                DayCountdownText = diff.TotalDays > 0 ? $"目标日倒计时: {diff.TotalDays} 天" : "目标日期已到";
            }
        }

    [RelayCommand]
        private void OpenSettings()
        {
            AvailableWindows.Clear();
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    const uint GW_OWNER = 4;
                    if (GetWindow(hWnd, GW_OWNER) == IntPtr.Zero)
                    {
                        var info = GetWindowInfo(hWnd);
                        if (!string.IsNullOrEmpty(info.Title))
                        {
                            AvailableWindows.Add(info);
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            IsSettingsVisible = true;
        }

        [RelayCommand]
        private void CloseSettings()
        {
            IsSettingsVisible = false;
        }

        [RelayCommand]
        private void AddWindow()
        {
            if (SelectedWindowInComboBox != null && !TargetWindows.Contains(SelectedWindowInComboBox))
            {
                TargetWindows.Add(SelectedWindowInComboBox);
            }
        }

        [RelayCommand]
        private void DeleteWindow()
        {
            if (SelectedWindowInListBox != null)
            {
                TargetWindows.Remove(SelectedWindowInListBox);
            }
        }

        [RelayCommand]
        private async Task AddFocusedWindowAsync()
        {
            if (IsAddingFocusedWindow) return;
            IsAddingFocusedWindow = true;

            for (int i = 5; i > 0; i--)
            {
                AddFocusedWindowText = $"请 Focus 窗口 ({i}s)";
                await Task.Delay(1000);
            }

            IntPtr handle = GetForegroundWindow();
            if (handle != IntPtr.Zero)
            {
                var info = GetWindowInfo(handle);
                if (!string.IsNullOrEmpty(info.ClassName) || !string.IsNullOrEmpty(info.ProcessName))
                {
                    if (!TargetWindows.Contains(info))
                    {
                        TargetWindows.Add(info);
                    }
                }
            }

            AddFocusedWindowText = "添加当前 Focus 窗口(倒数5s)";
            IsAddingFocusedWindow = false;
        }

        [RelayCommand]
        private void SetTargetDate()
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox("请输入目标日期 (yyyy:MM:dd or yyyy-MM-dd)", "目标日期设置", "");
            if (DateTime.TryParse(input.Replace(':', '-'), out DateTime dt) && dt > DateTime.Now)
            {
                _targetDate = dt;
                UpdateDayCountdown();
            }
            else
            {
                MessageBox.Show("日期无效或小于当前时间！");
            }
        }

        [RelayCommand]
        private void ToggleRest()
        {
            if (_isResting)
            {
                var res = MessageBox.Show("是否结束休息？", "提示", MessageBoxButton.YesNo);
                if (res == MessageBoxResult.Yes)
                {
                    EndRest(auto: false);
                }
            }
            else
            {
                TotalRestText = $"今天你休息了{(int)_totalRestTime.TotalMinutes}分钟";
                IsRestOverlayVisible = true;
            }
        }

        [RelayCommand]
        private void StartRest()
        {
            _isResting = true;
            _restTimeRemaining = TimeSpan.FromMinutes(RestInputMinutes);
            _totalRestTime = _totalRestTime.Add(_restTimeRemaining);
            IsRestOverlayVisible = false;
            RestButtonText = "结束休息";
            TimingStatusBrush = new SolidColorBrush(Colors.Gray);
            SlackingTimerText = $"还有{Math.Ceiling(_restTimeRemaining.TotalMinutes)}分钟休息结束，好好休息吧";
        }

        [RelayCommand]
        private void CloseRestOverlay()
        {
            IsRestOverlayVisible = false;
        }

        private void EndRest(bool auto)
        {
            _isResting = false;
            RestButtonText = "休息";
            if (auto)
            {
                new ToastContentBuilder()
                    .AddText("休息结束，继续工作吧")
                    .Show();
            }
            SlackingTimerText = $"{(int)_currentSlackingTime.TotalMinutes:D2} : {_currentSlackingTime.Seconds:D2}";
        }

        public void SaveData()
        {
            var data = new AppData
            {
                TotalTime = _totalTime.Add(_currentSessionTime),
                TargetWindows = TargetWindows.ToArray(),
                TargetDate = _targetDate,
                CycleCounter = _cycleCounter,
                CycleMinutes = CycleMinutes,
                SlackingNotificationInterval = SlackingNotificationInterval,
                TotalRestTime = _totalRestTime
            };
            File.WriteAllText("save.sv", JsonSerializer.Serialize(data));
        }

        private void LoadData()
        {
            if (File.Exists("save.sv"))
            {
                try
                {
                    var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText("save.sv"));
                    if (data != null)
                    {
                        _totalTime = data.TotalTime;
                        _targetDate = data.TargetDate;
                        _cycleCounter = data.CycleCounter;
                        CycleMinutes = data.CycleMinutes > 0 ? data.CycleMinutes : 60;
                        SlackingNotificationInterval = data.SlackingNotificationInterval > 0 ? data.SlackingNotificationInterval : 15;
                        _totalRestTime = data.TotalRestTime;

                        if (data.TargetWindows != null)
                        {
                            foreach (var w in data.TargetWindows) TargetWindows.Add(w);
                        }
                    }
                }
                catch { }
            }
        }

        public bool CanCloseApp()
        {
            if (_currentSessionTime.TotalHours >= 1)
            {
                var res = MessageBox.Show($"已经计时了{(int)_currentSessionTime.TotalHours}h{_currentSessionTime.Minutes}m，确定要退出吗？", "提示", MessageBoxButton.YesNo);
                return res == MessageBoxResult.Yes;
            }
            return true;
        }
    }

    public class AppData
    {
        public TimeSpan TotalTime { get; set; }
        public TargetWindowInfo[] TargetWindows { get; set; }
        public DateTime? TargetDate { get; set; }
        public int CycleCounter { get; set; }
        public int CycleMinutes { get; set; }
        public int SlackingNotificationInterval { get; set; }
        public TimeSpan TotalRestTime { get; set; }
    }
}