using System;
using System.Collections.Generic;
using System.Linq;

namespace UsbI2cController.Converters
{
    public static class DataFormatConverter
    {
        /// <summary>
        /// HEX文字列をバイト配列に変換（スペースまたはカンマ区切り対応）
        /// 例: "0x1A 0x2B 0x3C" or "1A 2B 3C" or "1A,2B,3C"
        /// </summary>
        public static bool TryParseHex(string input, out byte[] result)
        {
            result = Array.Empty<byte>();
            
            if (string.IsNullOrWhiteSpace(input))
                return false;

            try
            {
                // スペースとカンマで分割
                var parts = input.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                List<byte> bytes = new List<byte>();

                foreach (var part in parts)
                {
                    string trimmed = part.Trim().Replace("0x", "").Replace("0X", "");
                    
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    if (trimmed.Length > 2)
                    {
                        // 連続したHEX文字列を2文字ずつ分割
                        for (int i = 0; i < trimmed.Length; i += 2)
                        {
                            if (i + 1 < trimmed.Length)
                            {
                                bytes.Add(Convert.ToByte(trimmed.Substring(i, 2), 16));
                            }
                            else
                            {
                                bytes.Add(Convert.ToByte(trimmed.Substring(i, 1), 16));
                            }
                        }
                    }
                    else
                    {
                        bytes.Add(Convert.ToByte(trimmed, 16));
                    }
                }

                result = bytes.ToArray();
                return result.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// DEC文字列をバイト配列に変換（スペースまたはカンマ区切り）
        /// 例: "26 43 60" or "26,43,60"
        /// </summary>
        public static bool TryParseDec(string input, out byte[] result)
        {
            result = Array.Empty<byte>();
            
            if (string.IsNullOrWhiteSpace(input))
                return false;

            try
            {
                var parts = input.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                List<byte> bytes = new List<byte>();

                foreach (var part in parts)
                {
                    string trimmed = part.Trim();
                    
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    byte value = Convert.ToByte(trimmed, 10);
                    bytes.Add(value);
                }

                result = bytes.ToArray();
                return result.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// バイト配列をHEX文字列に変換
        /// </summary>
        public static string ToHexString(byte[] data, bool withPrefix = false)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            string prefix = withPrefix ? "0x" : "";
            return string.Join(" ", data.Select(b => $"{prefix}{b:X2}"));
        }

        /// <summary>
        /// バイト配列をDEC文字列に変換
        /// </summary>
        public static string ToDecString(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            return string.Join(" ", data.Select(b => b.ToString()));
        }
    }
}
