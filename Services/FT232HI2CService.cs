using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FTD2XX_NET;
using UsbI2cController.Models;

namespace UsbI2cController.Services
{
    public class FT232HI2CService : IDisposable
    {
        private FTDI _ftdi;
        private bool _isInitialized;
        private int _currentDeviceIndex = -1;
        private ClockingMode _currentClockingMode = ClockingMode.TwoPhase;
        
        // I2C clock edge setting (fixed to Negative Edge per I2C spec)
        private byte _i2cWriteCommand = 0x11;
        
        public enum I2CClockSpeed
        {
            Standard_100kHz = 0,  // 100kHz: 10us period → 5us per edge
            Fast_400kHz = 1       // 400kHz: 2.5us period → 1.25us per edge
        }
        
        public enum ClockingMode
        {
            TwoPhase = 0,   // 2-phase clocking (default, standard I2C)
            ThreePhase = 1  // 3-phase clocking (may affect timing)
        }
        
        // Debug log event
        public event Action<string>? DebugLog;

        public FT232HI2CService()
        {
            _ftdi = new FTDI();
        }

        private void Log(string message)
        {
            DebugLog?.Invoke(message);
        }

        /// <summary>
        /// Initialize FT232H device in I2C mode (uses first device by default)
        /// </summary>
        public bool Initialize()
        {
            return Initialize(0);
        }

        /// <summary>
        /// Initialize specified FT232H device in I2C mode
        /// </summary>
        /// <param name="deviceIndex">Device index (0-based)</param>
        public bool Initialize(int deviceIndex)
        {
            try
            {
                FTDI.FT_STATUS status;
                uint deviceCount = 0;

                // Get number of devices
                status = _ftdi.GetNumberOfDevices(ref deviceCount);
                if (status != FTDI.FT_STATUS.FT_OK || deviceCount == 0)
                {
                    return false;
                }

                // Check device index is in range
                if (deviceIndex < 0 || deviceIndex >= deviceCount)
                {
                    return false;
                }

                // Get device information
                FTDI.FT_DEVICE_INFO_NODE[] deviceList = new FTDI.FT_DEVICE_INFO_NODE[deviceCount];
                status = _ftdi.GetDeviceList(deviceList);
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    return false;
                }

                // Open specified FT232H device
                status = _ftdi.OpenByIndex((uint)deviceIndex);
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    return false;
                }

                _currentDeviceIndex = deviceIndex;

                // Reset to MPSSE mode
                status = _ftdi.ResetDevice();
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    return false;
                }

                // Set baud rate (not used in MPSSE mode but required)
                status = _ftdi.SetBaudRate(100000); // 100kHz
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    return false;
                }

                // Set data characteristics
                status = _ftdi.SetDataCharacteristics(FTDI.FT_DATA_BITS.FT_BITS_8, 
                                                       FTDI.FT_STOP_BITS.FT_STOP_BITS_1, 
                                                       FTDI.FT_PARITY.FT_PARITY_NONE);
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    return false;
                }

                // Disable flow control
                status = _ftdi.SetFlowControl(FTDI.FT_FLOW_CONTROL.FT_FLOW_NONE, 0, 0);
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    return false;
                }

                // Set timeouts (5 seconds for read and write)
                status = _ftdi.SetTimeouts(5000, 5000);
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    return false;
                }

                // Latency Timer
                status = _ftdi.SetLatency(16);
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    return false;
                }

                // Enable MPSSE mode
                status = _ftdi.SetBitMode(0x00, 0x00); // Reset
                System.Threading.Thread.Sleep(50);
                status = _ftdi.SetBitMode(0x00, 0x02); // MPSSE mode
                System.Threading.Thread.Sleep(50);

                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    return false;
                }

                // Clear buffers
                status = _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

                // Send MPSSE synchronization command (wait until MPSSE mode is ready)
                byte[] syncCommand = new byte[] { 0xAA }; // BAD_COMMAND
                uint bytesWritten = 0;
                _ftdi.Write(syncCommand, syncCommand.Length, ref bytesWritten);
                System.Threading.Thread.Sleep(30);
                
                // Read and discard error response (0xFA 0xAA expected)
                uint bytesAvailable = 0;
                _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                if (bytesAvailable > 0)
                {
                    byte[] dummyBuffer = new byte[bytesAvailable];
                    uint bytesRead = 0;
                    _ftdi.Read(dummyBuffer, bytesAvailable, ref bytesRead);
                }

                // Configure GPIO pins for I2C
                // AD0 = SCL (always output)
                // AD1 = SDA (bidirectional - switch between output/input)
                // AD2 = SDA input (for reading)
                List<byte> initBuffer = new List<byte>();
                
                // Set GPIO Low byte (AD0-AD7)
                initBuffer.Add(0x80); // Set data bits low byte
                initBuffer.Add(0xFF); // Value: SCL=1, SDA=1 (both high for idle)
                initBuffer.Add(0xFB); // Direction: AD0=out(SCL), AD1=out(SDA-out), AD2=in(SDA-in), AD3=out
                
                // Disable loopback
                initBuffer.Add(0x85); // Disable loopback
                
                // Set clocking mode (default: 2-phase)
                // 0x8D = Disable 3-phase clocking (use 2-phase)
                // 0x8C = Enable 3-phase clocking
                initBuffer.Add(_currentClockingMode == ClockingMode.TwoPhase ? (byte)0x8D : (byte)0x8C);
                
                // Disable adaptive clocking
                initBuffer.Add(0x97); // Turn off adaptive clocking
                
                bytesWritten = 0;
                status = _ftdi.Write(initBuffer.ToArray(), initBuffer.Count, ref bytesWritten);
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    return false;
                }

                _isInitialized = true;
                
                // Set default clock speed (100kHz)
                SetI2CClockSpeed(I2CClockSpeed.Standard_100kHz);
                
                return true;
            }
            catch
            {
                _isInitialized = false;
                return false;
            }
        }

        /// <summary>
        /// Set I2C clock speed
        /// </summary>
        /// <param name="speed">Clock speed (Standard_100kHz or Fast_400kHz)</param>
        public bool SetI2CClockSpeed(I2CClockSpeed speed)
        {
            if (!_isInitialized)
            {
                Log("Cannot set clock speed: Device not initialized");
                return false;
            }

            ushort divisor;
            string speedName;
            
            switch (speed)
            {
                case I2CClockSpeed.Standard_100kHz:
                    // 60MHz / (10 * 100kHz) - 1 = 59 Datasheet is incorrect, This value works correctly.
                    divisor = 59;
                    speedName = "100kHz (Standard)";
                    break;
                case I2CClockSpeed.Fast_400kHz:
                    // 60MHz / (10 * 400kHz) - 1 = 14 Datasheet is incorrect, This value works correctly.
                    divisor = 14;
                    speedName = "400kHz (Fast)";
                    break;
                default:
                    return false;
            }

            try
            {
                List<byte> buffer = new List<byte>();
                buffer.Add(0x86); // Set clock divisor command
                buffer.Add((byte)(divisor & 0xFF)); // Divisor Low byte
                buffer.Add((byte)(divisor >> 8)); // Divisor High byte
                
                Log($"Setting I2C Clock: divisor={divisor} (0x{divisor:X4})");
                Log($"  Command bytes: 0x86 0x{(byte)(divisor & 0xFF):X2} 0x{(byte)(divisor >> 8):X2}");
                Log($"  Expected SCL frequency: 60MHz / ((1 + {divisor}) * 10) = {60000000.0 / ((1 + divisor) * 10) / 1000.0:F2} kHz");
                
                uint bytesWritten = 0;
                FTDI.FT_STATUS status = _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                
                if (status == FTDI.FT_STATUS.FT_OK)
                {
                    Log($"I2C Clock Speed set to: {speedName} (divisor={divisor})");
                    return true;
                }
                else
                {
                    Log($"Failed to set clock speed: {status}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Exception setting clock speed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Set MPSSE clocking mode
        /// </summary>
        /// <param name="mode">Clocking mode (TwoPhase or ThreePhase)</param>
        /// <returns>True if successful</returns>
        public bool SetClockingMode(ClockingMode mode)
        {
            if (!_isInitialized)
            {
                Log("Cannot set clocking mode: Device not initialized");
                return false;
            }

            try
            {
                List<byte> buffer = new List<byte>();
                
                // 0x8D = Disable 3-phase clocking (use 2-phase)
                // 0x8C = Enable 3-phase clocking
                byte command = mode == ClockingMode.TwoPhase ? (byte)0x8D : (byte)0x8C;
                buffer.Add(command);
                
                uint bytesWritten = 0;
                FTDI.FT_STATUS status = _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                
                if (status == FTDI.FT_STATUS.FT_OK)
                {
                    _currentClockingMode = mode;
                    Log($"Clocking mode set to: {mode}");
                    return true;
                }
                else
                {
                    Log($"Failed to set clocking mode: {status}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Exception setting clocking mode: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get current clocking mode
        /// </summary>
        /// <returns>Current clocking mode</returns>
        public ClockingMode GetClockingMode()
        {
            return _currentClockingMode;
        }

        /// <summary>
        /// Write data to I2C device (continuous transmission from START to STOP)
        /// </summary>
        /// <param name="deviceAddress">7-bit I2C address</param>
        /// <param name="data">Data bytes to send</param>
        /// <returns>True if successful</returns>
        public bool WriteI2C(byte deviceAddress, byte[] data)
        {
            if (!_isInitialized || data == null || data.Length == 0)
            {
                Log("I2C Write failed: Invalid parameters");
                return false;
            }

            try
            {
                // I2C write address (7-bit address left-shifted + write bit 0)
                byte writeAddress = (byte)((deviceAddress << 1) & 0xFE);
                
                Log($"I2C Write: Device 0x{deviceAddress:X2}, {data.Length} bytes");
                Log($"  Data: {BitConverter.ToString(data)}");
                
                List<byte> buffer = new List<byte>();
                
                // Clear buffers
                _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                
                // START condition
                buffer.AddRange(I2CStart());
                
                // Send address byte
                buffer.Add(0x11); // Clock data byte out on -ve edge, MSB first
                buffer.Add(0x00); // Length Low (1 byte - 1 = 0)
                buffer.Add(0x00); // Length High
                buffer.Add(writeAddress);
                
                // Get Acknowledge bit for address
                buffer.Add(0x80); // Command to set directions of lower 8 pins
                buffer.Add(0x00); // Set SCL low
                buffer.Add(0x01); // Set SK as output, DO and others as input
                
                buffer.Add(0x22); // MSB_RISING_EDGE_CLOCK_BIT_IN - scan in ACK bit
                buffer.Add(0x00); // Length of 0x0 means to scan in 1 bit
                
                // Send data bytes
                foreach (byte dataByte in data)
                {
                    // Set SDA high, SCL low after ACK
                    buffer.Add(0x80);
                    buffer.Add(0x02); // Set SDA high, SCL low
                    buffer.Add(0x03); // Set SK,DO as output

                    // Clock data byte out on -ve Clock Edge MSB first
                    buffer.Add(0x11); // MSB_FALLING_EDGE_CLOCK_BYTE_OUT
                    buffer.Add(0x00); // Length Low
                    buffer.Add(0x00); // Length High
                    buffer.Add(dataByte); // Data to be sent
                    
                    // Get Acknowledge bit
                    buffer.Add(0x80); // Command to set directions
                    buffer.Add(0x00); // Set SCL low
                    buffer.Add(0x01); // Set SK as output, DO and others as input
                    
                    buffer.Add(0x22); // MSB_RISING_EDGE_CLOCK_BIT_IN - scan in ACK bit
                    buffer.Add(0x00); // Length of 0x0 means to scan in 1 bit

                }

                // STOP condition
                buffer.AddRange(I2CStop());

                // // Send answer back immediate command
                buffer.Add(0x87);
                // dump buffer
                Log($"I2C Write: Command buffer: {BitConverter.ToString(buffer.ToArray())}");
                // Send all commands at once
                uint bytesWritten = 0;
                FTDI.FT_STATUS status = _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);                
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    Log($"I2C Write failed: Write command error");
                    return false;
                }
                
                Log($"I2C Write: Commands sent successfully ({bytesWritten} bytes)");
                
                // Wait for ACK responses (address + data bytes)
                int expectedAckBytes = 1 + data.Length;
                
                // Wait for the bytes to come back (ACK bits from slave)
                uint bytesAvailable = 0;
                int readTimeoutCounter = 0;
                status = _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                
                while ((bytesAvailable < expectedAckBytes) && (status == FTDI.FT_STATUS.FT_OK) && (readTimeoutCounter < 500))
                {
                    status = _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                    readTimeoutCounter++;
                    System.Threading.Thread.Sleep(1);
                }
                
                if (status != FTDI.FT_STATUS.FT_OK || bytesAvailable == 0)
                {
                    Log($"I2C Write failed: No ACK response (expected {expectedAckBytes} bytes, timeout={readTimeoutCounter})");
                    return false;
                }
                
                byte[] ackBuffer = new byte[bytesAvailable];
                uint ackRead = 0;
                status = _ftdi.Read(ackBuffer, bytesAvailable, ref ackRead);
                
                if (status == FTDI.FT_STATUS.FT_OK && ackRead > 0)
                {
                    Log($"I2C Write: ACK response received ({ackRead} bytes, expected {expectedAckBytes})");
                    Log($"  ACK data: {BitConverter.ToString(ackBuffer, 0, (int)ackRead)}");
                    
                    // Check ACK bits (bit 0 should be 0 for ACK, 1 for NACK)
                    bool allAck = true;
                    for (int i = 0; i < Math.Min(expectedAckBytes, (int)ackRead); i++)
                    {
                        bool ack = (ackBuffer[i] & 0x01) == 0x00;
                        if (!ack)
                        {
                            if (i == 0)
                            {
                                Log($"I2C Write: Address 0x{deviceAddress:X2} NACK - device not responding");
                            }
                            else
                            {
                                Log($"I2C Write: Data byte {i-1} (0x{data[i-1]:X2}) NACK");
                            }
                            allAck = false;
                        }
                    }
                    
                    if (!allAck)
                    {
                        Log("I2C Write: NACK received - write failed");
                        return false;
                    }
                    
                    Log("I2C Write: All ACKs confirmed");
                }
                else
                {
                    Log("I2C Write: Failed to read ACK response");
                    return false;
                }
                
                // Wait for EEPROM write cycle (20ms for safety)
                // System.Threading.Thread.Sleep(20);
                
                Log("I2C Write: Write cycle completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Log($"I2C Write exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Read data from I2C device (Current Address Read)
        /// </summary>
        /// <param name="deviceAddress">7-bit I2C address</param>
        /// <param name="length">Number of bytes to read</param>
        /// <param name="data">Read data</param>
        /// <returns>True if successful</returns>
        public bool ReadI2C(byte deviceAddress, int length, out byte[] data)
        {
            data = Array.Empty<byte>();

            if (!_isInitialized || length <= 0)
            {
                return false;
            }

            try
            {
                // I2C read address (7-bit address left-shifted + read bit 1)
                byte readAddress = (byte)((deviceAddress << 1) | 0x01);
                
                List<byte> buffer = new List<byte>();
                
                // START condition
                buffer.AddRange(I2CStart());
                
                // Send address
                buffer.AddRange(I2CWriteByte(readAddress));
                
                // Read data
                for (int i = 0; i < length; i++)
                {
                    bool isLast = (i == length - 1);
                    buffer.AddRange(I2CReadByte(!isLast)); // Send ACK except for last byte
                }
                
                // STOP condition
                buffer.AddRange(I2CStop());
                
                // Send immediately
                buffer.Add(0x87);

                // Send to FTDI
                uint bytesWritten = 0;
                FTDI.FT_STATUS status = _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    Log($"I2C Read failed: Write command error");
                    return false;
                }

                // Expected bytes: Address ACK(1) + Data(length) bytes
                int expectedBytes = 1 + length;
                
                // Wait for the bytes to come back
                uint bytesAvailable = 0;
                int readTimeoutCounter = 0;
                status = _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                
                while ((bytesAvailable < expectedBytes) && (status == FTDI.FT_STATUS.FT_OK) && (readTimeoutCounter < 500))
                {
                    status = _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                    readTimeoutCounter++;
                    System.Threading.Thread.Sleep(1); // short delay
                }
                
                if (status != FTDI.FT_STATUS.FT_OK || bytesAvailable == 0)
                {
                    Log($"I2C Read failed: No data available (expected {expectedBytes} bytes, timeout={readTimeoutCounter})");
                    return false;
                }

                byte[] readBuffer = new byte[bytesAvailable];
                uint bytesRead = 0;
                status = _ftdi.Read(readBuffer, bytesAvailable, ref bytesRead);
                
                if (status == FTDI.FT_STATUS.FT_OK && bytesRead > 0)
                {
                    Log($"I2C Read: Received {bytesRead} bytes total (expected {expectedBytes})");
                    Log($"  Raw data: {BitConverter.ToString(readBuffer, 0, (int)bytesRead)}");
                    
                    // MPSSE response structure:
                    // [0]: ACK bit from I2CWriteByte(readAddress) (bit 0: 0=ACK, 1=NACK)
                    // [1 to 1+length-1]: 8-bit data returned from 0x20 command
                    
                    // Check address ACK bit (response from 0x22 command)
                    if (bytesRead >= 1)
                    {
                        bool ack = (readBuffer[0] & 0x01) == 0x00;
                        Log($"  Address ACK bit: {(readBuffer[0] & 0x01)} (ACK={ack})");
                        
                        if (!ack)
                        {
                            Log("I2C Read: NACK received for address");
                            return false;
                        }
                    }
                    
                    // Extract data portion (after 1 ACK byte)
                    int dataOffset = 1;
                    int availableDataBytes = (int)bytesRead - dataOffset;
                    
                    if (availableDataBytes >= length)
                    {
                        data = new byte[length];
                        Array.Copy(readBuffer, dataOffset, data, 0, length);
                    }
                    else if (availableDataBytes > 0)
                    {
                        data = new byte[availableDataBytes];
                        Array.Copy(readBuffer, dataOffset, data, 0, availableDataBytes);
                    }
                    else
                    {
                        Log("I2C Read: No data bytes available");
                        return false;
                    }
                    
                    Log($"I2C Read success: {data.Length} bytes read");
                    Log($"  Extracted data: {BitConverter.ToString(data)}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log($"I2C Read exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Read data from specified memory address of I2C device (STOP-separated method - BME280 style)
        /// For memory devices like EEPROM
        /// </summary>
        /// <param name="deviceAddress">7-bit I2C address</param>
        /// <param name="memoryAddress">Memory/Register address (8-bit)</param>
        /// <param name="length">Number of bytes to read</param>
        /// <param name="data">Read data</param>
        /// <returns>True if successful</returns>
        public bool ReadI2CWithAddress(byte deviceAddress, byte memoryAddress, int length, out byte[] data)
        {
            data = Array.Empty<byte>();

            if (!_isInitialized || length <= 0)
            {
                return false;
            }

            try
            {
                byte writeAddress = (byte)((deviceAddress << 1) & 0xFE);
                byte readAddress = (byte)((deviceAddress << 1) | 0x01);
                
                // Clear buffers
                _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                
                // === Phase 1: Dummy Write with STOP ===
                List<byte> buffer1 = new List<byte>();
                
                // START condition
                buffer1.AddRange(I2CStart());
                
                // Send device address (Write)
                buffer1.AddRange(I2CWriteByte(writeAddress));
                
                // Send memory address
                buffer1.AddRange(I2CWriteByte(memoryAddress));
                
                // STOP condition (Dummy Write complete)
                buffer1.AddRange(I2CStop());
                
                // Send Dummy Write
                uint bytesWritten = 0;
                FTDI.FT_STATUS status = _ftdi.Write(buffer1.ToArray(), buffer1.Count, ref bytesWritten);
                
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    Log($"I2C Random Read failed: Dummy Write error");
                    return false;
                }
                
                // Wait for Dummy Write ACKs
                int expectedAckBytes = 2; // WriteAddress + MemoryAddress
                uint bytesAvailable = 0;
                int readTimeoutCounter = 0;
                status = _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                
                while ((bytesAvailable < expectedAckBytes) && (status == FTDI.FT_STATUS.FT_OK) && (readTimeoutCounter < 500))
                {
                    status = _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                    readTimeoutCounter++;
                    System.Threading.Thread.Sleep(1);
                }
                
                if (bytesAvailable > 0)
                {
                    byte[] ackBuffer = new byte[bytesAvailable];
                    uint ackRead = 0;
                    _ftdi.Read(ackBuffer, bytesAvailable, ref ackRead);
                    
                    Log($"I2C Random Read: Dummy Write ACK received ({ackRead} bytes)");
                    
                    // Check ACKs
                    for (int i = 0; i < Math.Min(expectedAckBytes, (int)ackRead); i++)
                    {
                        bool ack = (ackBuffer[i] & 0x01) == 0x00;
                        if (!ack)
                        {
                            Log($"I2C Random Read: NACK at Dummy Write byte {i}");
                            return false;
                        }
                    }
                }
                
                // Insert 2-clock wait between Dummy Write and Phase 2
                // Ensures minimum time for I2C bus to maintain idle state
                List<byte> waitBuffer = new List<byte>();
                
                // Maintain idle state for 2 clock cycles (SCL High, SDA High)
                for (int i = 0; i < 2; i++)
                {
                    // SCL High → Low → High cycle
                    waitBuffer.Add(0x80); // Set GPIO
                    waitBuffer.Add(0xFF); // SCL=1, SDA=1 (idle)
                    waitBuffer.Add(0xFB); // Direction
                    
                    waitBuffer.Add(0x80); // Set GPIO
                    waitBuffer.Add(0xFE); // SCL=0, SDA=1
                    waitBuffer.Add(0xFB); // Direction
                    
                    waitBuffer.Add(0x80); // Set GPIO
                    waitBuffer.Add(0xFF); // SCL=1, SDA=1 (idle)
                    waitBuffer.Add(0xFB); // Direction
                }
                
                // Send wait command
                bytesWritten = 0;
                status = _ftdi.Write(waitBuffer.ToArray(), waitBuffer.Count, ref bytesWritten);
                System.Threading.Thread.Sleep(1); // Additional stabilization wait
                
                // === Phase 2: Multi-Byte Read with START ===
                List<byte> buffer2 = new List<byte>();
                
                // START condition (new transaction)
                buffer2.AddRange(I2CStart());
                
                // Send device address (Read)
                buffer2.AddRange(I2CWriteByte(readAddress));
                
                // Read data (Sequential Read)
                for (int i = 0; i < length; i++)
                {
                    bool isLast = (i == length - 1);
                    buffer2.AddRange(I2CReadByte(!isLast)); // NACK for last byte
                }
                
                // STOP condition
                buffer2.AddRange(I2CStop());
                
                // Send Read
                bytesWritten = 0;
                status = _ftdi.Write(buffer2.ToArray(), buffer2.Count, ref bytesWritten);
                
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    Log($"I2C Random Read failed: Read command error");
                    return false;
                }
                
                // Wait for Read response
                int expectedBytes = 1 + length; // ReadAddress ACK + Data
                bytesAvailable = 0;
                readTimeoutCounter = 0;
                status = _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                
                while ((bytesAvailable < expectedBytes) && (status == FTDI.FT_STATUS.FT_OK) && (readTimeoutCounter < 500))
                {
                    status = _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                    readTimeoutCounter++;
                    System.Threading.Thread.Sleep(1);
                }
                
                if (status != FTDI.FT_STATUS.FT_OK || bytesAvailable == 0)
                {
                    Log($"I2C Random Read failed: No data (expected {expectedBytes} bytes)");
                    return false;
                }
                
                byte[] readBuffer = new byte[bytesAvailable];
                uint bytesRead = 0;
                status = _ftdi.Read(readBuffer, bytesAvailable, ref bytesRead);
                
                if (status == FTDI.FT_STATUS.FT_OK && bytesRead > 0)
                {
                    Log($"I2C Random Read: Received {bytesRead} bytes (expected {expectedBytes})");
                    Log($"  Raw data: {BitConverter.ToString(readBuffer, 0, (int)bytesRead)}");
                    
                    // MPSSE response structure (Phase 2):
                    // [0]: ACK bit from I2CWriteByte(readAddress) (bit 0: 0=ACK, 1=NACK)
                    // [1 to 1+length-1]: 8-bit data returned from 0x20 command
                    
                    // Check address ACK bit (response from 0x22 command)
                    if (bytesRead >= 1)
                    {
                        bool ack = (readBuffer[0] & 0x01) == 0x00;
                        Log($"  Read address ACK bit: {(readBuffer[0] & 0x01)} (ACK={ack})");
                        
                        if (!ack)
                        {
                            Log("I2C Random Read: NACK for read address");
                            return false;
                        }
                    }
                    
                    // Extract data
                    int dataOffset = 1;
                    int availableDataBytes = (int)bytesRead - dataOffset;
                    
                    if (availableDataBytes >= length)
                    {
                        data = new byte[length];
                        Array.Copy(readBuffer, dataOffset, data, 0, length);
                    }
                    else if (availableDataBytes > 0)
                    {
                        data = new byte[availableDataBytes];
                        Array.Copy(readBuffer, dataOffset, data, 0, availableDataBytes);
                    }
                    else
                    {
                        Log("I2C Random Read: No data bytes");
                        return false;
                    }
                    
                    Log($"I2C Random Read success: Register 0x{memoryAddress:X2}, {data.Length} bytes");
                    Log($"  Data: {BitConverter.ToString(data)}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Log($"I2C Random Read exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Scan I2C bus and detect responding device addresses
        /// </summary>
        /// <param name="startAddress">Start address for scan (default: 0x03)</param>
        /// <param name="endAddress">End address for scan (default: 0x77)</param>
        /// <param name="progress">Progress callback (current address, total addresses)</param>
        /// <returns>List of detected I2C addresses</returns>
        public List<byte> ScanI2CBus(byte startAddress = 0x03, byte endAddress = 0x77, Action<int, int>? progress = null)
        {
            var foundAddresses = new List<byte>();

            if (!_isInitialized)
            {
                return foundAddresses;
            }

                // Check reserved address ranges
            if (startAddress < 0x03) startAddress = 0x03;  // 0x00-0x02 reserved
            if (endAddress > 0x77) endAddress = 0x77;      // 0x78-0x7F reserved
            
            Log($"=== I2C Bus Scan Started (0x{startAddress:X2} - 0x{endAddress:X2}) ===");

            int totalAddresses = endAddress - startAddress + 1;
            int currentIndex = 0;

            for (byte address = startAddress; address <= endAddress; address++)
            {
                currentIndex++;
                progress?.Invoke(currentIndex, totalAddresses);

                try
                {
                    // Check each address
                    if (CheckI2CDeviceWithAck(address))
                    {
                        foundAddresses.Add(address);
                        Log($">>> Device found at address 0x{address:X2}");
                    }
                    
                    // Short wait (for bus stabilization)
                    System.Threading.Thread.Sleep(2);
                }
                catch
                {
                    // Skip on error
                    continue;
                }
            }

            Log($"=== I2C Bus Scan Completed: {foundAddresses.Count} device(s) found ===");
            if (foundAddresses.Count > 0)
            {
                Log($"Found addresses: {string.Join(", ", foundAddresses.Select(a => $"0x{a:X2}"))}");
            }

            return foundAddresses;
        }

        /// <summary>
        /// Check if device at specified I2C address responds (using ACK bit)
        /// </summary>
        /// <param name="address">I2C address to check</param>
        /// <returns>True if device responds</returns>
        private bool CheckI2CDeviceWithAck(byte address)
        {
            if (!_isInitialized)
            {
                return false;
            }

            try
            {
                byte writeAddress = (byte)((address << 1) & 0xFE);
                
                List<byte> buffer = new List<byte>();
                
                // Clear buffer (remove old data)
                _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                
                // START condition
                buffer.AddRange(I2CStart());
                
                // Send address byte
                buffer.Add(0x11); // Clock data byte out on -ve edge, MSB first
                buffer.Add(0x00); // Length Low (1 byte - 1 = 0)
                buffer.Add(0x00); // Length High
                buffer.Add(writeAddress);
                
                // Read ACK bit: Direct GPIO control for timing
                // Set AD1(SDA-out) High (release), SCL=Low
                buffer.Add(0x80); // Set data bits low byte
                buffer.Add(0x00); // Value: AD0(SCL)=0, AD1(SDA)=0
                buffer.Add(0x11); // Direction: AD0=out, AD1/2=in
                
                // Set SCL High and sample ACK bit
                buffer.Add(0x22); // MSB Rising edge clock + GPIO bits read
                buffer.Add(0x00); // Length Low (1 bit - 1 = 0)

                // Send immediately
                buffer.Add(0x87);

                buffer.AddRange(I2CStop());
                                
                // Send to FTDI
                uint bytesWritten = 0;
                FTDI.FT_STATUS status = _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    Log($"Address 0x{address:X2}: Write failed");
                    return false;
                }
                
                // Check ACK bit response
                uint bytesAvailable = 1;                
                byte[] readBuffer = new byte[bytesAvailable];
                uint bytesRead = 0;
                FTDI.FT_STATUS read_status = _ftdi.Read(readBuffer, bytesAvailable, ref bytesRead);

                // Log($"Address 0x{address:X2}: Read {bytesRead} bytes (expected >=1)");
                if (read_status == FTDI.FT_STATUS.FT_OK && bytesRead >= 1)
                {
                    // Check ACK bit (bit 0)
                    byte gpioState = readBuffer[0];
                    bool ackReceived = (gpioState & 0x01) == 0;
                    
                    // Debug log (detailed)
                    {
                        StringBuilder logBuilder = new StringBuilder();
                        logBuilder.AppendLine($"Address 0x{address:X2}: GPIO = 0x{gpioState:X2} (binary: {Convert.ToString(gpioState, 2).PadLeft(8, '0')})");
                        logBuilder.AppendLine($"  AD2 (SDA) = {((gpioState & 0x04) >> 2)}, ACK = {(ackReceived ? "YES" : "NO")}");
                        Log(logBuilder.ToString());
                    }
                    
                    return ackReceived;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Log($"Exception in CheckI2CDeviceWithAck: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if device at specified I2C address responds
        /// </summary>
        /// <param name="address">I2C address to check</param>
        /// <returns>True if device responds</returns>
        public bool CheckI2CDevice(byte address)
        {
            return CheckI2CDeviceWithAck(address);
        }

        // I2C START condition
        private byte[] I2CStart()
        {
            // FTDI AN_255 compliant: Repeat each state 4 times to ensure minimum start hold and setup time
            List<byte> cmd = new List<byte>();
            
            // START condition begin: SDA Low (while SCL High)
            // Repeat 4 times to ensure minimum start hold time
            for (int i = 0; i < 4; i++)
            {
                cmd.Add(0x80); // Set data bits low byte
                cmd.Add(0x03); // Value: AD0(SCL)=1, AD1(SDA)=1, AD2=0, AD3=0 (0x03 = 0000 0011)
                               // SCL=1, SDA=1 initially (idle state before START)
                cmd.Add(0x03); // Direction: AD0/1=out, AD2-7=in (0x03 = 0000 0011)
            }
            
            // START condition complete: SCL Low
            // Repeat 4 times to ensure minimum start setup time
            for (int i = 0; i < 4; i++)
            {
                cmd.Add(0x80); // Set data bits low byte
                cmd.Add(0x01); // Value: AD0(SCL)=1, AD1(SDA)=0, AD2=0, AD3=0 (0x01 = 0000 0001)
                               // SCL=1, SDA=0 (START condition: SDA goes low while SCL high)
                cmd.Add(0x03); // Direction: AD0/1=out, AD2-7=in (0x03 = 0000 0011)
            }
            cmd.Add(0x80);
            cmd.Add(0x00); 
            cmd.Add(0x03);

            return cmd.ToArray();
        }

        // I2C STOP condition
        private byte[] I2CStop()
        {            
            List<byte> cmd = new List<byte>();
            
            // SCL low, SDA low
            for (int j = 0; j < 10; j++)
            {
                cmd.Add(0x80); // MPSSE_CMD_SET_DATA_BITS_LOWBYTE
                cmd.Add(0x00);
                cmd.Add(0x03);
            }
            
            // SCL high, SDA low
            for (int j = 0; j < 10; j++)
            {
                cmd.Add(0x80); // MPSSE_CMD_SET_DATA_BITS_LOWBYTE
                cmd.Add(0x01);
                cmd.Add(0x03);
            }
            
            // SCL high, SDA high
            for (int j = 0; j < 10; j++)
            {
                cmd.Add(0x80); // MPSSE_CMD_SET_DATA_BITS_LOWBYTE
                cmd.Add(0x03);
                cmd.Add(0x03);
            }
            
            // Tristate the SCL & SDA pins
            cmd.Add(0x80);
            cmd.Add(0x03);
            cmd.Add(0x03);
            
            return cmd.ToArray();
        }

        // I2C write one byte
        private byte[] I2CWriteByte(byte data)
        {
            // FTDI AN_255 compliant implementation
            // 0x11 = Clock data byte out on -ve edge, MSB first (I2C standard)
            // 0x10 = Clock data byte out on +ve edge, MSB first (non-standard)
            List<byte> cmd = new List<byte>
            {
                _i2cWriteCommand, // Clock edge command (always 0x11: negative edge)
                0x00, // Length Low (1 byte - 1 = 0)
                0x00, // Length High
                data, // Actual data byte

                // Read ACK bit: Direct GPIO control for timing
                // Set AD1(SDA-out) High (release), SCL=Low
                0x80, // Set data bits low byte
                0x00, // Value: AD0(SCL)=0, AD1(SDA)=0
                0x11, // Direction: AD0=out, AD1/2=in
                
                // Read ACK bit: Set SCL High and sample ACK bit
                0x22, // Clock data bits in on +ve edge, MSB first
                0x00, // Length Low (1 bit - 1 = 0)
                
                // This command tells the MPSSE to send any results gathered back immediately
                0x87  // Send answer back immediate command
            };
            return cmd.ToArray();
        }

        // I2C read one byte
        private byte[] I2CReadByte(bool sendAck)
        {
            // FTDI AN_255 compliant implementation
            // Switch AD1 to input mode for reading SDA
            List<byte> cmd = new List<byte>
            {
                // Set AD1 to input mode (to read SDA)
                0x80, // Set data bits low byte
                0xFE, // Value: SCL=0, SDA=1 (pull-up)
                0xF9, // Direction: AD0=out(SCL), AD1=in(SDA), AD2=in, AD3=out
                
                // Clock one byte of data in from AD1 (now configured as input)
                0x20, // Command: clock data byte in on +ve edge, MSB first
                0x00, // Length Low (1 byte - 1 = 0)
                0x00, // Length High
                
                // Return AD1 to output mode before sending ACK
                0x80, // Set data bits low byte
                0xFE, // Value: SCL=0, SDA=1
                0xFB, // Direction: AD0/1/3-7=out, AD2=in (AD1 back to output)
                
                0x13, // Command: clock data bits out on -ve edge, MSB first
                0x00, // Length of 0x00 means clock out ONE bit
                (byte)(sendAck ? 0x00 : 0xFF), // ACK=0x00 (bit7='0'), NACK=0xFF (bit7='1')
                
                // Put I2C line back to idle (during transfer) state...
                0x80, // Command to set ADbus direction/data
                0xFE, // Set the value of the pins (SCL=0, SDA=1)
                0xFB, // Direction: AD0/1/3-7=out, AD2=in
            };
            return cmd.ToArray();
        }

        /// <summary>
        /// Get number of connected devices
        /// </summary>
        public uint GetDeviceCount()
        {
            uint count = 0;
            _ftdi.GetNumberOfDevices(ref count);
            return count;
        }

        /// <summary>
        /// Get list of available devices
        /// </summary>
        public List<DeviceInfo> GetAvailableDevices()
        {
            var deviceInfoList = new List<DeviceInfo>();
            uint deviceCount = 0;

            FTDI.FT_STATUS status = _ftdi.GetNumberOfDevices(ref deviceCount);
            if (status != FTDI.FT_STATUS.FT_OK || deviceCount == 0)
            {
                return deviceInfoList;
            }

            FTDI.FT_DEVICE_INFO_NODE[] deviceList = new FTDI.FT_DEVICE_INFO_NODE[deviceCount];
            status = _ftdi.GetDeviceList(deviceList);

            if (status == FTDI.FT_STATUS.FT_OK)
            {
                for (int i = 0; i < deviceCount; i++)
                {
                    var device = deviceList[i];
                    string serialNumber = "Unknown";
                    string description = "FT232H USB-I2C";
                    
                    // Open device by Location ID to get detailed information
                    var tempFtdi = new FTDI();
                    var openStatus = tempFtdi.OpenByLocation(device.LocId);
                    
                    if (openStatus == FTDI.FT_STATUS.FT_OK)
                    {
                        // Read device information
                        tempFtdi.GetCOMPort(out _);
                        
                        // Read serial number from device EEPROM
                        FTD2XX_NET.FTDI.FT232H_EEPROM_STRUCTURE eeprom = new FTD2XX_NET.FTDI.FT232H_EEPROM_STRUCTURE();
                        var eepromStatus = tempFtdi.ReadFT232HEEPROM(eeprom);
                        
                        if (eepromStatus == FTDI.FT_STATUS.FT_OK)
                        {
                            serialNumber = eeprom.SerialNumber ?? serialNumber;
                            description = eeprom.Description ?? description;
                        }
                        
                        tempFtdi.Close();
                    }
                    
                    Log($"Device {i}: Type={device.Type}, Desc='{description}', S/N='{serialNumber}', ID=0x{device.ID:X}, LocId=0x{device.LocId:X}");
                    
                    deviceInfoList.Add(new DeviceInfo
                    {
                        Index = i,
                        Type = device.Type.ToString(),
                        Description = description,
                        SerialNumber = serialNumber
                    });
                }
            }
            else
            {
                Log($"GetDeviceList failed: {status}");
            }

            return deviceInfoList;
        }

        /// <summary>
        /// Get currently connected device index
        /// </summary>
        public int GetCurrentDeviceIndex()
        {
            return _currentDeviceIndex;
        }

        /// <summary>
        /// Test GPIO pin states (for debugging)
        /// </summary>
        public string TestGPIORead()
        {
            if (!_isInitialized)
            {
                return "Not initialized";
            }

            try
            {
                StringBuilder result = new StringBuilder();
                
                // Test 1: GPIO Output Control
                result.AppendLine("=== Test 1: GPIO Output Control ===");
                
                // Set SDA Low
                List<byte> buffer = new List<byte>();
                buffer.Add(0x80); // Set data bits low byte
                buffer.Add(0x00); // Value: SCL=0, SDA=0
                buffer.Add(0x03); // Direction: AD0=out, AD1=out
                buffer.Add(0x87); // Send immediate
                
                _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                uint bytesWritten = 0;
                _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                System.Threading.Thread.Sleep(10);
                
                // Read GPIO state
                buffer.Clear();
                buffer.Add(0x81); // Read data bits low byte
                buffer.Add(0x87);
                _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                System.Threading.Thread.Sleep(10);
                
                uint bytesAvailable = 0;
                _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                if (bytesAvailable > 0)
                {
                    byte[] readBuffer = new byte[bytesAvailable];
                    uint bytesRead = 0;
                    _ftdi.Read(readBuffer, bytesAvailable, ref bytesRead);
                    if (bytesRead > 0)
                    {
                        result.AppendLine($"SDA=Low: GPIO = 0x{readBuffer[0]:X2} (binary: {Convert.ToString(readBuffer[0], 2).PadLeft(8, '0')})");
                    }
                }
                
                // Set SDA High
                buffer.Clear();
                buffer.Add(0x80);
                buffer.Add(0x02); // Value: SCL=0, SDA=1
                buffer.Add(0x03); // Direction: AD0=out, AD1=out
                buffer.Add(0x81); // Read data bits
                buffer.Add(0x87);
                
                _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                System.Threading.Thread.Sleep(10);
                
                _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                if (bytesAvailable > 0)
                {
                    byte[] readBuffer = new byte[bytesAvailable];
                    uint bytesRead = 0;
                    _ftdi.Read(readBuffer, bytesAvailable, ref bytesRead);
                    if (bytesRead > 0)
                    {
                        result.AppendLine($"SDA=High: GPIO = 0x{readBuffer[0]:X2} (binary: {Convert.ToString(readBuffer[0], 2).PadLeft(8, '0')})");
                    }
                }
                
                // Test 2: Input mode SDA read
                result.AppendLine("\n=== Test 2: GPIO Input Read (with pullup) ===");
                
                buffer.Clear();
                buffer.Add(0x80);
                buffer.Add(0x00); // Value
                buffer.Add(0x09); // Direction: AD0=out(SCL), AD1=in, AD2=in, AD3=out
                buffer.Add(0x81); // Read data bits
                buffer.Add(0x87);
                
                _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                System.Threading.Thread.Sleep(50);
                
                _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                if (bytesAvailable > 0)
                {
                    byte[] readBuffer = new byte[bytesAvailable];
                    uint bytesRead = 0;
                    _ftdi.Read(readBuffer, bytesAvailable, ref bytesRead);
                    if (bytesRead > 0)
                    {
                        byte gpioState = readBuffer[0];
                        result.AppendLine($"GPIO Input: 0x{gpioState:X2} (binary: {Convert.ToString(gpioState, 2).PadLeft(8, '0')})");
                        result.AppendLine($"  AD0 (SCL): {(gpioState & 0x01)}");
                        result.AppendLine($"  AD1 (SDA-out): {((gpioState & 0x02) >> 1)}");
                        result.AppendLine($"  AD2 (SDA-in): {((gpioState & 0x04) >> 2)}");
                        result.AppendLine($"  AD3: {((gpioState & 0x08) >> 3)}");
                        
                        if ((gpioState & 0x04) != 0)
                        {
                            result.AppendLine("\n✓ AD2 (SDA-in) is HIGH (pullup OK or device holding high)");
                        }
                        else
                        {
                            result.AppendLine("\n✗ AD2 (SDA-in) is LOW (no pullup or device pulling low)");
                        }
                    }
                }
                
                // Restore original settings
                buffer.Clear();
                buffer.Add(0x80);
                buffer.Add(0x03); // Value: SCL=1, SDA=1
                buffer.Add(0x03); // Direction: AD0=out, AD1=out
                buffer.Add(0x87);
                _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                
                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Exception: {ex.Message}";
            }
        }

        /// <summary>
        /// Reset I2C bus (send clock pulses to release bus lock)
        /// </summary>
        public string ResetI2CBus()
        {
            if (!_isInitialized)
            {
                return "Not initialized";
            }

            try
            {
                StringBuilder result = new StringBuilder();
                result.AppendLine("=== I2C Bus Reset ===");
                
                // Set SDA to input mode
                List<byte> buffer = new List<byte>();
                buffer.Add(0x80);
                buffer.Add(0x00); // SCL=0, SDA=0
                buffer.Add(0x09); // Direction: AD0=out, AD1=in, AD2=in, AD3=out
                
                // Send 9 clock pulses (to allow slave device to complete byte transmission)
                for (int i = 0; i < 9; i++)
                {
                    buffer.Add(0x80);
                    buffer.Add(0x01); // SCL=1
                    buffer.Add(0x09);
                    
                    buffer.Add(0x80);
                    buffer.Add(0x00); // SCL=0
                    buffer.Add(0x09);
                }
                
                // Return SDA to output mode and send STOP condition
                buffer.Add(0x80);
                buffer.Add(0x00); // SCL=0, SDA=0
                buffer.Add(0x0B); // Direction: AD0/1/3=out, AD2=in
                
                buffer.Add(0x80);
                buffer.Add(0x02); // SCL=1, SDA=0
                buffer.Add(0x0B);
                
                buffer.Add(0x80);
                buffer.Add(0x03); // SCL=1, SDA=1 (STOP)
                buffer.Add(0x0B);
                
                buffer.Add(0x87); // Send immediate
                
                _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                uint bytesWritten = 0;
                _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                System.Threading.Thread.Sleep(50);
                
                result.AppendLine("9 clock pulses sent to release bus lock.");
                
                // Check bus state
                buffer.Clear();
                buffer.Add(0x80);
                buffer.Add(0x00);
                buffer.Add(0x09); // AD2=input (SDA-in)
                buffer.Add(0x81); // Read GPIO
                buffer.Add(0x87);
                
                _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                System.Threading.Thread.Sleep(50);
                
                uint bytesAvailable = 0;
                _ftdi.GetRxBytesAvailable(ref bytesAvailable);
                if (bytesAvailable > 0)
                {
                    byte[] readBuffer = new byte[bytesAvailable];
                    uint bytesRead = 0;
                    _ftdi.Read(readBuffer, bytesAvailable, ref bytesRead);
                    if (bytesRead > 0)
                    {
                        byte gpio = readBuffer[0];
                        result.AppendLine($"\nAfter reset: GPIO = 0x{gpio:X2}");
                        result.AppendLine($"SDA (AD2): {((gpio & 0x04) >> 2)}");
                        
                        if ((gpio & 0x04) != 0)
                        {
                            result.AppendLine("\n✓ Bus reset successful - SDA is now HIGH");
                        }
                        else
                        {
                            result.AppendLine("\n✗ SDA still LOW - Check hardware:");
                            result.AppendLine("  1. Is pullup resistor connected? (2.2k-10k to VCC)");
                            result.AppendLine("  2. Is I2C device powered?");
                            result.AppendLine("  3. Is AD1 shorted to GND?");
                        }
                    }
                }
                
                // Return to idle state
                buffer.Clear();
                buffer.Add(0x80);
                buffer.Add(0x03); // SCL=1, SDA=1
                buffer.Add(0x03); // Both output
                buffer.Add(0x87);
                _ftdi.Write(buffer.ToArray(), buffer.Count, ref bytesWritten);
                
                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Exception: {ex.Message}";
            }
        }

        public void Dispose()
        {
            if (_ftdi != null && _ftdi.IsOpen)
            {
                _ftdi.Close();
            }
            _isInitialized = false;
            _currentDeviceIndex = -1;
        }
    }
}
