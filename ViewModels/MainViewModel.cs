using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UsbI2cController.Services;
using UsbI2cController.Models;
using UsbI2cController.Converters;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace UsbI2cController.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly FT232HI2CService _i2cService;
        private readonly I2CCommandExecutor _commandExecutor;

        [ObservableProperty]
        private string _deviceStatus = "";

        [ObservableProperty]
        private string _i2cAddress = "0x50";

        [ObservableProperty]
        private string _memoryAddress = "0x00";

        [ObservableProperty]
        private bool _useMemoryAddress = true;

        [ObservableProperty]
        private string _writeData = "";

        [ObservableProperty]
        private string _readLength = "16";

        [ObservableProperty]
        private string _readData = "";

        [ObservableProperty]
        private bool _isHexMode = true;

        [ObservableProperty]
        private bool _isDecMode = false;

        [ObservableProperty]
        private bool _isConnected = false;

        [ObservableProperty]
        private string _statusMessage = "";

        [ObservableProperty]
        private DeviceInfo? _selectedDevice;

        [ObservableProperty]
        private bool _isScanning = false;

        [ObservableProperty]
        private int _scanProgress = 0;

        [ObservableProperty]
        private string _selectedI2CAddress = "";

        [ObservableProperty]
        private string _debugLog = "";

        [ObservableProperty]
        private bool _isDebugLogVisible = false;

        [ObservableProperty]
        private bool _isClockSpeed100kHz = true;

        [ObservableProperty]
        private bool _isClockSpeed400kHz = false;

        [ObservableProperty]
        private bool _isClockEdgeNegative = true;

        [ObservableProperty]
        private string _currentLanguage = "ja";

        [ObservableProperty]
        private bool _isClockEdgePositive = false;

        public ObservableCollection<I2CTransaction> TransactionHistory { get; } = new ObservableCollection<I2CTransaction>();
        
        public ObservableCollection<DeviceInfo> AvailableDevices { get; } = new ObservableCollection<DeviceInfo>();
        
        public ObservableCollection<string> FoundI2CAddresses { get; } = new ObservableCollection<string>();
        
        // 入力履歴
        public ObservableCollection<string> WriteDataHistory { get; } = new ObservableCollection<string>();
        
        public ObservableCollection<string> MemoryAddressHistory { get; } = new ObservableCollection<string>();
        
        public ObservableCollection<string> ReadLengthHistory { get; } = new ObservableCollection<string>();

        // コマンド操作リスト
        public ObservableCollection<CommandOperationViewModel> CommandOperations { get; } = new ObservableCollection<CommandOperationViewModel>();

        // 操作リストが空かどうか
        public bool HasNoOperations => CommandOperations.Count == 0;

        // 履歴保存用ファイルパス
        private static readonly string HistoryFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UsbI2cController",
            "input_history.json");

        // コマンドシーケンス保存用ファイルパス
        private static readonly string CommandSequenceFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UsbI2cController",
            "command_sequence.json");

        // 言語設定保存用ファイルパス
        private static readonly string LanguageSettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UsbI2cController",
            "language_settings.json");

        public MainViewModel()
        {
            _i2cService = new FT232HI2CService();
            _commandExecutor = new I2CCommandExecutor(_i2cService);
            _i2cService.DebugLog += OnDebugLog;
            LoadLanguageSettings();
            DeviceStatus = GetString("DeviceStatusNotConnected");
            LoadHistory();
            LoadCommandSequence();
            RefreshDeviceList();
            UpdateStatusMessage("StatusReady");
            
            // コマンド操作リストの変更を監視
            CommandOperations.CollectionChanged += (s, e) => 
            {
                OnPropertyChanged(nameof(HasNoOperations));
                SaveCommandSequence(); // 変更時に自動保存
            };
        }

        private void OnDebugLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DebugLog += message + "\n";
                // ログが長くなりすぎないように制限（最新の10000文字のみ保持）
                if (DebugLog.Length > 10000)
                {
                    DebugLog = DebugLog.Substring(DebugLog.Length - 10000);
                }
            });
        }

        [RelayCommand]
        private void ClearLog()
        {
            DebugLog = "";
        }

        [RelayCommand]
        private void ToggleDebugLog()
        {
            IsDebugLogVisible = !IsDebugLogVisible;
            UpdateStatusMessage(IsDebugLogVisible ? "StatusDebugLogVisible" : "StatusDebugLogHidden");
        }

        [RelayCommand]
        private void TestGPIO()
        {
            if (!IsConnected)
            {
                MessageBox.Show("デバイスに接続してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // デバッグログを表示
            IsDebugLogVisible = true;
            
            string result = _i2cService.TestGPIORead();
            DebugLog += $"\n=== GPIO Test ===\n{result}\n";
        }

        [RelayCommand]
        private void SetClockSpeed100kHz()
        {
            if (!IsConnected)
            {
                MessageBox.Show("デバイスに接続してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_i2cService.SetI2CClockSpeed(FT232HI2CService.I2CClockSpeed.Standard_100kHz))
            {
                IsClockSpeed100kHz = true;
                IsClockSpeed400kHz = false;
                UpdateStatusMessage("StatusClockSpeed100kHz");
            }
        }

        [RelayCommand]
        private void SetClockSpeed400kHz()
        {
            if (!IsConnected)
            {
                MessageBox.Show("デバイスに接続してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_i2cService.SetI2CClockSpeed(FT232HI2CService.I2CClockSpeed.Fast_400kHz))
            {
                IsClockSpeed100kHz = false;
                IsClockSpeed400kHz = true;
                UpdateStatusMessage("StatusClockSpeed400kHz");
            }
        }

        [RelayCommand]
        private void SetClockEdgeNegative()
        {
            if (!IsConnected)
            {
                MessageBox.Show("デバイスに接続してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _i2cService.SetI2CClockEdge(FT232HI2CService.I2CClockEdge.NegativeEdge);
            IsClockEdgeNegative = true;
            IsClockEdgePositive = false;
            UpdateStatusMessage("StatusClockEdgeNegative");
        }

        [RelayCommand]
        private void SetClockEdgePositive()
        {
            if (!IsConnected)
            {
                MessageBox.Show("デバイスに接続してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _i2cService.SetI2CClockEdge(FT232HI2CService.I2CClockEdge.PositiveEdge);
            IsClockEdgeNegative = false;
            IsClockEdgePositive = true;
            UpdateStatusMessage("StatusClockEdgePositive");
        }

        [RelayCommand]
        private void RefreshDeviceList()
        {
            try
            {
                AvailableDevices.Clear();
                var devices = _i2cService.GetAvailableDevices();
                
                foreach (var device in devices)
                {
                    AvailableDevices.Add(device);
                }

                if (AvailableDevices.Count > 0)
                {
                    SelectedDevice = AvailableDevices[0];
                    DeviceStatus = string.Format(GetString("DeviceStatusDetected"), AvailableDevices.Count);
                    UpdateStatusMessage("StatusDevicesFound", AvailableDevices.Count);
                }
                else
                {
                    SelectedDevice = null;
                    DeviceStatus = GetString("DeviceStatusNone");
                    UpdateStatusMessage("StatusNoDeviceFound");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage("StatusDeviceSearchError", ex.Message);
            }
        }

        [RelayCommand]
        private void Connect()
        {
            try
            {
                if (SelectedDevice == null)
                {
                    MessageBox.Show("デバイスを選択してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_i2cService.Initialize(SelectedDevice.Index))
                {
                    IsConnected = true;
                    DeviceStatus = string.Format(GetString("DeviceStatusConnected"), SelectedDevice.Index);
                    UpdateStatusMessage("StatusConnected", SelectedDevice.Index, SelectedDevice.SerialNumber);
                }
                else
                {
                    IsConnected = false;
                    DeviceStatus = GetString("DeviceStatusConnectFailed");
                    UpdateStatusMessage("StatusInitFailed");
                    MessageBox.Show("FT232Hデバイスの初期化に失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                DeviceStatus = GetString("DeviceStatusError");
                UpdateStatusMessage("StatusConnectionError", ex.Message);
                MessageBox.Show($"接続エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void Disconnect()
        {
            _i2cService.Dispose();
            IsConnected = false;
            DeviceStatus = GetString("DeviceStatusDisconnected");
            UpdateStatusMessage("StatusDisconnected");
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task ScanI2CAddresses()
        {
            if (!IsConnected)
            {
                MessageBox.Show("デバイスが接続されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsScanning)
            {
                return;
            }

            IsScanning = true;
            FoundI2CAddresses.Clear();
            UpdateStatusMessage("StatusScanning");

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var foundAddresses = _i2cService.ScanI2CBus(0x03, 0x77, (current, total) =>
                    {
                        ScanProgress = (int)((current / (double)total) * 100);
                    });

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var address in foundAddresses)
                        {
                            FoundI2CAddresses.Add($"0x{address:X2}");
                        }

                        if (foundAddresses.Count > 0)
                        {
                            UpdateStatusMessage("StatusDevicesDetected", foundAddresses.Count);
                        }
                        else
                        {
                            UpdateStatusMessage("StatusNoDeviceDetected");
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateStatusMessage("StatusScanError", ex.Message);
                        MessageBox.Show($"I2Cスキャンエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsScanning = false;
                        ScanProgress = 0;
                    });
                }
            });
        }

        [RelayCommand]
        private void SelectI2CAddress(string address)
        {
            if (!string.IsNullOrEmpty(address))
            {
                I2cAddress = address;
                UpdateStatusMessage("StatusAddressSelected", address);
            }
        }

        [RelayCommand]
        private void WriteI2C()
        {
            if (!IsConnected)
            {
                MessageBox.Show("デバイスが接続されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // I2Cアドレスをパース
                byte address = ParseAddress(I2cAddress);

                // メモリアドレスをパース
                byte memAddr = ParseAddress(MemoryAddress);

                // 送信データをパース
                byte[] userData;
                bool parseSuccess = IsHexMode 
                    ? DataFormatConverter.TryParseHex(WriteData, out userData)
                    : DataFormatConverter.TryParseDec(WriteData, out userData);

                if (!parseSuccess || userData == null || userData.Length == 0)
                {
                    MessageBox.Show("データ形式が正しくありません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // メモリアドレス + データを結合（EEPROM Byte Write/Page Write形式）
                byte[] data = new byte[1 + userData.Length];
                data[0] = memAddr;
                Array.Copy(userData, 0, data, 1, userData.Length);

                // I2C書き込み実行
                bool success = _i2cService.WriteI2C(address, data);

                // 入力履歴に追加
                AddToHistory(WriteDataHistory, WriteData);
                AddToHistory(MemoryAddressHistory, MemoryAddress);

                // トランザクション履歴に追加
                var transaction = new I2CTransaction
                {
                    Timestamp = DateTime.Now,
                    Type = "Write",
                    DeviceAddress = address,
                    Data = data,
                    Success = success
                };
                TransactionHistory.Insert(0, transaction);

                // 履歴を最大100件に制限
                while (TransactionHistory.Count > 100)
                {
                    TransactionHistory.RemoveAt(TransactionHistory.Count - 1);
                }

                if (success)
                {
                    StatusMessage = $"書き込み成功: {data.Length}バイト @ 0x{address:X2}";
                }
                else
                {
                    StatusMessage = "書き込み失敗";
                    MessageBox.Show("I2C書き込みに失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"書き込みエラー: {ex.Message}";
                MessageBox.Show($"書き込みエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void ReadI2C()
        {
            if (!IsConnected)
            {
                MessageBox.Show("デバイスが接続されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // I2Cアドレスをパース
                byte address = ParseAddress(I2cAddress);

                // 読み込みバイト数をパース
                if (!int.TryParse(ReadLength, out int length) || length <= 0 || length > 256)
                {
                    MessageBox.Show("読み込みバイト数は1～256の範囲で指定してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // メモリアドレスをパース
                byte memAddr = ParseAddress(MemoryAddress);

                // I2C読み込み実行（Random Read / Sequential Read）
                byte[] data;
                bool success = _i2cService.ReadI2CWithAddress(address, memAddr, length, out data);

                // 入力履歴に追加
                AddToHistory(MemoryAddressHistory, MemoryAddress);
                AddToHistory(ReadLengthHistory, ReadLength);

                // トランザクション履歴に追加
                var transaction = new I2CTransaction
                {
                    Timestamp = DateTime.Now,
                    Type = $"Read@0x{memAddr:X2}",
                    DeviceAddress = address,
                    Data = data ?? new byte[0],
                    Success = success
                };
                TransactionHistory.Insert(0, transaction);

                // 履歴を最大100件に制限
                while (TransactionHistory.Count > 100)
                {
                    TransactionHistory.RemoveAt(TransactionHistory.Count - 1);
                }

                if (success && data != null)
                {
                    // 読み込んだデータを表示
                    ReadData = IsHexMode 
                        ? DataFormatConverter.ToHexString(data, true)
                        : DataFormatConverter.ToDecString(data);
                    
                    StatusMessage = $"読み込み成功: {data.Length}バイト @ 0x{address:X2}";
                }
                else
                {
                    ReadData = "";
                    StatusMessage = "読み込み失敗";
                    MessageBox.Show("I2C読み込みに失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                ReadData = "";
                StatusMessage = $"読み込みエラー: {ex.Message}";
                MessageBox.Show($"読み込みエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void ClearHistory()
        {
            TransactionHistory.Clear();
            StatusMessage = "履歴をクリアしました";
        }

        [RelayCommand]
        private void SwitchToHex()
        {
            if (!IsHexMode)
            {
                IsHexMode = true;
                IsDecMode = false;
                ConvertDisplayedData();
            }
        }

        [RelayCommand]
        private void SwitchToDec()
        {
            if (!IsDecMode)
            {
                IsHexMode = false;
                IsDecMode = true;
                ConvertDisplayedData();
            }
        }

        private void ConvertDisplayedData()
        {
            // 読み込みデータの表示形式を変換
            if (!string.IsNullOrWhiteSpace(ReadData))
            {
                byte[] data;
                bool parsed = IsHexMode
                    ? DataFormatConverter.TryParseDec(ReadData, out data)
                    : DataFormatConverter.TryParseHex(ReadData, out data);

                if (parsed && data != null)
                {
                    ReadData = IsHexMode
                        ? DataFormatConverter.ToHexString(data, true)
                        : DataFormatConverter.ToDecString(data);
                }
            }
        }

        private byte ParseAddress(string addressStr)
        {
            addressStr = addressStr.Trim().Replace("0x", "").Replace("0X", "");
            
            if (addressStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                addressStr = addressStr.Substring(2);
            }

            // HEX形式として解析を試みる
            if (byte.TryParse(addressStr, System.Globalization.NumberStyles.HexNumber, null, out byte hexResult))
            {
                return hexResult;
            }

            // 10進数として解析を試みる
            if (byte.TryParse(addressStr, out byte decResult))
            {
                return decResult;
            }

            throw new FormatException($"無効なI2Cアドレス: {I2cAddress}");
        }

        /// <summary>
        /// 入力履歴に追加（重複排除、最大20件）
        /// </summary>
        private void AddToHistory(ObservableCollection<string> history, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            // 既存の同じ値を削除（最新を上に）
            var existing = history.FirstOrDefault(h => h.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                history.Remove(existing);
            }

            // 先頭に追加
            history.Insert(0, value);

            // 最大20件に制限
            while (history.Count > 20)
            {
                history.RemoveAt(history.Count - 1);
            }

            // 履歴を保存
            SaveHistory();
        }

        /// <summary>
        /// 入力履歴をファイルに保存
        /// </summary>
        private void SaveHistory()
        {
            try
            {
                var historyData = new
                {
                    WriteData = WriteDataHistory.ToList(),
                    MemoryAddress = MemoryAddressHistory.ToList(),
                    ReadLength = ReadLengthHistory.ToList()
                };

                var directory = Path.GetDirectoryName(HistoryFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(historyData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(HistoryFilePath, json);
            }
            catch (Exception ex)
            {
                // 保存失敗してもアプリを続行
                System.Diagnostics.Debug.WriteLine($"Failed to save history: {ex.Message}");
            }
        }

        /// <summary>
        /// 入力履歴をファイルから読み込み
        /// </summary>
        private void LoadHistory()
        {
            try
            {
                if (!File.Exists(HistoryFilePath))
                {
                    return;
                }

                var json = File.ReadAllText(HistoryFilePath);
                var historyData = JsonSerializer.Deserialize<InputHistory>(json);

                if (historyData != null)
                {
                    // WriteData履歴
                    WriteDataHistory.Clear();
                    if (historyData.WriteData != null)
                    {
                        foreach (var item in historyData.WriteData)
                        {
                            WriteDataHistory.Add(item);
                        }
                    }

                    // MemoryAddress履歴
                    MemoryAddressHistory.Clear();
                    if (historyData.MemoryAddress != null)
                    {
                        foreach (var item in historyData.MemoryAddress)
                        {
                            MemoryAddressHistory.Add(item);
                        }
                    }

                    // ReadLength履歴
                    ReadLengthHistory.Clear();
                    if (historyData.ReadLength != null)
                    {
                        foreach (var item in historyData.ReadLength)
                        {
                            ReadLengthHistory.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 読み込み失敗してもアプリを続行
                System.Diagnostics.Debug.WriteLine($"Failed to load history: {ex.Message}");
            }
        }

        /// <summary>
        /// 履歴データ用クラス
        /// </summary>
        private class InputHistory
        {
            public List<string>? WriteData { get; set; }
            public List<string>? MemoryAddress { get; set; }
            public List<string>? ReadLength { get; set; }
        }

        /// <summary>
        /// コマンドシーケンスをファイルに保存
        /// </summary>
        private void SaveCommandSequence()
        {
            try
            {
                if (CommandOperations.Count == 0)
                {
                    // 操作がない場合は保存ファイルを削除
                    try
                    {
                        if (File.Exists(CommandSequenceFilePath))
                        {
                            File.Delete(CommandSequenceFilePath);
                        }
                    }
                    catch { }
                    return;
                }

                var sequenceData = new CommandSequenceData
                {
                    I2CAddress = I2cAddress,
                    IsHexMode = IsHexMode,
                    Operations = CommandOperations.Select(op => new OperationData
                    {
                        Type = op.Type.ToString(),
                        WriteDataInput = op.WriteDataInput,
                        ReadLengthInput = op.ReadLengthInput,
                        Index = op.Index,
                        Comment = op.Comment
                    }).ToList()
                };

                var directory = Path.GetDirectoryName(CommandSequenceFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(sequenceData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(CommandSequenceFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save command sequence: {ex.Message}");
            }
        }

        /// <summary>
        /// コマンドシーケンスをファイルから読み込み
        /// </summary>
        private void LoadCommandSequence()
        {
            try
            {
                if (!File.Exists(CommandSequenceFilePath))
                {
                    return;
                }

                var json = File.ReadAllText(CommandSequenceFilePath);
                var sequenceData = JsonSerializer.Deserialize<CommandSequenceData>(json);

                if (sequenceData != null)
                {
                    // I2Cアドレス復元
                    if (!string.IsNullOrEmpty(sequenceData.I2CAddress))
                    {
                        I2cAddress = sequenceData.I2CAddress;
                    }

                    // HEX/DECモード復元
                    IsHexMode = sequenceData.IsHexMode;
                    IsDecMode = !sequenceData.IsHexMode;

                    // 操作リスト復元
                    CommandOperations.Clear();
                    if (sequenceData.Operations != null)
                    {
                        foreach (var opData in sequenceData.Operations.OrderBy(o => o.Index))
                        {
                            if (Enum.TryParse<I2COperationType>(opData.Type, out var opType))
                            {
                                var op = new CommandOperationViewModel
                                {
                                    Index = opData.Index,
                                    Type = opType,
                                    WriteDataInput = opData.WriteDataInput ?? "",
                                    ReadLengthInput = opData.ReadLengthInput ?? "",
                                    IsHexMode = IsHexMode,
                                    Comment = opData.Comment ?? ""
                                };

                                // 入力をパース
                                if (op.ParseInputs())
                                {
                                    CommandOperations.Add(op);
                                }
                            }
                        }
                    }

                    if (CommandOperations.Count > 0)
                    {
                        StatusMessage = $"前回のコマンドシーケンスを復元しました ({CommandOperations.Count} 操作)";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load command sequence: {ex.Message}");
            }
        }



        /// <summary>
        /// コマンドシーケンスデータ用クラス
        /// </summary>
        private class CommandSequenceData
        {
            public string I2CAddress { get; set; } = "";
            public bool IsHexMode { get; set; } = true;
            public List<OperationData>? Operations { get; set; }
        }

        /// <summary>
        /// 操作データ用クラス
        /// </summary>
        private class OperationData
        {
            public string Type { get; set; } = "";
            public string? WriteDataInput { get; set; }
            public string? ReadLengthInput { get; set; }
            public int Index { get; set; }
            public string Comment { get; set; } = "";
        }

        // ========== コマンドシーケンス関連 ==========

        [RelayCommand]
        private void AddWriteOperation()
        {
            // ダイアログでWrite データを入力
            var dialog = new InputDialog(GetString("WriteDataInput"), GetString("WriteDataPrompt"), "");
            if (dialog.ShowDialog() == true)
            {
                var op = new CommandOperationViewModel
                {
                    Index = CommandOperations.Count + 1,
                    Type = I2COperationType.Write,
                    WriteDataInput = dialog.InputText,
                    IsHexMode = IsHexMode
                };

                if (op.ParseInputs())
                {
                    CommandOperations.Add(op);
                    StatusMessage = $"Write 操作を追加しました ({op.WriteData?.Length ?? 0} bytes)";
                }
                else
                {
                    MessageBox.Show("データ形式が正しくありません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        [RelayCommand]
        private void AddReadOperation()
        {
            // ダイアログでRead バイト数を入力
            var dialog = new InputDialog(GetString("ReadLengthInput"), GetString("ReadLengthPrompt"), "16");
            if (dialog.ShowDialog() == true)
            {
                var op = new CommandOperationViewModel
                {
                    Index = CommandOperations.Count + 1,
                    Type = I2COperationType.Read,
                    ReadLengthInput = dialog.InputText,
                    IsHexMode = IsHexMode
                };

                if (op.ParseInputs())
                {
                    CommandOperations.Add(op);
                    StatusMessage = $"Read 操作を追加しました ({op.ReadLength} bytes)";
                }
                else
                {
                    MessageBox.Show("バイト数は1～256の範囲で指定してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        [RelayCommand]
        private void EditOperation(CommandOperationViewModel operation)
        {
            if (operation == null) return;

            var dialog = new Views.EditOperationDialog
            {
                Owner = Application.Current.MainWindow,
                CommentHint = GetString("CommentPrompt"),
                CommentText = operation.Comment
            };

            if (operation.Type == I2COperationType.Write)
            {
                dialog.Title = GetString("WriteDataInput");
                dialog.Message = GetString("EditOperationMessage");
                dialog.InputHint = GetString("WriteDataPrompt");
                dialog.InputText = operation.WriteDataInput;
                dialog.ShowDataInput = true;

                if (dialog.ShowDialog() == true)
                {
                    operation.WriteDataInput = dialog.InputText;
                    operation.Comment = dialog.CommentText ?? "";
                    if (operation.ParseInputs())
                    {
                        operation.NotifyPropertyChanged(nameof(operation.Description));
                        operation.NotifyPropertyChanged(nameof(operation.Comment));
                        SaveCommandSequence();
                        UpdateStatusMessage("StatusOperationEdited");
                    }
                }
            }
            else if (operation.Type == I2COperationType.Read)
            {
                dialog.Title = GetString("ReadLengthInput");
                dialog.Message = GetString("EditOperationMessage");
                dialog.InputHint = GetString("ReadLengthPrompt");
                dialog.InputText = operation.ReadLengthInput;
                dialog.ShowDataInput = true;

                if (dialog.ShowDialog() == true)
                {
                    operation.ReadLengthInput = dialog.InputText;
                    operation.Comment = dialog.CommentText ?? "";
                    if (operation.ParseInputs())
                    {
                        operation.NotifyPropertyChanged(nameof(operation.Description));
                        operation.NotifyPropertyChanged(nameof(operation.Comment));
                        SaveCommandSequence();
                        UpdateStatusMessage("StatusOperationEdited");
                    }
                }
            }
            else
            {
                // START, STOP等はコメントのみ編集
                dialog.Title = GetString("CommentInput");
                dialog.Message = GetString("EditOperationMessage");
                dialog.ShowDataInput = false;

                if (dialog.ShowDialog() == true)
                {
                    operation.Comment = dialog.CommentText ?? "";
                    operation.NotifyPropertyChanged(nameof(operation.Comment));
                    SaveCommandSequence();
                    UpdateStatusMessage("StatusOperationEdited");
                }
            }
        }

        [RelayCommand]
        private void RemoveOperation(CommandOperationViewModel operation)
        {
            if (operation != null)
            {
                CommandOperations.Remove(operation);
                // インデックスを再割り当て
                ReindexOperations();
                StatusMessage = "操作を削除しました";
            }
        }

        [RelayCommand]
        private void MoveUpOperation(CommandOperationViewModel operation)
        {
            if (operation == null) return;

            int currentIndex = CommandOperations.IndexOf(operation);
            if (currentIndex > 0)
            {
                CommandOperations.Move(currentIndex, currentIndex - 1);
                ReindexOperations();
                StatusMessage = "操作を上に移動しました";
            }
        }

        [RelayCommand]
        private void MoveDownOperation(CommandOperationViewModel operation)
        {
            if (operation == null) return;

            int currentIndex = CommandOperations.IndexOf(operation);
            if (currentIndex < CommandOperations.Count - 1)
            {
                CommandOperations.Move(currentIndex, currentIndex + 1);
                ReindexOperations();
                StatusMessage = "操作を下に移動しました";
            }
        }

        [RelayCommand]
        private void ClearOperations()
        {
            CommandOperations.Clear();
            StatusMessage = "すべての操作をクリアしました";
        }

        /// <summary>
        /// 操作リストのインデックスを再割り当て
        /// </summary>
        private void ReindexOperations()
        {
            for (int i = 0; i < CommandOperations.Count; i++)
            {
                CommandOperations[i].Index = i + 1;
            }
        }

        [RelayCommand]
        private void ExecuteSequence()
        {
            if (!IsConnected)
            {
                MessageBox.Show("デバイスが接続されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CommandOperations.Count == 0)
            {
                MessageBox.Show("操作が追加されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // I2Cアドレスをパース
                byte address = ParseAddress(I2cAddress);

                // シーケンスを実行
                bool overallSuccess = true;
                var executedData = new List<byte>();

                for (int i = 0; i < CommandOperations.Count; i++)
                {
                    var op = CommandOperations[i];
                    bool success = false;

                    switch (op.Type)
                    {
                        case I2COperationType.Write:
                            if (op.WriteData != null && op.WriteData.Length > 0)
                            {
                                success = _i2cService.WriteI2C(address, op.WriteData);
                                if (success)
                                {
                                    executedData.AddRange(op.WriteData);
                                }
                            }
                            break;

                        case I2COperationType.Read:
                            if (op.ReadLength > 0)
                            {
                                byte[] data;
                                success = _i2cService.ReadI2C(address, op.ReadLength, out data);
                                if (success && data != null)
                                {
                                    op.ReadResultData = data;
                                    executedData.AddRange(data);
                                    // UIに通知して表示を更新
                                    op.NotifyPropertyChanged(nameof(op.ReadResultData));
                                    op.NotifyPropertyChanged(nameof(op.ReadResultText));
                                    op.NotifyPropertyChanged(nameof(op.HasReadResult));
                                }
                            }
                            break;
                    }

                    if (!success)
                    {
                        overallSuccess = false;
                        MessageBox.Show($"操作 #{op.Index} ({op.TypeDisplay}) の実行に失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        break;
                    }

                    // 各操作の間に2クロック分の待機時間を挿入（最後の操作以外）
                    if (i < CommandOperations.Count - 1)
                    {
                        System.Threading.Thread.Sleep(2); // 2ms待機でバス安定化
                    }

                    // 各操作をトランザクション履歴に個別に追加
                    var transaction = new I2CTransaction
                    {
                        Timestamp = DateTime.Now,
                        Type = op.Type == I2COperationType.Write ? "Write" : "Read",
                        DeviceAddress = address,
                        Data = op.Type == I2COperationType.Write 
                            ? (op.WriteData ?? Array.Empty<byte>())
                            : (op.ReadResultData ?? Array.Empty<byte>()),
                        Success = success
                    };
                    TransactionHistory.Insert(0, transaction);
                }

                // 履歴を最大100件に制限
                while (TransactionHistory.Count > 100)
                {
                    TransactionHistory.RemoveAt(TransactionHistory.Count - 1);
                }

                if (overallSuccess)
                {
                    StatusMessage = $"シーケンス実行成功: {CommandOperations.Count} 操作完了";
                }
                else
                {
                    StatusMessage = "シーケンス実行失敗";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"実行エラー: {ex.Message}";
                MessageBox.Show($"実行エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void SaveSequenceToFile()
        {
            if (CommandOperations.Count == 0)
            {
                MessageBox.Show("保存する操作がありません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "I2C Sequence Files (*.i2cseq)|*.i2cseq|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    DefaultExt = ".i2cseq",
                    Title = "I2Cシーケンスを保存"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var sequenceData = new
                    {
                        I2CAddress = I2cAddress,
                        IsHexMode = IsHexMode,
                        Operations = CommandOperations.Select(op => new
                        {
                            Type = op.Type.ToString(),
                            WriteDataInput = op.WriteDataInput,
                            ReadLengthInput = op.ReadLengthInput,
                            Index = op.Index,
                            Comment = op.Comment
                        }).ToList()
                    };

                    var json = JsonSerializer.Serialize(sequenceData, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    File.WriteAllText(saveFileDialog.FileName, json);
                    StatusMessage = $"シーケンスを保存しました: {Path.GetFileName(saveFileDialog.FileName)}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void LoadSequenceFromFile()
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "I2C Sequence Files (*.i2cseq)|*.i2cseq|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    DefaultExt = ".i2cseq",
                    Title = "I2Cシーケンスを読み込み"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var json = File.ReadAllText(openFileDialog.FileName);
                    var sequenceData = JsonSerializer.Deserialize<CommandSequenceData>(json);

                    if (sequenceData != null)
                    {
                        // 確認ダイアログ
                        if (CommandOperations.Count > 0)
                        {
                            var result = MessageBox.Show(
                                "現在のシーケンスを破棄して読み込みますか？",
                                "確認",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (result != MessageBoxResult.Yes)
                            {
                                return;
                            }
                        }

                        // I2Cアドレス復元
                        if (!string.IsNullOrEmpty(sequenceData.I2CAddress))
                        {
                            I2cAddress = sequenceData.I2CAddress;
                        }

                        // HEX/DECモード復元
                        IsHexMode = sequenceData.IsHexMode;
                        IsDecMode = !sequenceData.IsHexMode;

                        // 操作リスト復元
                        CommandOperations.Clear();
                        if (sequenceData.Operations != null)
                        {
                            foreach (var opData in sequenceData.Operations.OrderBy(o => o.Index))
                            {
                                if (Enum.TryParse<I2COperationType>(opData.Type, out var opType))
                                {
                                    var op = new CommandOperationViewModel
                                    {
                                        Index = opData.Index,
                                        Type = opType,
                                        WriteDataInput = opData.WriteDataInput ?? "",
                                        ReadLengthInput = opData.ReadLengthInput ?? "",
                                        IsHexMode = IsHexMode,
                                        Comment = opData.Comment ?? ""
                                    };

                                    // 入力をパース
                                    if (op.ParseInputs())
                                    {
                                        CommandOperations.Add(op);
                                    }
                                }
                            }
                        }

                        UpdateStatusMessage("StatusSequenceLoaded", Path.GetFileName(openFileDialog.FileName), CommandOperations.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"読み込みエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        partial void OnCurrentLanguageChanged(string value)
        {
            ChangeLanguage(value);
            SaveLanguageSettings();
            
            // コマンド操作の説明を更新
            foreach (var op in CommandOperations)
            {
                op.NotifyPropertyChanged(nameof(op.Description));
            }
            
            // ステータスメッセージを更新
            UpdateStatusMessage("StatusReady");
        }

        private void ChangeLanguage(string languageCode)
        {
            var dict = new ResourceDictionary();
            dict.Source = new Uri($"pack://application:,,,/Resources/Strings.{languageCode}.xaml", UriKind.Absolute);
            
            // 既存の言語リソースを削除
            var oldDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("/Resources/Strings."));
            
            if (oldDict != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(oldDict);
            }
            
            // 新しい言語リソースを追加
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }

        private void LoadLanguageSettings()
        {
            try
            {
                if (File.Exists(LanguageSettingsFilePath))
                {
                    string json = File.ReadAllText(LanguageSettingsFilePath);
                    var settings = JsonSerializer.Deserialize<LanguageSettings>(json);
                    if (settings != null && !string.IsNullOrEmpty(settings.Language))
                    {
                        // プロパティのみ設定（リソースは既にApp.xaml.csで読み込まれている）
                        _currentLanguage = settings.Language;
                        OnPropertyChanged(nameof(CurrentLanguage));
                    }
                }
            }
            catch
            {
                // エラー時は何もしない（デフォルトの"ja"を使用）
            }
        }

        private void SaveLanguageSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(LanguageSettingsFilePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var settings = new LanguageSettings { Language = CurrentLanguage };
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(LanguageSettingsFilePath, json);
            }
            catch
            {
                // エラーは無視
            }
        }

        private string GetString(string key)
        {
            try
            {
                if (Application.Current.Resources[key] is string value)
                {
                    return value;
                }
            }
            catch { }
            return key;
        }

        private void UpdateStatusMessage(string messageKey, params object[] args)
        {
            var format = GetString(messageKey);
            StatusMessage = args.Length > 0 ? string.Format(format, args) : format;
        }
    }

    public class LanguageSettings
    {
        public string Language { get; set; } = "ja";
    }
}
