using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IFolderDialogService _dialog;

    public MainViewModel(IFolderDialogService dialog)
    {
        _dialog = dialog;
        BrowseCommand = new RelayCommand(_ => Browse());
        AnalyzeCommand = new RelayCommand(async _ => await RunAsync(apply: false), _ => IsNotBusy && HasRoot);
        ApplyCommand = new RelayCommand(async _ => await RunAsync(apply: true), _ => IsNotBusy && HasRoot);
        OpenResourceHeaderCommand = new RelayCommand(_ => Open(ResourceHeaderPath), _ => !string.IsNullOrWhiteSpace(ResourceHeaderPath));
        OpenBackupCommand = new RelayCommand(_ => Open(BackupPath), _ => HasBackup);

        // 초기 상태
        StatusMessage = "준비됨";
    }

    #region Bindable Properties
    private string _rootPath = "";
    public string RootPath { get => _rootPath; set { _rootPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRoot)); CommandRefresh(); } }
    public bool HasRoot => !string.IsNullOrWhiteSpace(RootPath);

    private string _resourceHeaderPath = "";
    public string ResourceHeaderPath { get => _resourceHeaderPath; set { _resourceHeaderPath = value; OnPropertyChanged(); } }

    private int _totalDefines;
    public int TotalDefines { get => _totalDefines; set { _totalDefines = value; OnPropertyChanged(); } }

    private int _removedDefines;
    public int RemovedDefines { get => _removedDefines; set { _removedDefines = value; OnPropertyChanged(); } }

    private int _keptDefines;
    public int KeptDefines { get => _keptDefines; set { _keptDefines = value; OnPropertyChanged(); } }

    private string _backupPath = "";
    public string BackupPath { get => _backupPath; set { _backupPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBackup)); CommandRefresh(); } }
    public bool HasBackup => !string.IsNullOrWhiteSpace(BackupPath);

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); CommandRefresh(); } }
    public bool IsNotBusy => !IsBusy;

    public ObservableCollection<DefineBeforeRow> DefinesBefore { get; } = new();
    public ObservableCollection<DefineAfterRow> DefinesAfter { get; } = new();
    #endregion

    #region Commands
    public RelayCommand BrowseCommand { get; }
    public RelayCommand AnalyzeCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand OpenResourceHeaderCommand { get; }
    public RelayCommand OpenBackupCommand { get; }
    #endregion

    private void Browse()
    {
        var path = _dialog.BrowseForFolder("리소스가 있는 루트 폴더를 선택하세요.");
        if (!string.IsNullOrWhiteSpace(path))
        {
            RootPath = path;
            StatusMessage = "폴더 선택됨";
        }
    }

    private async Task RunAsync(bool apply)
    {
        try
        {
            IsBusy = true;
            StatusMessage = apply ? "적용 중..." : "분석 중...";
            DefinesBefore.Clear();
            DefinesAfter.Clear();
            BackupPath = "";
            ResourceHeaderPath = "";
            TotalDefines = RemovedDefines = KeptDefines = 0;

            var result = await Task.Run(() =>
                ResourceHeaderTool.CleanAndRenumber(RootPath, resourceHeaderPath: null, apply: apply));

            ResourceHeaderPath = result.ResourceHeaderPath;
            TotalDefines = result.TotalDefines;
            RemovedDefines = result.RemovedDefines;
            KeptDefines = result.KeptDefines;
            BackupPath = result.BackupPath ?? "";

            foreach (var b in result.Before)
                DefinesBefore.Add(new DefineBeforeRow { Name = b.Name, OldValue = b.OldValue, Used = b.Used });

            foreach (var a in result.After)
                DefinesAfter.Add(new DefineAfterRow { Name = a.Name, NewValue = a.NewValue });

            StatusMessage = apply ? "적용 완료" : "분석 완료";
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void Open(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* 무시 */ }
    }

    private void CommandRefresh()
    {
        AnalyzeCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
        OpenBackupCommand.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class DefineBeforeRow
{
    public string Name { get; set; } = "";
    public int OldValue { get; set; }
    public bool Used { get; set; }
}

public class DefineAfterRow
{
    public string Name { get; set; } = "";
    public int NewValue { get; set; }
}

public class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute; _canExecute = canExecute;
    }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
