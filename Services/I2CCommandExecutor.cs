using System;
using System.Collections.Generic;
using UsbI2cController.Models;

namespace UsbI2cController.Services
{
    /// <summary>
    /// I2Cコマンドシーケンスを実行するサービス
    /// </summary>
    public class I2CCommandExecutor
    {
        private readonly FT232HI2CService _i2cService;

        public I2CCommandExecutor(FT232HI2CService i2cService)
        {
            _i2cService = i2cService;
        }

        /// <summary>
        /// I2Cコマンドシーケンスを実行
        /// </summary>
        public bool ExecuteSequence(I2CCommandSequence sequence)
        {
            if (sequence == null || sequence.Operations.Count == 0)
            {
                return false;
            }

            try
            {
                sequence.Timestamp = DateTime.Now;
                bool overallSuccess = true;

                foreach (var operation in sequence.Operations)
                {
                    bool success = false;
                    
                    // 操作ごとのデバイスアドレスを取得（未指定の場合はシーケンスのアドレスを使用）
                    byte deviceAddress = sequence.DeviceAddress;

                    switch (operation.Type)
                    {
                        case I2COperationType.Write:
                            if (operation.WriteData != null && operation.WriteData.Length > 0)
                            {
                                success = _i2cService.WriteI2C(deviceAddress, operation.WriteData);
                            }
                            break;

                        case I2COperationType.Read:
                            if (operation.ReadLength > 0)
                            {
                                byte[] data;
                                success = _i2cService.ReadI2C(deviceAddress, operation.ReadLength, out data);
                                operation.ReadData = data;
                            }
                            break;

                        case I2COperationType.Start:
                        case I2COperationType.RepeatedStart:
                        case I2COperationType.Stop:
                            // これらは通常、Write/Readメソッド内で自動的に処理される
                            success = true;
                            break;

                        case I2COperationType.Delay:
                            if (operation.DelayMilliseconds >= 13)
                            {
                                // シーケンス間の固有ディレイ12msを考慮して減算
                                int actualDelay = operation.DelayMilliseconds - 12;
                                if (actualDelay > 0)
                                {
                                    System.Threading.Thread.Sleep(actualDelay);
                                }
                                success = true;
                            }
                            break;
                    }

                    if (!success)
                    {
                        overallSuccess = false;
                        // エラーが発生しても続行するか、ここで中断するかは要件次第
                        // break; // 中断する場合
                    }
                }

                sequence.Success = overallSuccess;
                return overallSuccess;
            }
            catch (Exception)
            {
                sequence.Success = false;
                return false;
            }
        }

        /// <summary>
        /// メモリアドレス指定の読み込みシーケンスを実行（Random Read）
        /// </summary>
        public bool ExecuteRandomRead(byte deviceAddress, byte memoryAddress, int readLength, out byte[] data)
        {
            return _i2cService.ReadI2CWithAddress(deviceAddress, memoryAddress, readLength, out data);
        }

        /// <summary>
        /// メモリアドレス指定の書き込みシーケンスを実行
        /// </summary>
        public bool ExecuteMemoryWrite(byte deviceAddress, byte memoryAddress, byte[] userData)
        {
            // メモリアドレス + データを結合
            byte[] data = new byte[1 + userData.Length];
            data[0] = memoryAddress;
            Array.Copy(userData, 0, data, 1, userData.Length);
            
            return _i2cService.WriteI2C(deviceAddress, data);
        }
    }
}
