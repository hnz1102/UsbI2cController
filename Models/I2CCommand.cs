using System;
using System.Collections.Generic;

namespace UsbI2cController.Models
{
    /// <summary>
    /// I2Cコマンド操作の種類
    /// </summary>
    public enum I2COperationType
    {
        Write,          // データ書き込み
        Read,           // データ読み込み
        Start,          // START条件（通常は自動）
        RepeatedStart,  // Repeated START条件
        Stop,           // STOP条件（通常は自動）
        Delay           // 待機時間（ミリ秒）
    }

    /// <summary>
    /// I2Cコマンドシーケンスの1つの操作
    /// </summary>
    public class I2COperation
    {
        /// <summary>
        /// 操作タイプ
        /// </summary>
        public I2COperationType Type { get; set; }

        /// <summary>
        /// 書き込みデータ（Write時のみ）
        /// </summary>
        public byte[]? WriteData { get; set; }

        /// <summary>
        /// 読み込みバイト数（Read時のみ）
        /// </summary>
        public int ReadLength { get; set; }

        /// <summary>
        /// 読み込みデータ結果（Read実行後）
        /// </summary>
        public byte[]? ReadData { get; set; }

        /// <summary>
        /// 待機時間（Delay時のみ、ミリ秒）
        /// </summary>
        public int DelayMilliseconds { get; set; }

    /// <summary>
    /// 説明
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// コメント
    /// </summary>
    public string Comment { get; set; } = string.Empty;        public override string ToString()
        {
            return Type switch
            {
                I2COperationType.Write => $"Write: {(WriteData != null ? BitConverter.ToString(WriteData).Replace("-", " ") : "0 bytes")}",
                I2COperationType.Read => $"Read: {ReadLength} bytes",
                I2COperationType.Start => "START",
                I2COperationType.RepeatedStart => "Repeated START",
                I2COperationType.Stop => "STOP",
                I2COperationType.Delay => $"Delay: {DelayMilliseconds} ms",
                _ => Type.ToString()
            };
        }
    }

    /// <summary>
    /// I2Cコマンドシーケンス（START～STOPまでの一連の操作）
    /// </summary>
    public class I2CCommandSequence
    {
        /// <summary>
        /// I2Cデバイスアドレス（7ビット）
        /// </summary>
        public byte DeviceAddress { get; set; }

        /// <summary>
        /// 操作のリスト
        /// </summary>
        public List<I2COperation> Operations { get; set; } = new List<I2COperation>();

        /// <summary>
        /// シーケンス全体の説明
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 実行結果
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// タイムスタンプ
        /// </summary>
        public DateTime Timestamp { get; set; }

        public override string ToString()
        {
            return $"0x{DeviceAddress:X2} - {Operations.Count} operations: {Description}";
        }
    }
}
