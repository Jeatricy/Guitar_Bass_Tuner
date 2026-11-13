using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GuitarBassTuner.Audio;
using GuitarBassTuner.Controls;

namespace GuitarBassTuner;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer timer;
    private readonly AudioInput input;
    private static readonly InputOption SystemDefault = new("系统默认输入", null);
    private static readonly InputOption Rocksmith = new("Rocksmith 输入线", null);
    private readonly ObservableCollection<InputOption> inputOptions = [SystemDefault, Rocksmith];
    private InputOption inputOption = SystemDefault;
    private CaptureEndpoints? displayedEndpoints;
    private InputSelection? inputSelection;
    private readonly List<ToggleButton> stringButtons = [];
    private Instrument instrument;
    private Tuning tuning = Tuning.For(Instrument.Guitar)[0];
    private bool automatic = true;
    private int? selectedString;
    private int lastSelectedString;
    private long lastPlottedTimestamp;
    private long lastAcceptedTimestamp;
    private double? visibleFrequency;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureInstrument();
        StateChanged += Window_StateChanged;
        input = new AudioInput();
        InputPicker.ItemsSource = inputOptions;
        timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(23)
        };
        timer.Tick += Refresh;
        timer.Start();
    }

    private void Instrument_Click(object sender, RoutedEventArgs e)
    {
        Instrument next = sender == BassButton ? Instrument.Bass : Instrument.Guitar;
        if (instrument == next)
            return;

        instrument = next;
        lastSelectedString = 0;
        ConfigureInstrument();
    }

    private void ConfigureInstrument()
    {
        IReadOnlyList<Tuning> presets = Tuning.For(instrument);
        tuning = presets[0];
        PresetPicker.ItemsSource = presets;
        PresetPicker.SelectedIndex = 0;
        GuitarHeadstocks.Visibility = instrument == Instrument.Guitar ? Visibility.Visible : Visibility.Collapsed;
        BassHeadstock.Visibility = instrument == Instrument.Bass ? Visibility.Visible : Visibility.Collapsed;
        UpdateHeadstock();
        RebuildStringButtons();
        ChangeTarget(automatic ? null : lastSelectedString);
        Graph.Reset();
    }

    private void Headstock_Click(object sender, RoutedEventArgs e) => UpdateHeadstock();

    private void UpdateHeadstock()
    {
        Neck.SetHeadstock(instrument == Instrument.Bass ? Headstock.InlineBass
            : InlineHeadstock.IsChecked == true ? Headstock.InlineGuitar : Headstock.SplitGuitar);
    }

    private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetPicker.SelectedItem is not Tuning next || ReferenceEquals(tuning, next))
            return;

        tuning = next;
        RebuildStringButtons();
        int? target = automatic && visibleFrequency is double frequency ? tuning.NearestString(frequency) : selectedString;
        ChangeTarget(target);
        Graph.Reset();
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        bool next = sender == AutoButton;
        if (automatic == next)
            return;

        automatic = next;
        if (!automatic)
            ChangeTarget(selectedString ?? lastSelectedString);
        else
            ChangeTarget(visibleFrequency is double frequency ? tuning.NearestString(frequency) : null);
        Graph.Reset();
    }

    private void Neck_StringSelected(object? sender, int index) => LockString(index);

    private void LockString(int index)
    {
        if (automatic || selectedString != index)
            Graph.Reset();
        automatic = false;
        ManualButton.IsChecked = true;
        ChangeTarget(index);
    }

    private void RebuildStringButtons()
    {
        StringButtons.Children.Clear();
        stringButtons.Clear();
        StringButtons.Columns = tuning.Notes.Length;
        for (int index = 0; index < tuning.Notes.Length; index++)
        {
            int stringIndex = index;
            int stringNumber = tuning.Notes.Length - index;
            string note = tuning.NoteName(tuning.Notes[index]);
            var number = new TextBlock
            {
                Text = $"{stringNumber} 弦",
                FontSize = 10,
                Foreground = DrawingStyle.Muted,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var name = new TextBlock
            {
                Text = note,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(number);
            label.Children.Add(name);
            var button = new ToggleButton
            {
                Style = (Style)FindResource("StringButton"),
                Content = label,
                ToolTip = $"{stringNumber} 弦 · {note} · {Tuning.Frequency(tuning.Notes[index]):0.00} Hz"
            };
            name.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = button });
            System.Windows.Automation.AutomationProperties.SetName(button, $"{stringNumber} 弦 {note}");
            button.Click += (_, _) => LockString(stringIndex);
            StringButtons.Children.Add(button);
            stringButtons.Add(button);
        }
    }

    private void ChangeTarget(int? index)
    {
        selectedString = index;
        Neck.Highlight(index);
        for (int button = 0; button < stringButtons.Count; button++)
            stringButtons[button].IsChecked = button == index;

        if (index is int active)
        {
            lastSelectedString = active;
            int midi = tuning.Notes[active];
            StringStatus.Text = $"{(automatic ? "自动" : "锁定")} · {tuning.Notes.Length - active} 弦";
            TargetNote.Text = tuning.NoteName(midi);
            TargetHz.Text = $"{Tuning.Frequency(midi):0.00} Hz";
        }
        else
        {
            StringStatus.Text = "等待识别";
            TargetNote.Text = "—";
            TargetHz.Text = "— Hz";
        }
    }

    private void UpdateInputOptions(CaptureEndpoints endpoints)
    {
        if (InputPicker.IsDropDownOpen || ReferenceEquals(displayedEndpoints, endpoints))
            return;

        bool initialScan = displayedEndpoints is null;
        displayedEndpoints = endpoints;
        if (initialScan && InputPicker.SelectedItem is null)
        {
            inputOption = endpoints.Rocksmith is null ? SystemDefault : Rocksmith;
            InputPicker.SelectedItem = inputOption;
        }

        for (int index = inputOptions.Count - 1; index >= 2; index--)
        {
            InputOption option = inputOptions[index];
            if (option != inputOption && endpoints.Find(option.EndpointId) is null)
                inputOptions.RemoveAt(index);
        }
        foreach (CaptureEndpoint endpoint in endpoints.Devices)
        {
            if (!endpoint.IsRocksmith && !inputOptions.Any(option => option.EndpointId == endpoint.Id))
                inputOptions.Add(new(endpoint.Name, endpoint.Id));
        }
        SelectEndpoint(endpoints);
    }

    private void Input_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InputPicker.SelectedItem is not InputOption option)
            return;

        inputOption = option;
        if (input.Endpoints is CaptureEndpoints endpoints)
            SelectEndpoint(endpoints);
    }

    private void SelectEndpoint(CaptureEndpoints endpoints)
    {
        CaptureEndpoint? endpoint = inputOption == SystemDefault ? endpoints.Find(endpoints.DefaultId)
            : inputOption == Rocksmith ? endpoints.Rocksmith
            : endpoints.Find(inputOption.EndpointId);

        if (endpoint == inputSelection?.Endpoint)
            return;

        inputSelection = endpoint is null ? null : new(endpoint);
        input.Select(inputSelection);
        visibleFrequency = null;
        lastAcceptedTimestamp = 0;
        lastPlottedTimestamp = 0;
        if (automatic)
            ChangeTarget(null);
        Graph.Reset();
        UpdatePitch(null, null);
        UpdateLevel(0);
    }

    private void Refresh(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        CaptureEndpoints? endpoints = input.Endpoints;
        if (endpoints is not null)
            UpdateInputOptions(endpoints);
        CaptureSnapshot snapshot = input.Snapshot;
        if (inputSelection is null)
        {
            snapshot = endpoints is { ErrorCode: not 0 }
                ? new(CaptureStatus.Unavailable, ErrorCode: endpoints.ErrorCode)
                : new(endpoints is null ? CaptureStatus.Searching : CaptureStatus.Disconnected);
        }
        else if (!ReferenceEquals(inputSelection, snapshot.Selection))
        {
            snapshot = new(CaptureStatus.Searching);
        }
        UpdateInputStatus(snapshot);
        PitchReading reading = snapshot.Reading is PitchReading sample &&
                               Stopwatch.GetElapsedTime(sample.Timestamp, now).TotalSeconds < 0.19
            ? sample
            : default;
        UpdateLevel(reading.Rms);

        if (reading.Frequency is double frequency)
        {
            visibleFrequency = frequency;
            lastAcceptedTimestamp = reading.Timestamp;
            if (automatic)
            {
                int nearest = tuning.NearestString(frequency);
                if (nearest != selectedString)
                    ChangeTarget(nearest);
            }

            if (selectedString is int active)
            {
                double cents = Tuning.Cents(frequency, Tuning.Frequency(tuning.Notes[active]));
                if (reading.Timestamp != lastPlottedTimestamp)
                {
                    Graph.Push(reading.Timestamp, cents, active);
                    lastPlottedTimestamp = reading.Timestamp;
                }
                UpdatePitch(frequency, cents);
                Graph.Update(now, cents, "");
                return;
            }
        }

        visibleFrequency = null;
        if (automatic && selectedString.HasValue &&
            (snapshot.Status != CaptureStatus.Listening ||
             Stopwatch.GetElapsedTime(lastAcceptedTimestamp, now).TotalSeconds > 0.65))
        {
            ChangeTarget(null);
        }
        UpdatePitch(null, null);
        string message = snapshot.Status switch
        {
            CaptureStatus.Disconnected => inputOption == Rocksmith ? "插入 Rocksmith 输入线" : "连接麦克风或选择其他输入",
            CaptureStatus.Unavailable => "等待音频输入恢复",
            CaptureStatus.Searching => "正在连接音频输入",
            _ => "拨响一根空弦"
        };
        Graph.Update(now, null, message);
    }

    private void UpdatePitch(double? frequency, double? deviation)
    {
        if (frequency is double detected && deviation is double cents)
        {
            DetectedNote.Text = tuning.NoteName((int)Math.Round(Tuning.Midi(detected)));
            DetectedHz.Text = $"{detected:0.00} Hz";
            bool inTune = Math.Abs(cents) <= PitchGraph.InTuneCents;
            double rounded = Math.Round(cents, 1);
            CentsValue.Text = rounded == 0 ? "0.0" : rounded.ToString("+0.0;−0.0");
            CentsValue.FontSize = Math.Abs(rounded) >= 1000 ? 28 : Math.Abs(rounded) >= 100 ? 35 : 40;
            CentsValue.Foreground = inTune ? DrawingStyle.InTune : DrawingStyle.Warning;
            PitchDirection.Text = inTune ? "已调准" : cents > 0 ? "偏高 · 放松琴弦" : "偏低 · 拧紧琴弦";
        }
        else
        {
            DetectedNote.Text = "—";
            DetectedHz.Text = "— Hz";
            CentsValue.Text = "—";
            CentsValue.FontSize = 40;
            CentsValue.Foreground = DrawingStyle.Muted;
            PitchDirection.Text = "等待音高";
        }
    }

    private void UpdateLevel(double rms)
    {
        double decibels = rms > 0 ? 20 * Math.Log10(rms) : double.NegativeInfinity;
        Meter.SetLevel(Math.Clamp((decibels + 64) / 58, 0, 1));
        LevelValue.Text = decibels < -90 ? "−∞ dB" : $"{decibels:0} dB";
    }

    private void UpdateInputStatus(CaptureSnapshot snapshot)
    {
        string name = inputSelection?.Endpoint.Name ?? inputOption.Name;
        InputName.Text = name;
        InputName.ToolTip = name;
        InputPicker.ToolTip = name;
        InputStatus.Text = snapshot.Status switch
        {
            CaptureStatus.Listening => $"已连接 · {snapshot.SampleRate / 1000.0:0.###} kHz",
            CaptureStatus.Disconnected => "未连接 · 等待插入",
            CaptureStatus.Unavailable => "输入不可用 · 正在重试",
            _ => "正在查找输入"
        };
        InputStatus.ToolTip = snapshot.Status switch
        {
            CaptureStatus.Listening => $"{snapshot.SampleRate} Hz · {snapshot.Channels} 声道",
            CaptureStatus.Unavailable => $"Windows 音频错误 0x{snapshot.ErrorCode:X8}。请检查系统的麦克风权限或关闭独占此输入的软件。",
            _ => null
        };
        InputDot.Fill = snapshot.Status switch
        {
            CaptureStatus.Listening => DrawingStyle.InTune,
            CaptureStatus.Unavailable => DrawingStyle.Warning,
            _ => DrawingStyle.Muted
        };
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Data = Geometry.Parse(maximized ? "M 0,3 H 7 V 10 H 0 Z M 3,3 V 0 H 10 V 7 H 7" : "M 0,0 H 9 V 9 H 0 Z");
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, (string)MaximizeButton.ToolTip);
    }

    private sealed record InputOption(string Name, string? EndpointId);

    protected override void OnClosed(EventArgs e)
    {
        timer.Stop();
        input.Dispose();
        base.OnClosed(e);
    }
}
