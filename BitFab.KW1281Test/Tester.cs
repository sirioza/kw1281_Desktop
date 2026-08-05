using BitFab.KW1281Test.Actions;
using BitFab.KW1281Test.Cluster;
using BitFab.KW1281Test.Enums;
using BitFab.KW1281Test.Interface;
using BitFab.KW1281Test.Interface.EDC15;
using BitFab.KW1281Test.Models;
using System.Drawing;
using System.Text.RegularExpressions;

namespace BitFab.KW1281Test
{
    internal class Tester
    {
        private readonly Messenger Mc = Messenger.Instance;
        private readonly DataSender Ds = DataSender.Instance;

        private readonly IKwpCommon _kwpCommon;
        private readonly IKW1281Dialog _kwp1281;
        private readonly int _controllerAddress;

        public Tester(IInterface @interface, int controllerAddress)
        {
            _kwpCommon = new KwpCommon(@interface);
            _kwp1281 = new KW1281Dialog(_kwpCommon);
            _controllerAddress = controllerAddress;
        }

        public ControllerInfo Kwp1281Wakeup(bool evenParityWakeup = false, bool failQuietly = false)
        {
            Mc.AddLine("Sending wakeup message");

            var kwpVersion = _kwpCommon.WakeUp((byte)_controllerAddress, evenParityWakeup, failQuietly);

            if (kwpVersion != 1281)
            {
                throw new UnexpectedProtocolException("Expected KWP1281 protocol.");
            }

            var ecuInfo = _kwp1281.Connect();
            Mc.AddLine($"ECU: {ecuInfo}");
            return ecuInfo;
        }

        public KW2000Dialog Kwp2000Wakeup(bool evenParityWakeup = false)
        {
            Mc.AddLine("Sending wakeup message");

            var kwpVersion = _kwpCommon!.WakeUp((byte)_controllerAddress, evenParityWakeup);

            if (kwpVersion == 1281)
            {
                throw new UnexpectedProtocolException("Expected KWP2000 protocol.");
            }

            var kwp2000 = new KW2000Dialog(_kwpCommon, (byte)_controllerAddress);

            return kwp2000;
        }

        public void EndCommunication()
        {
            _kwp1281.EndCommunication();
        }

        public async Task ActuatorTestAsync(ActuatorTestControl control)
        {
            using KW1281KeepAlive keepAlive = new(_kwp1281);

            while (!control.StopRequested)
            {
                var response = keepAlive.ActuatorTest(0x00);

                if (response == null || response.ActuatorName == "End")
                {
                    Ds.Send($"Actuator Test: End");
                    break;
                }

                Ds.Send($"Actuator Test: {response.ActuatorName}");

                bool next = await control.WaitForNextStepAsync();
                if (!next)
                {
                    break;
                }
            }
        }

        public void AdaptationRead(byte channel, ushort? login, int workshopCode)
        {
            if (login.HasValue)
            {
                _kwp1281.Login(login.Value, workshopCode);
            }

            if (_kwp1281.AdaptationRead(channel))
            {
                Ds.Send("Adaptation is OK");
            }
            else
            {
                Ds.Send("Adaptation is out of range.");
            }
        }

        public void AdaptationSave(byte channel, ushort channelValue, ushort? login, int workshopCode)
        {
            if (login.HasValue)
            {
                _kwp1281.Login(login.Value, workshopCode);
            }

            if (_kwp1281.AdaptationSave(channel, channelValue, workshopCode))
            {
                Ds.Send("Adaptation is saved");
            }
            else
            {
                Ds.Send("Adaptation is not saved.");
            }
        }

        public void AdaptationTest(byte channel, ushort channelValue, ushort? login, int workshopCode)
        {
            if (login.HasValue)
            {
                _kwp1281.Login(login.Value, workshopCode);
            }

            if (_kwp1281.AdaptationTest(channel, channelValue))
            {
                Ds.Send("Tested value is OK");
            }
            else
            {
                Ds.Send("Test failed.");
            }
        }

        public void BasicSettingRead(byte groupNumber)
        {
            _kwp1281.GroupRead(groupNumber, true);
        }

        public void ClarionVWPremium4SafeCode()
        {
            if (_controllerAddress != (int)ControllerAddress.Radio)
            {
                Mc.AddLine("Only supported for radio address 56", Color.Red);
                return;
            }

            // Thanks to Mike Naberezny for this (https://github.com/mnaberez)
            const byte readWriteSafeCode = 0xF0;
            const byte read = 0x00;
            _kwp1281.SendBlock([readWriteSafeCode, read]);

            var blocks = _kwp1281.ReceiveBlocks();
            var block = blocks.FirstOrDefault(b => !b.IsAckNak);
            if (block == null)
            {
                Mc.AddLine("No response received from radio.", Color.Red);
            }
            else if (block.Title != readWriteSafeCode)
            {
                Mc.AddLine($"Unexpected response received from radio. Block title: ${block.Title:X2}", Color.Red);
            }
            else
            {
                var safeCode = block.Body[0] * 256 + block.Body[1];
                Ds.Send($"Safe code: {safeCode:X4}");
            }
        }

        public void ClearFaultCodes()
        {
            var faultCodes = _kwp1281.ClearFaultCodes(_controllerAddress);

            if (faultCodes != null)
            {
                if (faultCodes.Count == 0)
                {
                    Ds.Send("Fault codes cleared.");
                }
                else
                {
                    Ds.Send(faultCodes.Select(code => code.ToString()));
                }
            }
            else
            {
                Ds.Error("Failed to clear fault codes.");
            }
        }

        public void DelcoVWPremium5SafeCode()
        {
            if (_controllerAddress != (int)ControllerAddress.RadioManufacturing)
            {
                Mc.AddLine("Only supported for radio manufacturing address 7C", Color.Red);
                return;
            }

            // Thanks to Mike Naberezny for this (https://github.com/mnaberez)
            const string secret = "DELCO";
            var code = (ushort)(secret[4] * 256 + secret[3]);
            var workshopCode = secret[2] * 65536 + secret[1] * 256 + secret[0];

            _kwp1281.Login(code, workshopCode);
            var bytes = _kwp1281.ReadRomEeprom(0x0014, 2);

            Ds.Send($"Safe code: {bytes[0]:X2}{bytes[1]:X2}");
        }

        public void DumpCcmRom(string filename)
        {
            if (_controllerAddress != (int)ControllerAddress.CCM &&
                _controllerAddress != (int)ControllerAddress.CentralLocking)
            {
                Ds.Error("Only supported for CCM and Central Locking.");
                return;
            }

            UnlockControllerForEepromReadWrite();

            var dumpFileName = Path.Combine(filename, "ccm_rom_dump.bin");
            const byte blockSize = 8;

            Mc.AddLine($"Saving CCM ROM to {dumpFileName}");

            var succeeded = true;
            using (var fs = File.Create(dumpFileName, blockSize, FileOptions.WriteThrough))
            {
                for (var seg = 0; seg < 16; seg++)
                {
                    for (var msb = 0; msb < 16; msb++)
                    {
                        for (var lsb = 0; lsb < 256; lsb += blockSize)
                        {
                            var blockBytes = _kwp1281.ReadCcmRom((byte)seg, (byte)msb, (byte)lsb, blockSize);
                            if (blockBytes == null)
                            {
                                blockBytes = [.. Enumerable.Repeat((byte)0, blockSize)];
                                succeeded = false;
                            }
                            else if (blockBytes.Count < blockSize)
                            {
                                blockBytes.AddRange(Enumerable.Repeat((byte)0, blockSize - blockBytes.Count));
                                succeeded = false;
                            }

                            fs.Write([.. blockBytes], 0, blockBytes.Count);
                            fs.Flush();
                        }
                    }
                }
            }

            if (!succeeded)
            {
                Ds.Error("Warning: Some bytes could not be read and were replaced with 0.");
            }

            Ds.Send($"Saved Ccm Rom dump to {dumpFileName}.");
        }

        public void DumpClusterNecRom(string filename)
        {
            if (_controllerAddress != (int)ControllerAddress.Cluster)
            {
                Ds.Error("Only supported for cluster.");
                return;
            }

            var dumpFileName = Path.Combine(filename, "cluster_nec_rom_dump.bin");
            const byte blockSize = 16;

            Mc.AddLine($"Saving cluster NEC ROM to {dumpFileName}");

            bool succeeded = true;
            using (var fs = File.Create(dumpFileName, blockSize, FileOptions.WriteThrough))
            {
                var cluster = new VdoCluster(_kwp1281);

                for (int address = 0; address < 65536; address += blockSize)
                {
                    var blockBytes = cluster.CustomReadNecRom((ushort)address, blockSize);
                    if (blockBytes == null)
                    {
                        blockBytes = [.. Enumerable.Repeat((byte)0, blockSize)];
                        succeeded = false;
                    }
                    else if (blockBytes.Count < blockSize)
                    {
                        blockBytes.AddRange([.. Enumerable.Repeat((byte)0, blockSize - blockBytes.Count)]);
                        succeeded = false;
                    }

                    fs.Write([.. blockBytes], 0, blockBytes.Count);
                    fs.Flush();
                }
            }

            if (!succeeded)
            {
                Ds.Error("Warning: Some bytes could not be read and were replaced with 0.");
            }

            Ds.Send($"Saved Cluster Nec Rom dump to {dumpFileName}.");
        }

        public void FindLogins(ushort goodLogin, int workshopCode)
        {
            const int start = 0;
            for (int login = start; login <= 65535; login++)
            {
                _kwp1281.Login(goodLogin, workshopCode);

                try
                {
                    Mc.Add($"Trying {login:D5}");
                    _kwp1281.Login((ushort)login, workshopCode);

                    Ds.Send($"{login:D5} succeeded");

                    continue;
                }
                catch(TimeoutException)
                {
                    _kwp1281.SetDisconnected();
                    try
                    {
                        Kwp1281Wakeup();
                    }
                    catch(InvalidOperationException)
                    {
                        _kwp1281.SetDisconnected();
                        Kwp1281Wakeup();
                    }
                }
            }
        }

        public byte[] ReadWriteEdc15Eeprom(string filename, List<KeyValuePair<ushort, byte>>? addressValuePairs = null)
        {
            _kwp1281.EndCommunication();

            Thread.Sleep(1000);

            // Now wake it up again, hopefully in KW2000 mode
            _kwpCommon!.Interface.SetBaudRate(10400);
            var kwpVersion = _kwpCommon.WakeUp((byte)_controllerAddress, evenParity: false);
            if (kwpVersion < 2000)
            {
                throw new InvalidOperationException(
                    $"Unable to wake up ECU in KW2000 mode. KW version: {kwpVersion}");
            }
            Mc.Add($"KW Version: {kwpVersion}");

            var edc15 = new Edc15VM(_kwpCommon, _controllerAddress);

            var dumpFileName = Path.Combine(filename, $"EDC15_EEPROM.bin");

            var eeprom = edc15.ReadWriteEeprom(dumpFileName, addressValuePairs);

            Ds.Send("The values at addresses have been written.");

            return eeprom;
        }

        public void DumpEeprom(uint address, uint length, string filename)
        {
            switch (_controllerAddress)
            {
                case (int)ControllerAddress.Cluster:
                    DumpClusterEeprom((ushort)address, (ushort)length, filename);
                    break;
                case (int)ControllerAddress.CCM:
                case (int)ControllerAddress.CentralElectric:
                case (int)ControllerAddress.CentralLocking:
                    DumpCcmEeprom((ushort)address, (ushort)length, filename);
                    break;
                default:
                    Mc.AddLine("Only supported for cluster, CCM, Central Locking and Central Electric");
                    break;
            }
        }

        public void DumpMarelliMem(uint address, uint length, ControllerInfo ecuInfo, string path)
        {
            if (_controllerAddress != (int)ControllerAddress.Cluster)
            {
                Ds.Error("Only supported for clusters");
                return;
            }

            ICluster cluster = new MarelliCluster(_kwp1281, ecuInfo.Text);
            string dumpFileName = cluster.DumpEeprom(address, length, path);

            Ds.Send($"Saved Marelli Mem dump to {dumpFileName}");
        }

        public void DumpMem(uint address, uint length, string filename)
        {
            if (_controllerAddress != (int)ControllerAddress.Cluster)
            {
                Ds.Error("Only supported for cluster");
                return;
            }

            DumpClusterMem(address, length, filename);
        }

        public void DumpRam(uint startAddr, uint length, string path)
        {
            UnlockControllerForEepromReadWrite();

            const int maxReadLength = 8;
            bool succeeded = true;
            string dumpFileName = Path.Combine(path, $"ram_${startAddr:X4}.bin");

            using (var fs = File.Create(dumpFileName, maxReadLength, FileOptions.WriteThrough))
            {
                for (uint addr = startAddr; addr < (startAddr + length); addr += maxReadLength)
                {
                    var readLength = (byte)Math.Min(startAddr + length - addr, maxReadLength);
                    var blockBytes = _kwp1281.ReadRam((ushort)addr, readLength);
                    if (blockBytes == null)
                    {
                        blockBytes = [.. Enumerable.Repeat((byte)0, readLength)];
                        succeeded = false;
                    }
                    fs.Write([.. blockBytes], 0, blockBytes.Count);
                    fs.Flush();
                }
            }

            if (!succeeded)
            {
                Ds.Error("Warning: Some bytes could not be read and were replaced with 0.");
            }

            Ds.Send($"RAM dump saved to {dumpFileName}.");
        }

        public void DumpRom(uint startAddr, uint length, string path)
        {
            UnlockControllerForEepromReadWrite();

            const int maxReadLength = 8;
            bool succeeded = true;

            string dumpFileName = Path.Combine(path, $"rom_0x{startAddr:X4}.bin");

            using (var fs = File.Create(dumpFileName, maxReadLength, FileOptions.WriteThrough))
            {
                for (uint addr = startAddr; addr < (startAddr + length); addr += maxReadLength)
                {
                    var readLength = (byte)Math.Min(startAddr + length - addr, maxReadLength);
                    var blockBytes = _kwp1281.ReadRomEeprom((ushort)addr, readLength);
                    if (blockBytes == null)
                    {
                        blockBytes = [.. Enumerable.Repeat((byte)0, readLength)];
                        succeeded = false;
                    }
                    fs.Write([.. blockBytes], 0, blockBytes.Count);
                    fs.Flush();
                }
            }

            if (!succeeded)
            {
                Ds.Error("Warning: Some bytes could not be read and were replaced with 0.");
            }

            Ds.Send($"ROM dump saved to {dumpFileName}.");
        }

        /// <summary>
        /// Dumps the memory of a Bosch RB4/RB8 cluster to a file.
        /// </summary>
        /// <returns>The dump file name or null if the EEPROM was not dumped.</returns>
        public string? DumpRBxMem(uint address, uint length, string fileDirectory, bool evenParityWakeup = true)
        {
            if (_controllerAddress != (int)ControllerAddress.Cluster)
            {
                Ds.Error("Only supported for cluster (address 17)");
                return null;
            }

            var kwp2000 = Kwp2000Wakeup(evenParityWakeup);

            var dumpFilePath = Path.Combine(fileDirectory, $"RBx_${address:X6}_mem.bin");

            ICluster cluster = new BoschRBxCluster(kwp2000);
            cluster.UnlockForEepromReadWrite();
            var dumpFileName = cluster.DumpEeprom(address, length, dumpFilePath);

            Ds.Send($"Saved RBx Mem dump to {dumpFileName}");

            return dumpFilePath;
        }

        /// <summary>
        /// Connects to the cluster and gets its unique ID. This is normally done by the radio in
        /// order to detect if its been moved to a different vehicle.
        /// </summary>
        public void GetClusterId()
        {
#if false
            if (_controllerAddress != 0x3F)
            {
                Mc.AddLine("Only supported for special cluster address $3F");
                return;
            }
#endif

            _kwp1281.SendBlock(
            [
                (byte)BlockTitle.SecurityAccessMode1,

                // The radio would send 4 random values for obfuscation, but the cluster ignores
                // them so we'll just send 0's.
                0x00, 0x00, 0x00, 0x00 // Challenge
            ]);

            var blocks = _kwp1281.ReceiveBlocks();
            var block = blocks.FirstOrDefault(b => !b.IsAckNak);
            if (block == null)
            {
                Mc.AddLine("No response received from cluster.");
            }
            else if (block.Title != (byte)BlockTitle.SecurityAccessMode2)
            {
                Mc.AddLine(
                    $"Unexpected response received from cluster. Block title: ${block.Title:X2}");
            }
            else
            {
                (byte id1, byte id2) = DecodeClusterId(block.Body[0], block.Body[1], block.Body[2], block.Body[3]);
                Mc.AddLine($"Cluster Id: ${id1:X2} ${id2:X2}");
            }
        }

        public void GetSkc()
        {
            if (_controllerAddress is (int)ControllerAddress.Cluster or (int)ControllerAddress.Immobilizer)
            {
                var ecuInfo = Kwp1281Wakeup();
                if (ecuInfo.Text.Contains("4B0920") ||
                    ecuInfo.Text.Contains("4Z7920") ||
                    ecuInfo.Text.Contains("8D0920") ||
                    ecuInfo.Text.Contains("8Z0920"))
                {
                    var family = ecuInfo.Text[..2] switch
                    {
                        "8D" => "A4",
                        "8Z" => "A2",
                        _ => "C5"
                    };

                    Mc.AddLine($"Cluster is Audi {family}");

                    var cluster = new AudiC5Cluster(_kwp1281);

                    cluster.UnlockForEepromReadWrite();
                    var dumpFileName = cluster.DumpEeprom(0, 0x800, $"Audi{family}.bin");

                    var buf = File.ReadAllBytes(dumpFileName);

                    var skc = Utils.GetShort(buf, 0x7E2);
                    var skc2 = Utils.GetShort(buf, 0x7E4);
                    var skc3 = Utils.GetShort(buf, 0x7E6);
                    if (skc != skc2 || skc != skc3)
                    {
                        Ds.Error($"Warning: redundant SKCs do not match: {skc:D5} {skc2:D5} {skc3:D5}");
                    }
                    else
                    {
                        Ds.Send(skc);
                    }
                }
                else if (ecuInfo.Text.Contains("VDO"))
                {
                    VdoCluster cluster = new(_kwp1281);
                    string[] partNumberGroups = FindAndParsePartNumber(ecuInfo.Text);
                    if (partNumberGroups.Length == 4)
                    {
                        string dumpFileName;
                        ushort startAddress;
                        byte[] buf;
                        ushort? skc;
                        if (partNumberGroups[1] == "919") // Non-CAN
                        {
                            startAddress = 0x1FA;
                            dumpFileName = DumpClusterEeprom(startAddress, length: 6, fileDirectory: string.Empty);
                            buf = File.ReadAllBytes(dumpFileName);
                            skc = Utils.GetBcd(buf, 0);
                            ushort skc2 = Utils.GetBcd(buf, 2);
                            ushort skc3 = Utils.GetBcd(buf, 4);
                            if (skc != skc2 || skc != skc3)
                            {
                                Ds.Error($"Warning: redundant SKCs do not match: {skc:D5} {skc2:D5} {skc3:D5}");
                            }
                        }
                        else if (partNumberGroups[1] == "920") // CAN
                        {
                            startAddress = 0x90;
                            dumpFileName = DumpClusterEeprom(startAddress, length: 0x7C, fileDirectory: string.Empty);
                            buf = File.ReadAllBytes(dumpFileName);
                            skc = VdoCluster.GetSkc(buf, startAddress);
                        }
                        else
                        {
                            Ds.Error($"Unknown cluster: {ecuInfo.Text}");
                            return;
                        }

                        if (skc.HasValue)
                        {
                            Ds.Send(skc.Value);
                        }
                        else
                        {
                            Ds.Error("Unable to determine SKC.");
                        }
                    }
                    else
                    {
                        Ds.Error($"Unknown cluster: {ecuInfo.Text}");
                    }
                }
                else if (ecuInfo.Text.Contains("RB4"))
                {
                    // Need to quit KWP1281 before switching to KWP2000
                    _kwp1281.EndCommunication();
                    Thread.Sleep(TimeSpan.FromSeconds(2));

                    var dumpFileName = DumpRBxMem(0x10046, 2, fileDirectory: null!);
                    var buf = File.ReadAllBytes(dumpFileName!);
                    if (buf.Length == 2)
                    {
                        ushort skc = Utils.GetShort(buf, 0);
                        Ds.Send(skc);
                    }
                    else
                    {
                        Ds.Error("Unable to read SKC. Cluster not in New mode (4)?");
                    }
                }
                else if (ecuInfo.Text.Contains("RB8"))
                {
                    // Need to quit KWP1281 before switching to KWP2000
                    _kwp1281.EndCommunication();
                    Thread.Sleep(TimeSpan.FromSeconds(2));

                    var dumpFileName = DumpRBxMem(0x1040E, 2, fileDirectory: null!);
                    var buf = File.ReadAllBytes(dumpFileName!);
                    ushort skc = Utils.GetShort(buf, 0);
                    Ds.Send(skc);
                }
                else if (ecuInfo.Text.Contains("M73"))
                {
                    string dumpFileName = new MarelliCluster(_kwp1281, ecuInfo.Text).DumpEeprom(null, null, string.Empty);
                    byte[] buf = File.ReadAllBytes(dumpFileName);
                    ushort? skc = MarelliCluster.GetSkc(buf);
                    if (skc.HasValue)
                    {
                        Ds.Send(skc.Value);
                    }
                    else
                    {
                        Ds.Error($"Unable to determine SKC for cluster: {ecuInfo.Text}");
                    }
                }
                else if (ecuInfo.Text.Contains("BOO"))
                {
                    new MotometerBOOCluster(_kwp1281!).UnlockForEepromReadWrite();

                    var dumpFileName = DumpBOOClusterEeprom(0, 0x10, string.Empty);

                    var buf = File.ReadAllBytes(dumpFileName);
                    var skc = Utils.GetBcd(buf, 0x08);
                    Ds.Send(skc);
                }
                else if (ecuInfo.Text.Contains("VWZ3Z0"))
                {
                    // IMMO BOX 1 1H0 953 257 and 7M0 953 257 support based on sniffed communication.
                    // 7M0 953 257 can be both IMMO BOX 1 or IMMO BOX 2.

                    var blockBytes = _kwp1281.ReadRomEeprom(0x0190, 176);
                    if (blockBytes == null)
                    {
                        Ds.Error("ROM read failed");
                        return;
                    }
                    else if (blockBytes.Count == 0)
                    {

                        if (ecuInfo.Text.Contains("1H0"))
                        {
                            Ds.Error("Failed to read SKC. Immo appears to be locked. You have to use an adapted key.");
                            return;
                        }
                        else if (ecuInfo.Text.Contains("6H0") || ecuInfo.Text.Contains("7M0"))
                        {
                            // This part adds IMMO BOX 2 experimental support (could not test this with real box).
                            // Should work for 6H0 953 257 and 7M0 953 257

                            Ds.Error("Trying to unlock IMMO BOX 2. This function is experimental and may not work...");

                            // Unlock ROM
                            _kwp1281.SendBlock([0xCB, 0x5D, 0x3B, 0xD3, 0x8A]);

                            // Send custom read command
                            blockBytes = _kwp1281.ReadSecureImmoAccess([0x02, 0x00, 0x65, 0x34, 0x9D]);

                            if (blockBytes == null || blockBytes.Count == 0)
                            {
                                Ds.Error("Failed to read SKC. Immo appears to be locked. You have to use an adapted key.");
                                return;
                            }
                        }
                        else
                        {
                            Ds.Error("Failed to read SKC for non 1H0/6H0/7M0 ECU.");
                            return;
                        }
                    }

                    var skc = Utils.GetShortBE([.. blockBytes], 1);
                    Ds.Send(skc);
                }
                else if (ecuInfo.Text.Contains("AGD"))
                {
                    Ds.Error($"Unsupported Magneti Marelli AGD cluster: {ecuInfo.Text}");
                }
                else
                {
                    Ds.Error($"Unsupported cluster: {ecuInfo.Text}");
                }
            }
            else if (_controllerAddress == (int)ControllerAddress.Ecu)
            {
                var ecuInfo = Kwp1281Wakeup();
                var eeprom = ReadWriteEdc15Eeprom(filename: string.Empty);
                Edc15VM.DisplayEepromInfo(eeprom);
            }
            else
            {
                Ds.Error(
                    "GetSKC only supported for clusters (address 17), Immo boxes (address 25) and ECUs (address 1)");
            }
        }

        /// <summary>
        /// Takes the info returned when connecting to the ECU, finds the ECU part number and
        /// splits into its components. For example, if the ECU info is this:
        ///     "1J5920926CX   KOMBI+WEGFAHRSP VDO V01"
        /// Then the part number would be identified as "1J5920926CX", which would be split into
        /// its 4 components: "1J5", "920", "926", "CX"
        /// </summary>
        /// <param name="ecuInfo"></param>
        /// <returns>A 4-element string array if the part number was found, otherwise an empty
        /// string array.</returns>
        internal static string[] FindAndParsePartNumber(string ecuInfo)
        {
            var match = Regex.Match(
                ecuInfo,
                "\\b(\\d[a-zA-Z][0-9a-zA-Z])(9\\d{2})(\\d{3})([a-zA-Z]{0,2})\\b");

            if (match.Success)
            {
                return [.. (match.Groups as IReadOnlyList<Group>).Skip(1).Select(g => g.Value)];
            }
            else
            {
                return [];
            }
        }

        public void GroupRead(byte groupNumber)
        {
            _kwp1281.GroupRead(groupNumber);
        }

        public void LoadEeprom(uint address, string filename)
        {
            switch (_controllerAddress)
            {
                case (int)ControllerAddress.Cluster:
                    LoadClusterEeprom((ushort)address, filename);
                    break;
                case (int)ControllerAddress.CCM:
                case (int)ControllerAddress.CentralElectric:
                case (int)ControllerAddress.CentralLocking:
                    LoadCcmEeprom((ushort)address, filename);
                    break;
                default:
                    Ds.Error("Only supported for cluster, CCM, Central Locking and Central Electric.");
                    break;
            }
        }

        public void MapEeprom(string filename)
        {
            switch (_controllerAddress)
            {
                case (int)ControllerAddress.Cluster:
                    MapClusterEeprom(filename);
                    break;
                case (int)ControllerAddress.CCM:
                case (int)ControllerAddress.CentralElectric:
                case (int)ControllerAddress.CentralLocking:
                    MapCcmEeprom(filename);
                    break;
                default:
                    Ds.Error("Only supported for cluster, CCM, Central Locking and Central Electric.");
                    break;
            }
        }

        public void ReadEeprom(uint address)
        {
            UnlockControllerForEepromReadWrite();

            var blockBytes = _kwp1281.ReadEeprom((ushort)address, 1);
            if (blockBytes == null)
            {
                Ds.Error("EEPROM read failed.");
            }
            else
            {
                var value = blockBytes[0];
                Ds.Send($"Address {address} (${address:X4}): Value {value} (${value:X2}).");
            }
        }

        public void ReadRam(uint address)
        {
            UnlockControllerForEepromReadWrite();

            var blockBytes = _kwp1281.ReadRam((ushort)address, 1);
            if (blockBytes == null)
            {
                Ds.Error("RAM read failed");
            }
            else
            {
                var value = blockBytes[0];
                Ds.Send($"Address {address} (${address:X4}): Value {value} (${value:X2}).");
            }
        }

        public void ReadRom(uint address)
        {
            UnlockControllerForEepromReadWrite();

            var blockBytes = _kwp1281.ReadRomEeprom((ushort)address, 1);
            if (blockBytes == null)
            {
                Ds.Error("ROM read failed");
            }
            else
            {
                var value = blockBytes[0];
                Ds.Send($"Address {address} (${address:X4}): Value {value} (${value:X2}).");
            }
        }

        public void ReadFaultCodes()
        {
            List<Blocks.FaultCode>? faultCodes = _kwp1281.ReadFaultCodes();
            if (faultCodes != null && faultCodes.Count > 0)
            {
                Ds.Send(faultCodes.Select(code => code.ToString()));
            }
            else
            {
                Ds.Send("Fault codes not found.");
            }
        }

        public void ReadIdent()
        {
            List<string> ident = [];

            foreach (ControllerIdent identInfo in _kwp1281.ReadIdent())
            {
                ident.Add(identInfo.ToString());
            }

            Ds.Send(ident);
        }

        public void ReadSoftwareVersion()
        {
            if (_controllerAddress == (int)ControllerAddress.Cluster)
            {
                var cluster = new VdoCluster(_kwp1281);
                var softwareVersion = cluster.CustomReadSoftwareVersion();

                Ds.Send(softwareVersion.Select(v => v.Item3).ToList());
            }
            else
            {
                Ds.Error("Only supported for cluster.");
            }
        }

        public void Reset()
        {
            if (_controllerAddress == (int)ControllerAddress.Cluster)
            {
                var cluster = new VdoCluster(_kwp1281);
                cluster.CustomReset();
                Ds.Send("Reset completed.");
            }
            else
            {
                Ds.Error("Only supported for cluster.");
            }
        }

        public void SetSoftwareCoding(int softwareCoding, int workshopCode)
        {
            var succeeded = _kwp1281.SetSoftwareCoding(_controllerAddress, softwareCoding, workshopCode);
            if (succeeded)
            {
                Ds.Send("Software coding set.");
            }
            else
            {
                Ds.Error("Failed to set software coding.");
            }
        }

        public void ToggleRB4Mode()
        {
            var kwp2000 = Kwp2000Wakeup(evenParityWakeup: true);

            BoschRBxCluster cluster = new(kwp2000);
            cluster.UnlockForEepromReadWrite();

            if (cluster.ToggleRB4Mode())
            {
                Ds.Send("RB4 mode was toggled");
            }
            else
            {
                Ds.Error("Failed to toggle RB4 mode");
            }
        }

        public void WriteEeprom(uint address, byte value)
        {
            UnlockControllerForEepromReadWrite();

            if(_kwp1281.WriteEeprom((ushort)address, [value]))
            {
                Ds.Send("The value at address has been written.");
            }
            else
            {
                Ds.Error("The write operation failed.");
            }
        }

        // End top-level commands

        private string DumpBOOClusterEeprom(ushort startAddress, ushort length, string path)
        {
            var ident = _kwp1281.ReadIdent();

            var identInfo = ident.First().ToString()
                .Split(Environment.NewLine).First() // Sometimes ReadIdent() can return multiple lines
                .Replace(' ', '_');

            var dumpFileName = Path.Combine(path, $"{identInfo}_${startAddress:X4}_eeprom.bin");

            Mc.AddLine($"Saving EEPROM dump to {dumpFileName}");
            DumpEeprom(startAddress, length, maxReadLength: 16, dumpFileName);
            Mc.AddLine($"Saved EEPROM dump to {dumpFileName}");

            return dumpFileName;
        }

        private string DumpClusterEeprom(ushort startAddress, ushort length, string fileDirectory)
        {
            List<ControllerIdent> ident = _kwp1281.ReadIdent();

            var identInfo = ident.First().ToString()
                .Split(Environment.NewLine).First() // Sometimes ReadIdent() can return multiple lines
                .Replace(' ', '_').Replace(":", string.Empty);

            ICluster cluster = new VdoCluster(_kwp1281);
            cluster.UnlockForEepromReadWrite();

            var dumpFilePath = Path.Combine(fileDirectory, $"{identInfo}_${startAddress:X4}_eeprom.bin");

            Mc.AddLine($"Saving EEPROM dump to {dumpFilePath}");
            cluster.DumpEeprom(startAddress, length, dumpFilePath);
            Ds.Send($"Saved Eeprom dump to {dumpFilePath}");

            return dumpFilePath;
        }

        private void MapCcmEeprom(string filename)
        {
            UnlockControllerForEepromReadWrite();

            var bytes = new List<byte>();
            const byte blockSize = 1;
            for (int addr = 0; addr <= 65535; addr += blockSize)
            {
                var blockBytes = _kwp1281.ReadEeprom((ushort)addr, blockSize);
                blockBytes = [.. Enumerable.Repeat(blockBytes == null ? (byte)0 : (byte)0xFF, blockSize)];
                bytes.AddRange(blockBytes);
            }
            var dumpFileName = Path.Combine(filename, "ccm_eeprom_map.bin");
            Mc.AddLine($"Saving EEPROM map to {dumpFileName}");
            File.WriteAllBytes(dumpFileName, [.. bytes]);

            Ds.Send($"Saved Ccm Eeprom map to {dumpFileName}");
        }

        private void MapClusterEeprom(string filename)
        {
            var cluster = new VdoCluster(_kwp1281);

            var map = cluster.MapEeprom();

            var mapFileName = Path.Combine(filename, "eeprom_map.bin");
            Mc.AddLine($"Saving EEPROM map to {mapFileName}");
            File.WriteAllBytes(mapFileName, [.. map]);

            Ds.Send($"Saved EEPROM map to {mapFileName}");
        }

        private void DumpCcmEeprom(ushort startAddress, ushort length, string filename)
        {
            UnlockControllerForEepromReadWrite();

            var dumpFileName = Path.Combine(filename, $"ccm_eeprom_${startAddress:X4}.bin");

            Mc.AddLine($"Saving EEPROM dump to {dumpFileName}");
            DumpEeprom(startAddress, length, maxReadLength: 8, dumpFileName);
            Mc.AddLine($"Saved EEPROM dump to {dumpFileName}");
        }

        private void UnlockControllerForEepromReadWrite()
        {
            switch ((ControllerAddress)_controllerAddress)
            {
                case ControllerAddress.CCM:
                case ControllerAddress.CentralLocking:
                    _kwp1281.Login(
                        code: 19283,
                        workshopCode: 222); // This is what VDS-PRO uses
                    break;

                case ControllerAddress.CentralElectric:
                    _kwp1281.Login(
                        code: 21318,
                        workshopCode: 222); // This is what VDS-PRO uses
                    break;

                case ControllerAddress.Cluster:
                    // TODO:UnlockCluster() is only needed for EEPROM read, not memory read
                    var cluster = new VdoCluster(_kwp1281);
                    var (isUnlocked, softwareVersion) = cluster.Unlock();
                    if (!isUnlocked)
                    {
                        Mc.AddLine("Unknown cluster software version. EEPROM access will likely fail.");
                    }

                    if (!cluster.RequiresSeedKey())
                    {
                        Mc.AddLine("Cluster is unlocked for ROM/EEPROM access. Skipping Seed/Key login.");
                        return;
                    }

                    cluster.SeedKeyAuthenticate(softwareVersion);
                    if (cluster.RequiresSeedKey())
                    {
                        Mc.AddLine("Failed to unlock cluster.");
                    }
                    else
                    {
                        Mc.AddLine("Cluster is unlocked for ROM/EEPROM access.");
                    }
                    break;
            }
        }

        private void DumpEeprom(ushort startAddr, ushort length, byte maxReadLength, string fileName)
        {
            bool succeeded = true;

            using (var fs = File.Create(fileName, maxReadLength, FileOptions.WriteThrough))
            {
                for (uint addr = startAddr; addr < (startAddr + length); addr += maxReadLength)
                {
                    var readLength = (byte)Math.Min(startAddr + length - addr, maxReadLength);
                    var blockBytes = _kwp1281.ReadEeprom((ushort)addr, readLength);
                    if (blockBytes == null)
                    {
                        blockBytes = [.. Enumerable.Repeat((byte)0, readLength)];
                        succeeded = false;
                    }
                    fs.Write([.. blockBytes], 0, blockBytes.Count);
                    fs.Flush();
                }
            }

            if (!succeeded)
            {
                Mc.Add("Warning: Some bytes could not be read and were replaced with 0.");
            }
        }

        private void WriteEeprom(
            ushort startAddr, byte[] bytes, uint maxWriteLength)
        {
            var succeeded = true;
            var length = bytes.Length;
            for (uint addr = startAddr; addr < (startAddr + length); addr += maxWriteLength)
            {
                var writeLength = (byte)Math.Min(startAddr + length - addr, maxWriteLength);
                if (!_kwp1281.WriteEeprom((ushort)addr, [.. bytes.Skip((int)(addr - startAddr)).Take(writeLength)]))
                {
                    succeeded = false;
                }
            }

            if (!succeeded)
            {
                Mc.AddLine("EEPROM write failed. You should probably try again.");
            }
        }

        private void LoadCcmEeprom(ushort address, string filename)
        {
            _ = _kwp1281.ReadIdent();

            UnlockControllerForEepromReadWrite();

            if (!File.Exists(filename))
            {
                Ds.Error($"File {filename} does not exist.");
                return;
            }

            Mc.AddLine($"Reading {filename}");
            var bytes = File.ReadAllBytes(filename);

            Mc.AddLine("Writing to cluster...");
            WriteEeprom(address, bytes, 8);

            Ds.Send("CCM EEPROM write completed.");
        }

        private void LoadClusterEeprom(ushort address, string filename)
        {
            _ = _kwp1281.ReadIdent();

            UnlockControllerForEepromReadWrite();

            if (!File.Exists(filename))
            {
                Ds.Error($"File {filename} does not exist.");
                return;
            }

            Mc.AddLine($"Reading {filename}");
            var bytes = File.ReadAllBytes(filename);

            Mc.AddLine("Writing to cluster...");
            WriteEeprom(address, bytes, 16);

            Ds.Send("Cluster EEPROM write completed.");
        }

        private void DumpClusterMem(uint startAddress, uint length, string path)
        {
            var cluster = new VdoCluster(_kwp1281);
            if (!cluster.RequiresSeedKey())
            {
                Mc.AddLine("Cluster is unlocked for memory access. Skipping Seed/Key login.");
            }
            else
            {
                var (isUnlocked, softwareVersion) = cluster.Unlock();
                if (!isUnlocked)
                {
                    Mc.AddLine("Unknown cluster software version. Memory access will likely fail.");
                }
                cluster.SeedKeyAuthenticate(softwareVersion);
            }

            var dumpFileName = Path.Combine(path, $"cluster_mem_${startAddress:X6}.bin");
            Mc.AddLine($"Saving memory dump to {dumpFileName}");

            cluster.DumpMem(dumpFileName, startAddress, length);

            Ds.Send($"Saved memory dump to {dumpFileName}");
        }

        private static (byte, byte) DecodeClusterId(byte b1, byte b2, byte b3, byte b4)
        {
            // For obfuscation, the cluster adds the values below, so we need to subtract them:
            bool carry = true;
            (b1, carry) = Utils.SubtractWithCarry(b1, 0xE7, carry);
            (b2, carry) = Utils.SubtractWithCarry(b2, 0xBD, carry);
            (b3, carry) = Utils.SubtractWithCarry(b3, 0x18, carry);
            (b4, carry) = Utils.SubtractWithCarry(b4, 0x00, carry);

            b1 ^= b3;
            b2 ^= b4;

            // Count the number of 0 bits in b1 and b2

            byte zeroCount = 0;
            for (int i = 0; i < 8; i++)
            {
                if (((b1 >> i) & 1) == 0)
                {
                    zeroCount++;
                }
                if (((b2 >> i) & 1) == 0)
                {
                    zeroCount++;
                }
            }

            // Right-rotate b3 and b4 zeroCount times:
            for (int i = 0; i < zeroCount; i++)
            {
                carry = (b4 & 1) != 0;
                (b3, carry) = Utils.RightRotate(b3, carry);
                (b4, carry) = Utils.RightRotate(b4, carry);
            }

            b1 ^= b3;
            b2 ^= b4;

            return (b1, b2);
        }
    }
}