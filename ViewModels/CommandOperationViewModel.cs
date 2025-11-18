using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UsbI2cController.Models;
using UsbI2cController.Converters;

namespace UsbI2cController.ViewModels
{
    /// <summary>
    /// コマンド操作のViewModel
    /// </summary>
    public partial class CommandOperationViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _index;

        [ObservableProperty]
        private I2COperationType _type;

        [ObservableProperty]
        private string _writeDataInput = "";

        [ObservableProperty]
        private string _readLengthInput = "";

        [ObservableProperty]
        private bool _isHexMode = true;

    [ObservableProperty]
    private byte[]? _readResultData;

    [ObservableProperty]
    private string _comment = "";

    /// <summary>
    /// 実際の書き込みデータ（パース済み）
    /// </summary>
    public byte[]? WriteData { get; set; }        /// <summary>
        /// 読み込みバイト数
        /// </summary>
        public int ReadLength { get; set; }

        /// <summary>
        /// タイプの表示名
        /// </summary>
        public string TypeDisplay => Type switch
        {
            I2COperationType.Write => "Write",
            I2COperationType.Read => "Read",
            I2COperationType.Start => "START",
            I2COperationType.RepeatedStart => "Rpt START",
            I2COperationType.Stop => "STOP",
            _ => Type.ToString()
        };

        /// <summary>
        /// 説明テキスト
        /// </summary>
        public string Description
        {
            get
            {
                var isJapanese = System.Windows.Application.Current.Resources.MergedDictionaries
                    .Any(d => d.Source != null && d.Source.OriginalString.Contains("Strings.ja"));
                
                return Type switch
                {
                    I2COperationType.Write => WriteData != null && WriteData.Length > 0
                        ? (isJapanese 
                            ? $"データ: {(IsHexMode ? DataFormatConverter.ToHexString(WriteData, true) : DataFormatConverter.ToDecString(WriteData))} ({WriteData.Length} bytes)"
                            : $"Data: {(IsHexMode ? DataFormatConverter.ToHexString(WriteData, true) : DataFormatConverter.ToDecString(WriteData))} ({WriteData.Length} bytes)")
                        : (isJapanese ? "データなし" : "No data"),
                    I2COperationType.Read => isJapanese 
                        ? $"{ReadLength} バイト読み込み" 
                        : $"Read {ReadLength} bytes",
                    I2COperationType.Start => isJapanese ? "START条件を送信" : "Send START condition",
                    I2COperationType.RepeatedStart => isJapanese ? "Repeated START条件を送信" : "Send Repeated START condition",
                    I2COperationType.Stop => isJapanese ? "STOP条件を送信" : "Send STOP condition",
                    _ => ""
                };
            }
        }

        /// <summary>
        /// 読み込み結果の表示テキスト
        /// </summary>
        public string ReadResultText
        {
            get
            {
                if (Type != I2COperationType.Read || ReadResultData == null || ReadResultData.Length == 0)
                {
                    return "";
                }
                return IsHexMode 
                    ? DataFormatConverter.ToHexString(ReadResultData, true) 
                    : DataFormatConverter.ToDecString(ReadResultData);
            }
        }

        /// <summary>
        /// 読み込み結果があるか
        /// </summary>
        public bool HasReadResult => Type == I2COperationType.Read && ReadResultData != null && ReadResultData.Length > 0;

        /// <summary>
        /// 入力を解析して内部データを更新
        /// </summary>
        public bool ParseInputs()
        {
            try
            {
                switch (Type)
                {
                    case I2COperationType.Write:
                        if (string.IsNullOrWhiteSpace(WriteDataInput))
                        {
                            return false;
                        }
                        byte[] data;
                        bool parsed = IsHexMode
                            ? DataFormatConverter.TryParseHex(WriteDataInput, out data)
                            : DataFormatConverter.TryParseDec(WriteDataInput, out data);
                        if (!parsed || data == null || data.Length == 0)
                        {
                            return false;
                        }
                        WriteData = data;
                        return true;

                    case I2COperationType.Read:
                        if (!int.TryParse(ReadLengthInput, out int length) || length <= 0 || length > 256)
                        {
                            return false;
                        }
                        ReadLength = length;
                        return true;

                    case I2COperationType.Start:
                    case I2COperationType.RepeatedStart:
                    case I2COperationType.Stop:
                        return true;

                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// I2COperationモデルに変換
        /// </summary>
        public I2COperation ToModel()
        {
            return new I2COperation
            {
                Type = Type,
                WriteData = WriteData,
                ReadLength = ReadLength,
                ReadData = ReadResultData,
                Description = Description,
                Comment = Comment
            };
        }

        /// <summary>
        /// プロパティ変更を通知（外部から呼び出し可能）
        /// </summary>
        public void NotifyPropertyChanged(string propertyName)
        {
            OnPropertyChanged(propertyName);
        }
    }
}
