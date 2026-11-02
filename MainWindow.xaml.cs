using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Threading;

namespace Anuxes.LogLens;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int LivePollMilliseconds = 900;
    private const int InitialLoadLimit = 100_000;
    private const int MaxRelatedTraceLines = 18;

    private static readonly Regex TimestampRegex = new(
        @"^(?<time>\[[^\]]+\]|\d{4}[-/]\d{2}[-/]\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?|\d{2}:\d{2}:\d{2}(?:[.,]\d+)?)\s*(?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LevelRegex = new(
        @"\b(?<level>TRACE|DEBUG|INFO|WARN(?:ING)?|ERROR|FATAL)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly DispatcherTimer _liveTimer;
    private readonly object _tailLock = new();
    private FileSystemWatcher? _fileWatcher;
    private readonly List<LogEntry> _searchMatches = new();
    private string? _currentFilePath;
    private long _lastReadPosition;
    private byte[] _pendingTailBytes = Array.Empty<byte>();
    private DateTime _trackedCreationTimeUtc;
    private bool _isPaused;
    private bool _isLivePollRunning;
    private bool _isBulkLoading;
    private int _errorTotal;
    private int _warnTotal;
    private int _fatalTotal;
    private LogEntry? _selectedEntry;
    private string _searchTerm = string.Empty;
    private int _currentMatchIndex = -1;

    public ObservableCollection<LogEntry> Entries { get; } = new();
    public ICollectionView LogView { get; }

    public string SearchTerm
    {
        get => _searchTerm;
        private set
        {
            if (_searchTerm == value) return;
            _searchTerm = value;
            OnPropertyChanged();
        }
    }

    public LogEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (ReferenceEquals(_selectedEntry, value)) return;
            _selectedEntry = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();

        LogView = CollectionViewSource.GetDefaultView(Entries);
        LogView.Filter = FilterEntry;
        DataContext = this;

        Entries.CollectionChanged += (_, args) =>
        {
            if (_isBulkLoading) return;

            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                _errorTotal = 0;
                _warnTotal = 0;
                _fatalTotal = 0;
            }
            else if (args.NewItems is not null)
            {
                foreach (LogEntry added in args.NewItems)
                {
                    if (added.Level == "ERROR") _errorTotal++;
                    else if (added.Level == "WARN") _warnTotal++;
                    else if (added.Level == "FATAL") _fatalTotal++;
                }
            }

            UpdateFooterCount();
            UpdateSeveritySummary();
        };

        _liveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(LivePollMilliseconds)
        };
        _liveTimer.Tick += async (_, _) => await PollLiveFileAsync();

        UpdateFooterCount();
        UpdateSeveritySummary();
        StateChanged += (_, _) => UpdateWindowStateButton();
    }

    private static LogEntry ParseLine(string line)
    {
        string timeText = "—";
        string payload = line;

        Match timestampMatch = TimestampRegex.Match(line);
        if (timestampMatch.Success)
        {
            timeText = timestampMatch.Groups["time"].Value.Trim('[', ']');
            payload = timestampMatch.Groups["rest"].Value;
        }

        Match levelMatch = LevelRegex.Match(payload);
        string level = "OTHER";
        string message = payload;

        if (levelMatch.Success)
        {
            level = levelMatch.Groups["level"].Value.ToUpperInvariant();
            if (level == "WARNING") level = "WARN";
            message = (payload[..levelMatch.Index] + payload[(levelMatch.Index + levelMatch.Length)..])
                .Trim(' ', '\t', ':', '|', '-', '[', ']');
        }

        if (string.IsNullOrWhiteSpace(message)) message = line;

        return new LogEntry
        {
            TimeText = timeText,
            Level = level,
            Message = message,
            Raw = line
        };
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a log file",
            Filter = "Log and text files (*.log;*.txt;*.jsonl)|*.log;*.txt;*.jsonl|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            await LoadFileAsync(dialog.FileName);
    }

    private async Task LoadFileAsync(string path)
    {
        try
        {
            SetStatus($"Loading {Path.GetFileName(path)}...");
            StopLiveMonitor();
            LiveModeCheck.IsChecked = false;
            _isPaused = false;
            PauseButton.Content = "Ⅱ  Pause";

            var snapshot = await Task.Run(() => ReadInitialSnapshot(path));

            _isBulkLoading = true;
            Entries.Clear();
            foreach (string line in snapshot.Lines)
                Entries.Add(ParseLine(line));
            _isBulkLoading = false;
            RecountSeverityTotals();

            _currentFilePath = path;
            SourceName.Text = Path.GetFileName(path);
            lock (_tailLock)
            {
                _lastReadPosition = snapshot.BytePosition;
                _pendingTailBytes = Array.Empty<byte>();
                _trackedCreationTimeUtc = snapshot.CreationTimeUtc;
            }

            SelectedEntry = Entries.LastOrDefault();
            LogGrid.SelectedItem = SelectedEntry;
            ApplyFilters();

            bool limitReached = snapshot.LimitReached;
            SetStatus(limitReached
                ? $"Loaded first {InitialLoadLimit:N0} lines • file: {Path.GetFileName(path)}"
                : $"Loaded {snapshot.Lines.Count:N0} lines • {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open file: {ex.Message}");
            MessageBox.Show(this, ex.Message, "ANUXES LOGLENS — Open Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static FileSnapshot ReadInitialSnapshot(string path)
    {
        var lines = new List<string>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        string? line;
        bool limitReached = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (lines.Count >= InitialLoadLimit)
            {
                limitReached = true;
                break;
            }
            lines.Add(line);
        }

        // ReadLine may buffer ahead. When the file is completely read, StreamReader is at EOF.
        // If the limit is reached, Live Mode starts at the actual current file end to avoid replaying the tail.
        long bytePosition = limitReached ? stream.Length : stream.Position;
        return new FileSnapshot(lines, bytePosition, limitReached, File.GetCreationTimeUtc(path));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) || !File.Exists(_currentFilePath))
        {
            SetStatus("No file to refresh. Open a log first.");
            return;
        }

        await LoadFileAsync(_currentFilePath);
    }

    private void LiveMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        if (LiveModeCheck.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath))
            {
                LiveModeCheck.IsChecked = false;
                SetStatus("Open a log file before enabling Live Mode");
                return;
            }

            _isPaused = false;
            PauseButton.Content = "Ⅱ  Pause";
            StartLiveMonitor();
        }
        else
        {
            StopLiveMonitor();
            SetStatus("Live Monitor disabled");
        }
    }

    private void StartLiveMonitor()
    {
        StopLiveMonitor();
        _isPaused = false;
        PauseButton.Content = "Ⅱ  Pause";

        // FileSystemWatcher provides fast updates. The timer remains as a fallback
        // in case Windows drops a notification or the writer replaces the file.
        try
        {
            string fullPath = Path.GetFullPath(_currentFilePath!);
            string directory = Path.GetDirectoryName(fullPath)!;
            string fileName = Path.GetFileName(fullPath);

            _fileWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                               NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = false
            };
            _fileWatcher.Changed += LiveFileWatcher_Changed;
            _fileWatcher.Created += LiveFileWatcher_Changed;
            _fileWatcher.Renamed += LiveFileWatcher_Renamed;
            _fileWatcher.Error += LiveFileWatcher_Error;
            _fileWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _fileWatcher?.Dispose();
            _fileWatcher = null;
            SetStatus("Live Monitor active • notification fallback unavailable; polling is running");
        }

        _liveTimer.Start();
        if (_fileWatcher is not null)
            SetStatus("LIVE • monitoring file changes");

        // Read immediately, then react to file events and keep a periodic safety poll.
        _ = PollLiveFileAsync();
    }

    private void StopLiveMonitor()
    {
        _liveTimer.Stop();
        if (_fileWatcher is not null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Changed -= LiveFileWatcher_Changed;
            _fileWatcher.Created -= LiveFileWatcher_Changed;
            _fileWatcher.Renamed -= LiveFileWatcher_Renamed;
            _fileWatcher.Error -= LiveFileWatcher_Error;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
    }

    private void LiveFileWatcher_Changed(object sender, FileSystemEventArgs e)
        => QueueLivePoll();

    private void LiveFileWatcher_Renamed(object sender, RenamedEventArgs e)
        => QueueLivePoll();

    private void LiveFileWatcher_Error(object sender, ErrorEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (LiveModeCheck.IsChecked == true)
                SetStatus("LIVE • filesystem notifications interrupted; polling fallback active");
        }));
    }

    private void QueueLivePoll()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (LiveModeCheck.IsChecked == true && !_isPaused)
                _ = PollLiveFileAsync();
        }), DispatcherPriority.Background);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (LiveModeCheck.IsChecked != true)
        {
            SetStatus("Enable Live Mode before pausing updates.");
            return;
        }

        _isPaused = !_isPaused;
        PauseButton.Content = _isPaused ? "▶  Resume" : "Ⅱ  Pause";
        SetStatus(_isPaused ? "Live updates paused" : "LIVE • monitoring resumed");
        if (!_isPaused) _ = PollLiveFileAsync();
    }

    private async Task PollLiveFileAsync()
    {
        if (LiveModeCheck.IsChecked != true || _isPaused || _isLivePollRunning || string.IsNullOrWhiteSpace(_currentFilePath)) return;

        _isLivePollRunning = true;
        string path = _currentFilePath;
        try
        {
            List<string> appendedLines = await Task.Run(() => ReadAppendedLines(path));
            if (!string.Equals(path, _currentFilePath, StringComparison.OrdinalIgnoreCase)) return;
            if (appendedLines.Count == 0) return;

            _isBulkLoading = true;
            foreach (string line in appendedLines)
                Entries.Add(ParseLine(line));
            _isBulkLoading = false;
            RecountSeverityTotals();

            ApplyFilters();
            RebuildSearchMatches();
            if (LogGrid.Items.Count > 0)
                LogGrid.ScrollIntoView(LogGrid.Items[LogGrid.Items.Count - 1]);

            SetStatus($"LIVE • received {appendedLines.Count} new line(s)");
        }
        catch (FileNotFoundException)
        {
            _liveTimer.Stop();
            LiveModeCheck.IsChecked = false;
            SetStatus("The source file is no longer available");
        }
        catch (IOException)
        {
            // The writer may temporarily hold or rotate the file. The next poll retries.
        }
        catch (Exception ex)
        {
            SetStatus($"Live read error: {ex.Message}");
        }
        finally
        {
            _isLivePollRunning = false;
        }
    }

    private List<string> ReadAppendedLines(string path)
    {
        lock (_tailLock)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            DateTime creationTimeUtc = File.GetCreationTimeUtc(path);
            if (stream.Length < _lastReadPosition || creationTimeUtc != _trackedCreationTimeUtc)
            {
                // The file was truncated or replaced. Restart the tail reader.
                _lastReadPosition = 0;
                _pendingTailBytes = Array.Empty<byte>();
                _trackedCreationTimeUtc = creationTimeUtc;
            }

            long available = stream.Length - _lastReadPosition;
            if (available <= 0) return new List<string>();

            // Limit each read so a long pause does not allocate an unbounded buffer.
            int bytesToRead = (int)Math.Min(available, 8L * 1024 * 1024);
            byte[] chunk = new byte[bytesToRead];
            stream.Seek(_lastReadPosition, SeekOrigin.Begin);

            int totalRead = 0;
            while (totalRead < bytesToRead)
            {
                int read = stream.Read(chunk, totalRead, bytesToRead - totalRead);
                if (read == 0) break;
                totalRead += read;
            }

            if (totalRead == 0) return new List<string>();
            _lastReadPosition += totalRead;

            byte[] combined = new byte[_pendingTailBytes.Length + totalRead];
            Buffer.BlockCopy(_pendingTailBytes, 0, combined, 0, _pendingTailBytes.Length);
            Buffer.BlockCopy(chunk, 0, combined, _pendingTailBytes.Length, totalRead);

            var completeLines = new List<string>();
            int lineStart = 0;
            for (int i = 0; i < combined.Length; i++)
            {
                if (combined[i] != (byte)'\n') continue;

                int lineLength = i - lineStart;
                if (lineLength > 0 && combined[i - 1] == (byte)'\r')
                    lineLength--;

                if (lineLength > 0)
                {
                    string line = Encoding.UTF8.GetString(combined, lineStart, lineLength);
                    if (lineStart == 0 && line.Length > 0 && line[0] == '\uFEFF')
                        line = line[1..];
                    if (line.Length > 0) completeLines.Add(line);
                }

                lineStart = i + 1;
            }

            // Keep incomplete bytes (including a split UTF-8 character) until the next write.
            _pendingTailBytes = combined.AsSpan(lineStart).ToArray();
            return completeLines;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchTerm = SearchBox.Text.Trim();
        _currentMatchIndex = -1;
        ApplyFilters();
        RebuildSearchMatches();
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyFilters();
        RebuildSearchMatches();
    }

    private void ApplyFilters()
    {
        LogView.Refresh();
        UpdateFooterCount();
        UpdateSeveritySummary();
    }

    private bool FilterEntry(object item)
    {
        if (item is not LogEntry entry || !IsLevelEnabled(entry.Level)) return false;
        return MatchesSearch(entry, SearchTerm);
    }

    private bool IsLevelEnabled(string level) => level switch
    {
        "TRACE" => TraceFilter?.IsChecked == true,
        "DEBUG" => DebugFilter?.IsChecked == true,
        "INFO" => InfoFilter?.IsChecked == true,
        "WARN" => WarnFilter?.IsChecked == true,
        "ERROR" => ErrorFilter?.IsChecked == true,
        "FATAL" => FatalFilter?.IsChecked == true,
        _ => OtherFilter?.IsChecked == true
    };

    private static bool MatchesSearch(LogEntry entry, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return entry.Message.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Raw.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Level.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildSearchMatches()
    {
        _searchMatches.Clear();
        if (SearchTerm.Length > 0)
            _searchMatches.AddRange(Entries.Where(entry => IsLevelEnabled(entry.Level) && MatchesSearch(entry, SearchTerm)));

        if (_searchMatches.Count == 0)
        {
            _currentMatchIndex = -1;
            SearchMatchCount.Text = SearchTerm.Length == 0 ? "0 / 0" : "0 / 0";
        }
        else
        {
            int selectedIndex = SelectedEntry is null ? -1 : _searchMatches.IndexOf(SelectedEntry);
            _currentMatchIndex = selectedIndex;
            UpdateSearchMatchCount();
        }

    }

    private void PreviousMatch_Click(object sender, RoutedEventArgs e) => NavigateSearchMatch(-1);
    private void NextMatch_Click(object sender, RoutedEventArgs e) => NavigateSearchMatch(1);

    private void NavigateSearchMatch(int direction)
    {
        if (_searchMatches.Count == 0)
        {
            SetStatus("No search matches. Enter a term or adjust the filters.");
            return;
        }

        if (_currentMatchIndex < 0)
            _currentMatchIndex = direction > 0 ? 0 : _searchMatches.Count - 1;
        else
            _currentMatchIndex = (_currentMatchIndex + direction + _searchMatches.Count) % _searchMatches.Count;

        LogEntry match = _searchMatches[_currentMatchIndex];
        SelectedEntry = match;
        LogGrid.SelectedItem = match;
        LogGrid.ScrollIntoView(match);
        UpdateSearchMatchCount();
        SetStatus($"Search match {_currentMatchIndex + 1} of {_searchMatches.Count}");
    }

    private void UpdateSearchMatchCount()
    {
        SearchMatchCount.Text = _searchMatches.Count == 0 || _currentMatchIndex < 0
            ? $"— / {_searchMatches.Count}"
            : $"{_currentMatchIndex + 1} / {_searchMatches.Count}";
    }

    private void LogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogGrid.SelectedItem is LogEntry entry)
        {
            SelectedEntry = entry;
            if (_searchMatches.Count > 0)
            {
                int matchIndex = _searchMatches.IndexOf(entry);
                if (matchIndex >= 0)
                {
                    _currentMatchIndex = matchIndex;
                    UpdateSearchMatchCount();
                }
            }
        }
    }

    private void RecountSeverityTotals()
    {
        _errorTotal = Entries.Count(entry => entry.Level == "ERROR");
        _warnTotal = Entries.Count(entry => entry.Level == "WARN");
        _fatalTotal = Entries.Count(entry => entry.Level == "FATAL");
        UpdateSeveritySummary();
    }

    private void UpdateSeveritySummary()
    {
        if (ErrorCount is null) return;
        ErrorCount.Text = _errorTotal.ToString("N0");
        WarnCount.Text = _warnTotal.ToString("N0");
        FatalCount.Text = _fatalTotal.ToString("N0");
    }

    private void UpdateFooterCount()
    {
        if (FooterCount is null) return;
        int visibleCount = LogView.Cast<object>().Count();
        FooterCount.Text = $"{visibleCount:N0} / {Entries.Count:N0} ENTRIES";
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (Entries.Count == 0)
        {
            SetStatus("Nothing to export. Open a log file first.");
            return;
        }

        List<int> errorIndexes = Entries.Select((entry, index) => (entry, index))
            .Where(pair => pair.entry.Level is "ERROR" or "FATAL")
            .Select(pair => pair.index)
            .ToList();

        if (errorIndexes.Count == 0)
        {
            SetStatus("No ERROR or FATAL entries found in the loaded log.");
            return;
        }

        var reportLines = new List<string>
        {
            "ANUXES LOGLENS — ERROR REPORT",
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Source: {_currentFilePath ?? "Unknown"}",
            $"ERROR: {Entries.Count(entry => entry.Level == "ERROR")}    FATAL: {Entries.Count(entry => entry.Level == "FATAL")}",
            new string('=', 78),
            string.Empty
        };

        var included = new HashSet<int>();
        foreach (int index in errorIndexes)
        {
            if (!included.Add(index)) continue;
            LogEntry error = Entries[index];
            reportLines.Add($"[{error.Level}] {error.TimeText}");
            reportLines.Add(error.Raw);

            int end = Math.Min(Entries.Count - 1, index + MaxRelatedTraceLines);
            for (int i = index + 1; i <= end; i++)
            {
                LogEntry candidate = Entries[i];
                if (candidate.Level is "ERROR" or "FATAL") break;
                if (LooksLikeStackTrace(candidate))
                {
                    included.Add(i);
                    reportLines.Add("  " + candidate.Raw);
                }
            }
            reportLines.Add(string.Empty);
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export error report",
            Filter = "Text report (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"LOGLENS_ErrorReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllLines(dialog.FileName, reportLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            SetStatus($"Exported {errorIndexes.Count} error/fatal event(s) to {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            SetStatus($"Export failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "ANUXES LOGLENS — Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool LooksLikeStackTrace(LogEntry entry)
    {
        string raw = entry.Raw.TrimStart();
        string message = entry.Message.TrimStart();
        return entry.Raw.Contains("StackTrace", StringComparison.OrdinalIgnoreCase)
            || entry.Raw.Contains("stack trace", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("at ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("at ", StringComparison.OrdinalIgnoreCase)
            || entry.Raw.Contains("Exception:", StringComparison.OrdinalIgnoreCase);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateWindowStateButton();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateWindowStateButton()
    {
        if (MaximizeButton is not null)
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
        }
    }

    private void SetStatus(string message) => FooterStatus.Text = message;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected override void OnClosed(EventArgs e)
    {
        StopLiveMonitor();
        base.OnClosed(e);
    }

    private sealed record FileSnapshot(List<string> Lines, long BytePosition, bool LimitReached, DateTime CreationTimeUtc);
}

public sealed class LogEntry
{
    public string TimeText { get; init; } = "—";
    public string Level { get; init; } = "OTHER";
    public string Message { get; init; } = string.Empty;
    public string Raw { get; init; } = string.Empty;
}

public sealed class HighlightTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
        nameof(SourceText), typeof(string), typeof(HighlightTextBlock), new PropertyMetadata(string.Empty, OnHighlightPropertyChanged));

    public static readonly DependencyProperty SearchTermProperty = DependencyProperty.Register(
        nameof(SearchTerm), typeof(string), typeof(HighlightTextBlock), new PropertyMetadata(string.Empty, OnHighlightPropertyChanged));

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string SearchTerm
    {
        get => (string)GetValue(SearchTermProperty);
        set => SetValue(SearchTermProperty, value);
    }

    private static void OnHighlightPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HighlightTextBlock block) block.RebuildInlines();
    }

    private void RebuildInlines()
    {
        Inlines.Clear();
        string text = SourceText ?? string.Empty;
        string query = SearchTerm ?? string.Empty;
        if (text.Length == 0) return;

        if (query.Length == 0)
        {
            Inlines.Add(new Run(text));
            return;
        }

        int position = 0;
        while (position < text.Length)
        {
            int matchIndex = text.IndexOf(query, position, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                Inlines.Add(new Run(text[position..]));
                break;
            }

            if (matchIndex > position)
                Inlines.Add(new Run(text[position..matchIndex]));

            Inlines.Add(new Run(text.Substring(matchIndex, query.Length))
            {
                Background = new SolidColorBrush(Color.FromRgb(91, 62, 126)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            });
            position = matchIndex + query.Length;
        }
    }
}
