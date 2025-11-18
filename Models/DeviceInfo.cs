namespace UsbI2cController.Models
{
    public class DeviceInfo
    {
        public int Index { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string DisplayName => $"[{Index}] {Type} - {Description} (S/N: {SerialNumber})";
        
        public override string ToString() => DisplayName;
    }
}
