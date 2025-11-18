using System;

namespace UsbI2cController.Models
{
    public class I2CTransaction
    {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = string.Empty; // "Write" or "Read"
        public byte DeviceAddress { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public bool Success { get; set; }

        public string DataAsHex => Data != null ? BitConverter.ToString(Data).Replace("-", " ") : "";
        public string DataAsDec => Data != null ? string.Join(" ", Data) : "";
        
        public override string ToString()
        {
            string status = Success ? "✓" : "✗";
            return $"[{Timestamp:HH:mm:ss}] {status} {Type} @ 0x{DeviceAddress:X2}: {DataAsHex}";
        }
    }
}
