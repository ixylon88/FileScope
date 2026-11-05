using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using FileScope.App.Infrastructure;
using FileScope.App.Localization;
using FileScope.App.Services;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;
using FileScope.Core.Files;
using FileScope.Core.Export;
using FileScope.App.Presentation;

namespace FileScope.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IFileAnalysisService _analysis;
    private readonly IFilePickerService _picker;
    private readonly IFolderScanner _folderScanner;
    private readonly IResultExporter _exporter;
    private readonly LocalizationService _strings;
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<FileRowViewModel> _multipleSignatures = [];
    private CancellationTokenSource? _cancellation;
    private bool _clearRequested, _disposed, _inspectCertificates, _onlineRevocation;
    private bool _inspectFileProperties, _inspectPeHeader;
    private long _epoch;
    private int _processed, _batchTotal, _skipped;
    private string _searchText = "";
    private FilterOption<RowStatus?> _stateFilter;
    private FilterOption<SignatureFilter> _signatureFilter;

    public ObservableCollection<FileRowViewModel> Rows { get; } = [];
    public ListCollectionView RowsView { get; }
    public IReadOnlyList<ChecksumOption> ChecksumOptions { get; }
    public OptionsViewModel Options { get; }
    public IReadOnlyList<FilterOption<RowStatus?>> StateFilters { get; }
    public IReadOnlyList<FilterOption<SignatureFilter>> SignatureFilters { get; }
    public AsyncRelayCommand BrowseCommand { get; }
    public AsyncRelayCommand AddFolderCommand { get; }
    public AsyncRelayCommand ExportCsvCommand { get; }
    public AsyncRelayCommand DropFilesCommand { get; }
    public AsyncRelayCommand ReanalyseCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand ResetFiltersCommand { get; }

    public MainViewModel(IFileAnalysisService analysis, IFilePickerService picker, LocalizationService strings,
        OptionsViewModel options, IFolderScanner folderScanner, IResultExporter exporter)
    {
        _analysis = analysis;
        _picker = picker;
        _folderScanner = folderScanner;
        _exporter = exporter;
        _strings = strings;
        Options = options;
        // Restore before attaching change handlers: initialization must not save
        // defaults over the loaded choices or write a partially restored state.
        var selection = options.AnalysisPreferences;
        ChecksumOptions = Array.AsReadOnly(
        [
            new ChecksumOption(ChecksumAlgorithm.MD5, "MD5", selection.MD5),
            new ChecksumOption(ChecksumAlgorithm.SHA1, "SHA-1", selection.SHA1),
            new ChecksumOption(ChecksumAlgorithm.SHA256, "SHA-256", selection.SHA256),
            new ChecksumOption(ChecksumAlgorithm.SHA384, "SHA-384", selection.SHA384),
            new ChecksumOption(ChecksumAlgorithm.SHA512, "SHA-512", selection.SHA512)
        ]);
        _inspectCertificates = selection.InspectCertificates;
        _onlineRevocation = selection.OnlineRevocation;
        _inspectFileProperties = selection.InspectFileProperties;
        _inspectPeHeader = selection.InspectPeHeader;
        StateFilters = Array.AsReadOnly(
        [
            new FilterOption<RowStatus?>(null, "AllStates", strings),
            new FilterOption<RowStatus?>(RowStatus.Queued, "Row_Queued", strings),
            new FilterOption<RowStatus?>(RowStatus.Running, "Row_Running", strings),
            new FilterOption<RowStatus?>(RowStatus.Completed, "Row_Completed", strings),
            new FilterOption<RowStatus?>(RowStatus.Failed, "Row_Failed", strings),
            new FilterOption<RowStatus?>(RowStatus.Cancelled, "Row_Cancelled", strings)
        ]);
        SignatureFilters = Array.AsReadOnly(
        [
            new FilterOption<SignatureFilter>(SignatureFilter.All, "AllSignatures", strings),
            new FilterOption<SignatureFilter>(SignatureFilter.Valid, "Filter_Valid", strings),
            new FilterOption<SignatureFilter>(SignatureFilter.NoSignature, "Signature_NoEmbeddedSignature", strings),
            new FilterOption<SignatureFilter>(SignatureFilter.Attention, "Filter_Attention", strings),
            new FilterOption<SignatureFilter>(SignatureFilter.NotChecked, "Signature_NotChecked", strings)
        ]);
        _stateFilter = StateFilters[0];
        _signatureFilter = SignatureFilters[0];
        RowsView = (ListCollectionView)CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = MatchesFilters;
        BrowseCommand = new AsyncRelayCommand(async _ =>
            await AddFilesAsync(_picker.PickFiles(strings["OpenTitle"], strings["AllFiles"])), OnCommandError, _ => IsIdle);
        AddFolderCommand = new AsyncRelayCommand(_ => AddFolderAsync(), OnCommandError, _ => IsIdle);
        ExportCsvCommand = new AsyncRelayCommand(_ => ExportCsvAsync(), OnCommandError,
            _ => IsIdle && HasRows && Options.HasExportColumns);
        DropFilesCommand = new AsyncRelayCommand(p => AddFilesAsync((string[])p!), OnCommandError,
            p => IsIdle && p is string[] { Length: > 0 });
        ReanalyseCommand = new AsyncRelayCommand(_ => RunBatchAsync(Rows.ToArray()), OnCommandError,
            _ => IsIdle && HasRows);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy && _cancellation?.IsCancellationRequested == false);
        ClearCommand = new RelayCommand(Clear, () => HasRows && !_clearRequested);
        ResetFiltersCommand = new RelayCommand(ResetFilters);
        strings.CultureChanged += OnCultureChanged;
        options.HashFormatChanged += OnHashFormatChanged;
        options.ExportSelectionChanged += OnExportSelectionChanged;
        foreach (var option in ChecksumOptions) option.PropertyChanged += OnChecksumOptionChanged;
    }

    public bool IsBusy
    {
        get;
        private set
        {
            if (!SetProperty(ref field, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(CanChooseOnlineRevocation));
            NotifyCommands();
        }
    }
    public bool IsIdle => !IsBusy && !_disposed;
    public bool HasRows => Rows.Count > 0;
    public bool ShowEmptyState => Rows.Count == 0;
    public bool ShowNoMatches => HasRows && RowsView.Count == 0;
    public bool InspectCertificates
    {
        get => _inspectCertificates;
        set
        {
            if (!SetProperty(ref _inspectCertificates, value)) return;
            OnPropertyChanged(nameof(CanChooseOnlineRevocation));
            OnPropertyChanged(nameof(ShowSignatureSelectorColumn));
            SaveAnalysisPreferences();
        }
    }
    public bool ShowSignatureSelectorColumn => InspectCertificates && _multipleSignatures.Count > 0;
    public bool OnlineRevocation
    {
        get => _onlineRevocation;
        set { if (SetProperty(ref _onlineRevocation, value)) SaveAnalysisPreferences(); }
    }
    public bool CanChooseOnlineRevocation => InspectCertificates && IsIdle;
    public bool InspectFileProperties
    {
        get => _inspectFileProperties;
        set { if (SetProperty(ref _inspectFileProperties, value)) SaveAnalysisPreferences(); }
    }
    public bool InspectPeHeader
    {
        get => _inspectPeHeader;
        set { if (SetProperty(ref _inspectPeHeader, value)) SaveAnalysisPreferences(); }
    }
    public bool ShowMD5Column => IsChecksumSelected(ChecksumAlgorithm.MD5);
    public bool ShowSHA1Column => IsChecksumSelected(ChecksumAlgorithm.SHA1);
    public bool ShowSHA256Column => IsChecksumSelected(ChecksumAlgorithm.SHA256);
    public bool ShowSHA384Column => IsChecksumSelected(ChecksumAlgorithm.SHA384);
    public bool ShowSHA512Column => IsChecksumSelected(ChecksumAlgorithm.SHA512);
    public bool IsCheckingSignature
    {
        get;
        private set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(IsProgressIndeterminate)); }
    }
    private bool IsFileOperation
    {
        get;
        set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(IsProgressIndeterminate)); }
    }
    public bool IsProgressIndeterminate => IsCheckingSignature || IsFileOperation;
    public double Progress { get; private set => SetProperty(ref field, value); }
    public string CurrentFile { get; private set => SetProperty(ref field, value); } = "";
    public string LastError { get; private set => SetProperty(ref field, value); } = "";
    public bool HasError => LastError.Length != 0;
    public string StatusText { get => _strings.Format(field, _processed, _batchTotal, _skipped); private set; } = "Ready";
    public string Statistics => _strings.Format("Statistics", RowsView.Count, Rows.Count);
    public XmlLanguage UiLanguage => XmlLanguage.GetLanguage(_strings.Culture.IetfLanguageTag);
    public FileRowViewModel? SelectedRow { get; set => SetProperty(ref field, value); }
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) RefreshView(); }
    }
    public FilterOption<RowStatus?> StateFilter
    {
        get => _stateFilter;
        set { if (value is not null && SetProperty(ref _stateFilter, value)) RefreshView(); }
    }
    public FilterOption<SignatureFilter> SignatureFilterOption
    {
        get => _signatureFilter;
        set { if (value is not null && SetProperty(ref _signatureFilter, value)) RefreshView(); }
    }
    private void OnHashFormatChanged(object? sender, EventArgs args)
    {
        foreach (var row in Rows) row.SetHashFormat(Options.SelectedHashFormat.Value);
        RefreshView();
    }

    private bool IsChecksumSelected(ChecksumAlgorithm algorithm) =>
        ChecksumOptions.Single(option => option.Algorithm == algorithm).IsSelected;

    private void OnChecksumOptionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not ChecksumOption option || args.PropertyName != nameof(ChecksumOption.IsSelected)) return;
        var property = option.Algorithm switch
        {
            ChecksumAlgorithm.MD5 => nameof(ShowMD5Column),
            ChecksumAlgorithm.SHA1 => nameof(ShowSHA1Column),
            ChecksumAlgorithm.SHA256 => nameof(ShowSHA256Column),
            ChecksumAlgorithm.SHA384 => nameof(ShowSHA384Column),
            ChecksumAlgorithm.SHA512 => nameof(ShowSHA512Column),
            _ => throw new ArgumentOutOfRangeException(nameof(sender), option.Algorithm, "The value of member 'Algorithm' exceeded the expected range.")
        };
        OnPropertyChanged(property);
        SaveAnalysisPreferences();
    }

    private void SaveAnalysisPreferences() => Options.SaveAnalysisPreferences(new AnalysisPreferences
    {
        MD5 = IsChecksumSelected(ChecksumAlgorithm.MD5),
        SHA1 = IsChecksumSelected(ChecksumAlgorithm.SHA1),
        SHA256 = IsChecksumSelected(ChecksumAlgorithm.SHA256),
        SHA384 = IsChecksumSelected(ChecksumAlgorithm.SHA384),
        SHA512 = IsChecksumSelected(ChecksumAlgorithm.SHA512),
        InspectCertificates = InspectCertificates,
        OnlineRevocation = OnlineRevocation,
        InspectFileProperties = InspectFileProperties,
        InspectPeHeader = InspectPeHeader
    });

    private async Task AddFolderAsync()
    {
        if (!IsIdle) return;
        var folder = _picker.PickFolder(_strings["AddFolderTitle"]);
        if (folder is null) return;
        var token = BeginFileOperation("ScanningFolder");
        CurrentFile = folder;
        FolderScanResult? result = null;
        try
        {
            result = await Task.Run(() => _folderScanner.Scan(folder, token), token);
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            result = null;
            SetStatus("FolderScanCancelled");
        }
        finally { EndFileOperation(); }
        if (result is null || _disposed) return;
        var warning = result.Issues.Count == 0 && result.SkippedDirectoryLinks == 0 ? ""
            : _strings.Format("FolderScanWarning", result.Issues.Count, result.SkippedDirectoryLinks);
        if (result.Issues.Count > 0) warning += $"\n{result.Issues[0].Path}: {result.Issues[0].Message}";
        await AddFilesAsync(result.Files, warning);
        if (result.Files.Count == 0) SetStatus("FolderEmpty");
    }

    private async Task ExportCsvAsync()
    {
        if (!IsIdle || !HasRows || !Options.HasExportColumns) return;
        var path = _picker.PickCsvDestination(_strings["ExportCsvTitle"], _strings["CsvFiles"]);
        if (path is null) return;
        // A manually typed filename must never replace an analyzed source file.
        if (_paths.Contains(Path.GetFullPath(path)))
        {
            LastError = _strings["ExportSourceConflict"];
            OnPropertyChanged(nameof(HasError));
            return;
        }
        var token = BeginFileOperation("ExportingCsv");
        CurrentFile = path;
        try
        {
            // Rows, not RowsView: filters/selection never omit loaded files.
            var table = ResultExportProjection.Capture(Rows, Options.Columns);
            await Task.Run(() => _exporter.ExportAsync(path, table, token), token);
            _processed = table.Rows.Count;
            SetStatus("CsvExported");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { SetStatus("CsvExportCancelled"); }
        finally { EndFileOperation(); }
    }

    private CancellationToken BeginFileOperation(string status)
    {
        ++_epoch;
        _cancellation = new CancellationTokenSource();
        _clearRequested = false;
        LastError = "";
        OnPropertyChanged(nameof(HasError));
        Progress = 0;
        IsFileOperation = true;
        IsBusy = true;
        SetStatus(status);
        return _cancellation.Token;
    }

    private void EndFileOperation()
    {
        ++_epoch;
        _cancellation?.Dispose();
        _cancellation = null;
        IsFileOperation = false;
        IsBusy = false;
        CurrentFile = "";
        if (_clearRequested) ClearRows();
        NotifyCommands();
    }

    private void OnExportSelectionChanged(object? sender, EventArgs args) => ExportCsvCommand.NotifyCanExecuteChanged();

    private async Task AddFilesAsync(IEnumerable<string> paths, string warning = "")
    {
        if (!IsIdle) return;
        var token = BeginFileOperation("PreparingFiles");
        LastError = warning;
        OnPropertyChanged(nameof(HasError));
        var added = new List<FileRowViewModel>();
        _skipped = 0;
        var cancelled = false;
        var visited = 0;
        try
        {
            // Keep the view readable during synchronous collection notifications.
            // Yield in chunks so a large folder can render and accept Cancel/Clear.
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                if (++visited % 128 == 0)
                {
                    NotifyRowsChanged();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    token.ThrowIfCancellationRequested();
                }
                if (string.IsNullOrWhiteSpace(path)) continue;
                string normalized;
                try { normalized = Path.GetFullPath(path); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
                { normalized = path; } // The core service reports the invalid path as a row.
                if (!_paths.Add(normalized)) { _skipped++; continue; }
                var row = new FileRowViewModel(normalized, _strings, Options.SelectedHashFormat.Value);
                row.SignaturesChanged += OnSignaturesChanged;
                row.SignatureSelectionChanged += OnSignatureSelectionChanged;
                Rows.Add(row);
                added.Add(row);
            }
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            cancelled = true;
            foreach (var row in added) row.Cancel();
            SetStatus("IntakeCancelled");
        }
        finally { EndFileOperation(); RefreshView(); }
        if (cancelled || _disposed) return;
        if (added.Count == 0) { SetStatus(_skipped > 0 ? "DuplicatesSkipped" : "Ready"); return; }
        await RunBatchAsync(added, warning);
    }

    private async Task RunBatchAsync(IReadOnlyList<FileRowViewModel> batch, string warning = "")
    {
        if (!IsIdle || batch.Count == 0) return;
        var options = new AnalysisOptions(ChecksumOptions.Where(x => x.IsSelected).Select(x => x.Algorithm),
            InspectCertificates, OnlineRevocation ? RevocationMode.Online : RevocationMode.OfflineCache,
            InspectFileProperties, InspectPeHeader);
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        var epoch = ++_epoch;
        _processed = 0;
        _batchTotal = batch.Count;
        _clearRequested = false;
        LastError = warning;
        OnPropertyChanged(nameof(HasError));
        IsBusy = true;
        Progress = 0;
        foreach (var row in batch) row.Reset();
        RefreshView();
        SetStatus("Processing");
        try
        {
            foreach (var row in batch)
            {
                token.ThrowIfCancellationRequested();
                CurrentFile = row.FileName;
                IsCheckingSignature = false;
                row.Start();
                RefreshAfterStateChange();
                var progress = new Progress<AnalysisProgress>(p =>
                {
                    // Queued Progress<T> callbacks may outlive a completed/cancelled file.
                    if (_disposed || _epoch != epoch || row.State != RowStatus.Running) return;
                    IsCheckingSignature = p.Stage == AnalysisStage.CheckingSignature;
                    var fraction = p.TotalBytes == 0 ? 1.0 : Math.Clamp((double)p.BytesRead / p.TotalBytes, 0, 1);
                    var percent = p.Stage is AnalysisStage.CheckingSignature or AnalysisStage.ReadingMetadata ? 95
                        : fraction * (options.InspectSignature || options.InspectFileProperties || options.InspectPeHeader ? 90 : 100);
                    row.SetProgress(p, percent);
                    Progress = 100.0 * (_processed + percent / 100.0) / _batchTotal;
                });
#pragma warning disable CA1031 // Do not catch general exception types // we report and display the job error
                try
                {
                    // Offload both CPU hashing and synchronous WinVerifyTrust.
                    var result = await Task.Run(() => _analysis.AnalyzeAsync(row.FullPath, options, progress, token), token);
                    row.Complete(result);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { row.Cancel(); throw; }
                catch (Exception ex)
                {
                    // An unexpected per-file failure must not discard the rest of the batch.
                    row.Complete(FileAnalysisResult.Failure(row.FullPath, AnalysisError.AnalysisFailure, ex.Message));
                }
#pragma warning restore CA1031 // Do not catch general exception types
                _processed++;
                Progress = 100.0 * _processed / _batchTotal;
                OnPropertyChanged(nameof(StatusText));
                RefreshAfterStateChange();
            }
            SetStatus("CompletedSummary");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            foreach (var row in batch.Where(r => r.State is RowStatus.Queued or RowStatus.Running)) row.Cancel();
            SetStatus("CancelledSummary");
        }
        finally
        {
            ++_epoch;
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
            IsCheckingSignature = false;
            CurrentFile = "";
            if (_clearRequested) ClearRows();
            RefreshView();
            NotifyCommands();
        }
    }

    private void Cancel()
    {
        _cancellation?.Cancel();
        SetStatus("Cancelling");
        NotifyCommands();
    }

    private void Clear()
    {
        if (IsBusy)
        {
            _clearRequested = true;
            _cancellation?.Cancel();
            SetStatus("Clearing");
            NotifyCommands();
        }
        else
        {
            ClearRows();
        }
    }

    private void ClearRows()
    {
        ++_epoch;
        foreach (var row in Rows) UnsubscribeRow(row);
        _multipleSignatures.Clear();
        OnPropertyChanged(nameof(ShowSignatureSelectorColumn));
        Rows.Clear();
        _paths.Clear();
        SelectedRow = null;
        _clearRequested = false;
        _processed = _batchTotal = _skipped = 0;
        Progress = 0;
        LastError = "";
        OnPropertyChanged(nameof(HasError));
        ResetFilters();
        SetStatus("Cleared");
        NotifyRowsChanged();
        Options.RequestColumnSizing();
    }

    private void OnSignaturesChanged(object? sender, EventArgs args)
    {
        if (sender is not FileRowViewModel row) return;
        var wasVisible = ShowSignatureSelectorColumn;
        if (row.HasMultipleSignatures) _multipleSignatures.Add(row);
        else _multipleSignatures.Remove(row);
        if (wasVisible != ShowSignatureSelectorColumn) OnPropertyChanged(nameof(ShowSignatureSelectorColumn));
    }

    private void OnSignatureSelectionChanged(object? sender, EventArgs args) => RefreshAfterStateChange();

    private void UnsubscribeRow(FileRowViewModel row)
    {
        row.SignaturesChanged -= OnSignaturesChanged;
        row.SignatureSelectionChanged -= OnSignatureSelectionChanged;
    }

    private bool MatchesFilters(object item)
    {
        if (item is not FileRowViewModel row) return false;
        if (StateFilter.Value is RowStatus status && row.State != status) return false;
        var matchesSignature = SignatureFilterOption.Value switch
        {
            SignatureFilter.Valid => row.SignatureState == SignatureStatus.Valid,
            SignatureFilter.NoSignature => row.SignatureState == SignatureStatus.NoEmbeddedSignature,
            SignatureFilter.NotChecked => row.SignatureState == SignatureStatus.NotChecked,
            SignatureFilter.Attention => row.SignatureState is not (SignatureStatus.Valid
                or SignatureStatus.NotChecked or SignatureStatus.NoEmbeddedSignature),
            SignatureFilter.All => true,
            _ => true
        };
        return matchesSignature && row.Matches(SearchText.Trim());
    }

    private void RefreshAfterStateChange()
    {
        // Refresh sorted/filtered values after a result arrives. No refresh is
        // needed in the common unfiltered/unsorted view, avoiding repeated scans.
        if (StateFilter.Value is not null || SignatureFilterOption.Value != SignatureFilter.All
            || SearchText.Length != 0 || RowsView.SortDescriptions.Count != 0)
        {
            RefreshView();
        }
        else
        {
            NotifyRowsChanged();
        }
    }

    private void RefreshView() { RowsView.Refresh(); NotifyRowsChanged(); }
    private void ResetFilters()
    {
        _searchText = "";
        _stateFilter = StateFilters[0];
        _signatureFilter = SignatureFilters[0];
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(StateFilter));
        OnPropertyChanged(nameof(SignatureFilterOption));
        RefreshView();
    }
    private void NotifyRowsChanged()
    {
        OnPropertyChanged(nameof(Statistics));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoMatches));
        NotifyCommands();
    }
    private void SetStatus(string key) { StatusText = key; OnPropertyChanged(nameof(StatusText)); }
    private void NotifyCommands()
    {
        BrowseCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
        DropFilesCommand.NotifyCanExecuteChanged();
        ReanalyseCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }
    private void OnCommandError(Exception exception)
    {
        LastError = exception.Message;
        OnPropertyChanged(nameof(HasError));
        SetStatus("UnexpectedError");
    }
    private void OnCultureChanged(object? sender, EventArgs args)
    {
        foreach (var row in Rows) row.RefreshLanguage();
        foreach (var option in StateFilters) option.RefreshLanguage();
        foreach (var option in SignatureFilters) option.RefreshLanguage();
        OnPropertyChanged(nameof(UiLanguage));
        OnPropertyChanged(nameof(StatusText));
        RefreshView();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_epoch;
        _strings.CultureChanged -= OnCultureChanged;
        Options.HashFormatChanged -= OnHashFormatChanged;
        Options.ExportSelectionChanged -= OnExportSelectionChanged;
        foreach (var row in Rows) UnsubscribeRow(row);
        Options.Dispose();
        foreach (var option in ChecksumOptions) option.PropertyChanged -= OnChecksumOptionChanged;
        _cancellation?.Cancel();
    }
}
