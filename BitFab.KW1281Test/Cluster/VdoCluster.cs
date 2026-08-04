using BitFab.KW1281Test.Actions;
using BitFab.KW1281Test.Blocks;
using BitFab.KW1281Test.Enums;
using System.Text;
using System.Text.RegularExpressions;

namespace BitFab.KW1281Test.Cluster
{
    internal class VdoCluster(IKW1281Dialog kwp1281) : ICluster
    {
        private readonly Messenger Mc = Messenger.Instance;

        public void UnlockForEepromReadWrite()
        {
            var (isUnlocked, softwareVersion) = Unlock();
            if (!isUnlocked)
            {
                Mc.AddLine("Unknown cluster software version. EEPROM access will likely fail.");
            }

            if (!RequiresSeedKey())
            {
                Mc.AddLine("Cluster is unlocked for ROM/EEPROM access. Skipping Seed/Key login.");
                return;
            }

            SeedKeyAuthenticate(softwareVersion);
            if (RequiresSeedKey())
            {
                Mc.AddLine("Failed to unlock cluster.");
            }
            else
            {
                Mc.AddLine("Cluster is unlocked for ROM/EEPROM access.");
            }
        }

        public string DumpEeprom(uint? optionalAddress, uint? optionalLength, string path)
        {
            var address = optionalAddress ?? 0;
            var length = optionalLength ?? 0x800;

            DumpEeprom((ushort)address, (ushort)length, maxReadLength: 16, path);

            return path;
        }

        /// <summary>
        /// http://www.maltchev.com/kiti/VAG_guide.txt
        /// </summary>
        public List<(byte, Block, string)> CustomReadSoftwareVersion()
        {
            var versionBlocks = new List<(byte, Block, string)>();

            Mc.Add("Sending Custom \"Read Software Version\" blocks");

            // The cluster can return 4 variations of software version, specified by the 2nd byte
            // of the block:
            // 0x00 - Cluster software version
            // 0x01 - Unknown
            // 0x02 - Unknown
            // 0x03 - Unknown
            for (byte variation = 0x00; variation < 0x04; variation++)
            {
                List<Block> blocks = SendCustom([0x84, variation]);
                foreach (Block? block in blocks.Where(b => !b.IsAckNak))
                {
                    string content = variation is 0x00 or 0x03
                        ? $"{variation:X2}: {DumpMixedContent(block)}"
                        : $"{variation:X2}: {DumpBinaryContent(block)}";

                    versionBlocks.Add((variation, block, content));
                }
            }

            return versionBlocks;
        }

        public void CustomReset()
        {
            Mc.AddLine("Sending Custom Reset block");
            SendCustom([0x82]);
        }

        public List<byte> CustomReadMemory(uint address, byte count)
        {
            Mc.AddLine($"Sending Custom \"Read Memory\" block (Address: ${address:X6}, Count: ${count:X2})");
            var blocks = SendCustom(
            [
                0x86,
                count,
                (byte)(address & 0xFF),
                (byte)((address >> 8) & 0xFF),
                (byte)((address >> 16) & 0xFF),
            ]);
            blocks = [.. blocks.Where(b => !b.IsAckNak)];
            if (blocks.Count != 1)
            {
                // Permissions issue?
                return [];
            }
            return [.. blocks[0].Body];
        }

        /// <summary>
        /// Read the low 64KB of the cluster's NEC controller ROM.
        /// For MFA clusters, that should cover the entire ROM.
        /// For FIS clusters, the ROM is 128KB and more work is needed to retrieve the high 64KB.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public List<byte> CustomReadNecRom(ushort address, byte count)
        {
            Mc.AddLine($"Sending Custom \"Read NEC ROM\" block (Address: ${address:X4}, Count: ${count:X2})");
            var blocks = SendCustom(
            [
                0xA6,
                count,
                (byte)(address & 0xFF),
                (byte)((address >> 8) & 0xFF),
            ]);
            blocks = [.. blocks.Where(b => !b.IsAckNak)];
            if (blocks.Count != 1)
            {
                throw new InvalidOperationException($"Custom \"Read NEC ROM\" returned {blocks.Count} blocks instead of 1");
            }
            return [.. blocks[0].Body];
        }

        public List<byte> MapEeprom()
        {
            // Unlock partial EEPROM read
            Unlock();

            var map = new List<byte>();
            const byte blockSize = 1;
            for (ushort addr = 0; addr < 2048; addr += blockSize)
            {
                var blockBytes = _kwp1281.ReadEeprom(addr, blockSize);
                blockBytes = [.. Enumerable.Repeat(blockBytes == null ? (byte)0 : (byte)0xFF, blockSize)];
                map.AddRange(blockBytes);
            }

            return map;
        }

        public void DumpMem(string dumpFileName, uint startAddress, uint length)
        {
            const byte blockSize = 15;

            bool succeeded = true;
            using (var fs = File.Create(dumpFileName, blockSize, FileOptions.WriteThrough))
            {
                for (var addr = startAddress; addr < startAddress + length; addr += blockSize)
                {
                    var readLength = (byte)Math.Min(startAddress + length - addr, blockSize);
                    var blockBytes = CustomReadMemory(addr, readLength);
                    if (blockBytes.Count != readLength)
                    {
                        succeeded = false;
                        blockBytes.AddRange(Enumerable.Repeat((byte)0, readLength - blockBytes.Count));
                        Mc.AddLine($"{readLength - blockBytes.Count} missing");
                    }
                    fs.Write([.. blockBytes], 0, blockBytes.Count);
                    fs.Flush();
                }
            }

            if (!succeeded)
            {
                Mc.Add("**********************************************************************");
                Mc.Add("*** Warning: Some bytes could not be read and were replaced with 0 ***");
                Mc.AddLine("**********************************************************************");
            }
        }

        private List<Block> SendCustom(List<byte> blockCustomBytes)
        {
            if (blockCustomBytes[0] > 0x80 && !_additionalCustomCommandsUnlocked)
            {
                CustomUnlockAdditionalCommands();
                _additionalCustomCommandsUnlocked = true;
            }

            blockCustomBytes.Insert(0, (byte)BlockTitle.Custom);
            _kwp1281.SendBlock(blockCustomBytes);
            return _kwp1281.ReceiveBlocks();
        }

        public (bool succeeded, string? softwareVersion) Unlock()
        {
            var versionBlocks = CustomReadSoftwareVersion();
            if (versionBlocks.Count == 0)
            {
                Mc.AddLine("Cluster did not return software version.");
                return (succeeded: false, softwareVersion: null);
            }

            // Now we need to send an unlock code that is unique to each ROM version
            Mc.AddLine("Sending Custom \"Unlock partial EEPROM read\" block");
            var softwareVersion = SoftwareVersionToString(versionBlocks.First().Item2.Body);
            var unlockCodes = GetClusterUnlockCodes(softwareVersion);
            var unlocked = false;
            foreach (var unlockCode in unlockCodes)
            {
                var unlockCommand = new List<byte> { 0x9D };
                unlockCommand.AddRange(unlockCode);
                var unlockResponse = SendCustom(unlockCommand);
                if (unlockResponse.Count != 1)
                {
                    throw new InvalidOperationException($"Received multiple responses from unlock request.");
                }
                if (unlockResponse[0].IsAck)
                {
                    Mc.AddLine(
                        $"Unlock code for software version '{softwareVersion}' is{Utils.Dump(unlockCode)}");
                    if (unlockCodes.Length > 1)
                    {
                        Mc.AddLine("Please report this to the program author.");
                    }
                    unlocked = true;
                    break;
                }
                else if (!unlockResponse[0].IsNak)
                {
                    throw new InvalidOperationException(
                        $"Received non-ACK/NAK ${unlockResponse[0].Title:X2} from unlock request.");
                }
            }
            return (unlocked, softwareVersion);
        }

        private const int MaxAccessLevel = 7;

        /// <summary>
        /// Tries to perform seed/key authentication with cluster.
        /// </summary>
        /// <param name="softwareVersion">Software version string like "VQMJ07LM 09.00"</param>
        public void SeedKeyAuthenticate(string? softwareVersion)
        {
            // Perform Seed/Key authentication
            Mc.AddLine("Sending Custom \"Seed request\" block");
            var response = SendCustom([0x96, 0x01]);

            List<Block> responseBlocks = response;
            if (responseBlocks.Where(b => !b.IsAckNak).ToList() is [CustomBlock customBlock])
            {
                Mc.AddLine($"Block: {Utils.Dump(customBlock.Body)}");

                var keyBytes = VdoKeyFinder.FindKey([.. customBlock.Body], MaxAccessLevel);

                Mc.AddLine("Sending Custom \"Key response\" block");

                List<byte> keyResponse = [0x96, 0x02, .. keyBytes];

                _ = SendCustom(keyResponse);
            }
        }

        public bool RequiresSeedKey()
        {
            var accessLevel = GetAccessLevel();
            return accessLevel != MaxAccessLevel;
        }

        private int? GetAccessLevel()
        {
            Mc.AddLine("Sending Custom \"Get Access Level\" block");
            var response = SendCustom([0x96, 0x04]);
            List<Block> responseBlocks = [.. response.Where(b => !b.IsAckNak)];
            if (responseBlocks is [CustomBlock])
            {
                int accessLevel = responseBlocks[0].Body.First();
                Mc.AddLine($"Access level is {accessLevel}.");

                return accessLevel;
            }
            else
            {
                Mc.AddLine("Access level is unknown.");
                return null;
            }
        }

        /// <summary>
        /// Given a VDO cluster EEPROM dump, attempt to determine the SKC and return it if found.
        /// </summary>
        /// <param name="bytes">A portion of a VDO cluster EEPROM dump.</param>
        /// <param name="startAddress">The start address of bytes within the EEPROM.</param>
        /// <returns>The SKC or null if the SKC could not be determined.</returns>
        public static ushort? GetSkc(byte[] bytes, int startAddress)
        {
            string text = Encoding.ASCII.GetString(bytes);

            // There are several EEPROM formats. We can determine the format by locating the
            // 14-character immobilizer ID and noting its offset in the dump.

            var immoMatch = Regex.Match(
                text,
                @"[A-Z]{2}Z\dZ0[A-Z]\d{7}");
            if (!immoMatch.Success)
            {
                Messenger.Instance.AddLine("GetSkc: Unable to find Immobilizer ID in cluster dump.");
                return null;
            }

            ushort skc;
            var index = immoMatch.Index + startAddress;

            switch (index)
            {
                case 0x090:
                case 0x0AC:
                    // Immo2
                    skc = Utils.GetBcd(bytes, 0x0BA - startAddress);
                    return skc;
                case 0x0A2:
                    // VWK501
                    skc = Utils.GetShort(bytes, 0x0CC - startAddress);
                    return skc;
                case 0x0E0:
                    // VWK503
                    skc = Utils.GetShort(bytes, 0x10A - startAddress);
                    return skc;
                default:
                    Messenger.Instance.AddLine(
                        $"GetSkc: Unknown EEPROM (Immobilizer offset: 0x{immoMatch.Index:X3})");
                    return null;
            }
        }

        /// <summary>
        /// http://www.maltchev.com/kiti/VAG_guide.txt
        /// This unlocks additional custom commands $81-$AF
        /// </summary>
        private void CustomUnlockAdditionalCommands()
        {
            Mc.AddLine("Sending Custom \"Unlock Additional Commands\" block");
            SendCustom([0x80, 0x01, 0x02, 0x03, 0x04]);
        }

        /// <summary>
        /// Different cluster models have different unlock codes. Return the appropriate one based
        /// on the cluster's software version.
        /// </summary>
        internal static byte[][] GetClusterUnlockCodes(string softwareVersion)
        {
            return softwareVersion switch
            {
                // 7H5920872L VDO V03
                "VT5P07MH 09.00" => [[0x00, 0x07, 0x43, 0x35]],
                "VAT500LL 01.00" or "VAT500LL 01.20" or "VAT500MH 01.10" or "VAT500MH 01.20" => [[0x01, 0x04, 0x3D, 0x35]],
                // 1J0919860B V15
                "$01 $00 $14 $01" => [[0x01, 0x08, 0x05, 0x02]],
                // 7D0920800F V01, 1J0919951C V55
                "V798MLA 01.00" => [[0x02, 0x03, 0x05, 0x09]],
                // 8D0919880M D02
                "$00 $00 $13 $01" => [[0x09, 0x06, 0x05, 0x02]],
                // 6Q0920800 V11
                "VSQX01LM 01.00" => [[0x31, 0x39, 0x34, 0x46]],
                // 3BD920848E V03
                "VCLM09MH $00 $09" => [[0x32, 0x31, 0x36, 0x31]],
                // 1JD920826E V01
                "VCB07LL  09.00" => [[0x33, 0x34, 0x46, 0x4A]],
                "VKQ501HH 09.00" or "VQMJ07HH 08.40" or "VQMJ07LM 08.40" or "VQMJ07LM 09.00" => [[0x34, 0x3F, 0x43, 0x39]],
                // 6Q0920903 V02
                "VQMJ06LM 09.00" => [[0x35, 0x3D, 0x47, 0x3E]],
                "SS5501LM 00.80" or "SS5501ML 00.80" => [[0x36, 0x3B, 0x36, 0x3D]],
                // 1J0920906L V58
                "VWK501LL 00.88" or "VWK501MH 00.88" or "VWK501LL 01.00" or "VWK501MH 01.00" => [[0x36, 0x3D, 0x3E, 0x47]],
                "VT5X02LL 09.40" => [[0x36, 0x3F, 0x45, 0x42]],
                // 6QE920827C V06
                "VQMJ09HH 05.10" => [[0x37, 0x42, 0x47, 0x43]],
                "VT5X02LL 09.00" => [[0x38, 0x39, 0x3A, 0x47]],
                // 1M0920800C V15
                "S599CAA  01.00" or "V599HLA  00.91" or "V599LLA  00.91" or "V599LLA  01.00" or "V599MLA  01.00" or "V599LLA  03.00" => [[0x38, 0x3F, 0x40, 0x35]],
                "MPV300LL 04.00" or "MPV501MH 01.00" => [[0x38, 0x47, 0x34, 0x3A]],
                // 3B0920827C V06
                "VWK501MH 00.92" or "VWK501MH 01.10" => [[0x39, 0x34, 0x34, 0x40]],
                "VBK700LL 00.96" or "VBK700LL 01.00" or "VBKX00MH 01.00" => [[0x3A, 0x39, 0x31, 0x43]],
                "MPV300LL 02.00" => [[0x3B, 0x47, 0x03, 0x02]],
                // 1M0920802D V05
                "SS5501LM 01.00" or "SS5501ML 01.00" => [[0x3C, 0x34, 0x47, 0x35]],
                "VSQX01LM 01.20" => [[0x3D, 0x36, 0x40, 0x36]],
                "S599CAA  00.80" => [[0x3D, 0x39, 0x3B, 0x35]],
                // 3U0920842B V06
                "KB5M07HH 09.00" or "VWK503LL 09.00" or "VWK503MH 09.00" => [[0x3E, 0x35, 0x3D, 0x3A]],
                // 1J5920826L V75
                "VMMJ08MH 09.00" => [[0x3E, 0x47, 0x3D, 0x48]],
                "MPV300LL 00.90" or "MPV500LL 00.90" => [[0x3F, 0x38, 0x43, 0x38]],
                "SS5500LM 01.00" => [[0x40, 0x39, 0x39, 0x38]],
                // 6Q0920900 V18
                "VSQX01LM 01.10" => [[0x43, 0x43, 0x3D, 0x37]],
                "MPV300LL 03.00" => [[0x43, 0x43, 0x43, 0x39]],
                // 6Y1920860G V12
                "KPQMLA` $01" => [[0x47, 0x3B, 0x31, 0x3F]],
                // 5J0920810C V2721446
                "K5MJ07LM 08.10" => [[0x47, 0x3F, 0x39, 0x44]],
                _ => ClusterUnlockCodes,
            };
        }

        private static string SoftwareVersionToString(List<byte> versionBytes)
        {
            if (versionBytes.Count < 9 || versionBytes.Count > 10)
            {
                return Utils.DumpMixedContent(versionBytes);
            }

            var asciiPart = Encoding.ASCII.GetString(versionBytes.ToArray()[0..^2]);
            return $"{asciiPart} {versionBytes[^1]:X2}.{versionBytes[^2]:X2}";
        }

        internal static readonly byte[][] ClusterUnlockCodes =
        [
            [0x00, 0x00, 0x00, 0x00],
            [0x00, 0x00, 0x03, 0x02],
            [0x00, 0x01, 0x03, 0x02],
            [0x00, 0x02, 0x03, 0x02],
            [0x00, 0x02, 0x09, 0x07],
            [0x00, 0x03, 0x03, 0x02],
            [0x00, 0x03, 0x04, 0x02],
            [0x00, 0x04, 0x03, 0x02],
            [0x00, 0x04, 0x06, 0x07],
            [0x00, 0x05, 0x03, 0x02],
            [0x00, 0x06, 0x03, 0x02],
            [0x00, 0x07, 0x02, 0x04],
            [0x00, 0x07, 0x03, 0x08],
            [0x00, 0x07, 0x43, 0x35],
            [0x00, 0x08, 0x02, 0x04],
            [0x01, 0x00, 0x03, 0x02],
            [0x01, 0x00, 0x09, 0x05],
            [0x01, 0x01, 0x00, 0x04],
            [0x01, 0x01, 0x00, 0x05],
            [0x01, 0x01, 0x00, 0x06],
            [0x01, 0x01, 0x00, 0x07],
            [0x01, 0x01, 0x00, 0x08],
            [0x01, 0x01, 0x00, 0x09],
            [0x01, 0x01, 0x01, 0x00],
            [0x01, 0x01, 0x01, 0x01],
            [0x01, 0x01, 0x01, 0x02],
            [0x01, 0x01, 0x01, 0x03],
            [0x01, 0x01, 0x01, 0x04],
            [0x01, 0x01, 0x01, 0x05],
            [0x01, 0x01, 0x01, 0x06],
            [0x01, 0x01, 0x03, 0x02],
            [0x01, 0x01, 0x03, 0x07],
            [0x01, 0x01, 0x05, 0x08],
            [0x01, 0x01, 0x07, 0x09],
            [0x01, 0x02, 0x03, 0x02],
            [0x01, 0x03, 0x03, 0x02],
            [0x01, 0x04, 0x02, 0x02],
            [0x01, 0x04, 0x03, 0x02],
            [0x01, 0x04, 0x3D, 0x35],
            [0x01, 0x05, 0x03, 0x02],
            [0x01, 0x05, 0x06, 0x08],
            [0x01, 0x05, 0x3D, 0x35],
            [0x01, 0x06, 0x00, 0x02],
            [0x01, 0x06, 0x02, 0x00],
            [0x01, 0x06, 0x03, 0x02],
            [0x01, 0x06, 0x04, 0x02],
            [0x01, 0x07, 0x00, 0x03],
            [0x01, 0x08, 0x02, 0x05],
            [0x01, 0x08, 0x03, 0x00],
            [0x01, 0x08, 0x05, 0x02],
            [0x02, 0x00, 0x03, 0x02],
            [0x02, 0x00, 0x06, 0x01],
            [0x02, 0x01, 0x03, 0x02],
            [0x02, 0x02, 0x03, 0x02],
            [0x02, 0x02, 0x04, 0x01],
            [0x02, 0x02, 0x09, 0x02],
            [0x02, 0x03, 0x03, 0x02],
            [0x02, 0x03, 0x05, 0x09],
            [0x02, 0x04, 0x00, 0x02],
            [0x02, 0x04, 0x03, 0x02],
            [0x02, 0x05, 0x00, 0x02],
            [0x02, 0x05, 0x03, 0x02],
            [0x02, 0x05, 0x06, 0x09],
            [0x02, 0x05, 0x08, 0x01],
            [0x02, 0x06, 0x03, 0x02],
            [0x02, 0x06, 0x06, 0x09],
            [0x02, 0x09, 0x02, 0x06],
            [0x02, 0x09, 0x04, 0x02],
            [0x02, 0x09, 0x04, 0x03],
            [0x02, 0x32, 0x3B, 0x37],
            [0x03, 0x00, 0x03, 0x02],
            [0x03, 0x00, 0x03, 0x07],
            [0x03, 0x00, 0x07, 0x01],
            [0x03, 0x01, 0x03, 0x02],
            [0x03, 0x02, 0x03, 0x02],
            [0x03, 0x02, 0x05, 0x02],
            [0x03, 0x03, 0x03, 0x02],
            [0x03, 0x03, 0x08, 0x04],
            [0x03, 0x03, 0x09, 0x03],
            [0x03, 0x04, 0x03, 0x02],
            [0x03, 0x05, 0x03, 0x02],
            [0x03, 0x06, 0x03, 0x02],
            [0x03, 0x08, 0x02, 0x05],
            [0x04, 0x00, 0x03, 0x02],
            [0x04, 0x01, 0x03, 0x02],
            [0x04, 0x01, 0x03, 0x08],
            [0x04, 0x02, 0x03, 0x02],
            [0x04, 0x02, 0x06, 0x06],
            [0x04, 0x03, 0x03, 0x02],
            [0x04, 0x04, 0x03, 0x02],
            [0x04, 0x04, 0x09, 0x04],
            [0x04, 0x05, 0x03, 0x02],
            [0x04, 0x05, 0x05, 0x02],
            [0x04, 0x06, 0x03, 0x02],
            [0x04, 0x07, 0x00, 0x07],
            [0x05, 0x00, 0x03, 0x02],
            [0x05, 0x01, 0x03, 0x02],
            [0x05, 0x01, 0x04, 0x08],
            [0x05, 0x02, 0x03, 0x02],
            [0x05, 0x02, 0x03, 0x09],
            [0x05, 0x02, 0x09, 0x02],
            [0x05, 0x03, 0x03, 0x02],
            [0x05, 0x04, 0x03, 0x02],
            [0x05, 0x05, 0x03, 0x02],
            [0x05, 0x05, 0x08, 0x09],
            [0x05, 0x05, 0x09, 0x05],
            [0x05, 0x06, 0x03, 0x02],
            [0x05, 0x08, 0x05, 0x02],
            [0x06, 0x00, 0x02, 0x02],
            [0x06, 0x00, 0x03, 0x00],
            [0x06, 0x00, 0x03, 0x02],
            [0x06, 0x01, 0x03, 0x02],
            [0x06, 0x02, 0x03, 0x02],
            [0x06, 0x03, 0x03, 0x02],
            [0x06, 0x04, 0x03, 0x02],
            [0x06, 0x04, 0x07, 0x01],
            [0x06, 0x05, 0x03, 0x02],
            [0x06, 0x06, 0x03, 0x02],
            [0x06, 0x06, 0x09, 0x06],
            [0x06, 0x09, 0x01, 0x02],
            [0x06, 0x09, 0x03, 0x09],
            [0x06, 0x09, 0x05, 0x03],
            [0x07, 0x00, 0x03, 0x02],
            [0x07, 0x00, 0x06, 0x04],
            [0x07, 0x01, 0x03, 0x02],
            [0x07, 0x02, 0x03, 0x02],
            [0x07, 0x03, 0x03, 0x02],
            [0x07, 0x03, 0x05, 0x03],
            [0x07, 0x04, 0x03, 0x02],
            [0x07, 0x05, 0x03, 0x02],
            [0x07, 0x06, 0x03, 0x02],
            [0x07, 0x07, 0x09, 0x04],
            [0x07, 0x07, 0x09, 0x07],
            [0x08, 0x00, 0x03, 0x02],
            [0x08, 0x01, 0x03, 0x02],
            [0x08, 0x01, 0x06, 0x05],
            [0x08, 0x02, 0x01, 0x04],
            [0x08, 0x02, 0x03, 0x02],
            [0x08, 0x02, 0x03, 0x05],
            [0x08, 0x03, 0x03, 0x02],
            [0x08, 0x04, 0x02, 0x02],
            [0x08, 0x04, 0x03, 0x02],
            [0x08, 0x05, 0x03, 0x02],
            [0x08, 0x06, 0x03, 0x02],
            [0x08, 0x06, 0x07, 0x06],
            [0x08, 0x08, 0x09, 0x08],
            [0x09, 0x00, 0x03, 0x02],
            [0x09, 0x01, 0x01, 0x07],
            [0x09, 0x01, 0x03, 0x02],
            [0x09, 0x02, 0x03, 0x02],
            [0x09, 0x02, 0x06, 0x06],
            [0x09, 0x03, 0x03, 0x02],
            [0x09, 0x03, 0x09, 0x06],
            [0x09, 0x04, 0x03, 0x02],
            [0x09, 0x05, 0x02, 0x03],
            [0x09, 0x05, 0x03, 0x02],
            [0x09, 0x05, 0x05, 0x08],
            [0x09, 0x06, 0x03, 0x02],
            [0x09, 0x06, 0x04, 0x09],
            [0x09, 0x06, 0x05, 0x02],
            [0x09, 0x09, 0x03, 0x02],
            [0x09, 0x09, 0x09, 0x09],
            [0x31, 0x39, 0x34, 0x46],
            [0x31, 0x44, 0x35, 0x43],
            [0x32, 0x31, 0x36, 0x31],
            [0x32, 0x37, 0x3E, 0x31],
            [0x33, 0x34, 0x46, 0x4A],
            [0x34, 0x3F, 0x43, 0x39],
            [0x35, 0x3B, 0x39, 0x3D],
            [0x35, 0x3C, 0x31, 0x3C],
            [0x35, 0x3D, 0x04, 0x01],
            [0x35, 0x3D, 0x47, 0x3E],
            [0x35, 0x40, 0x3F, 0x38],
            [0x35, 0x43, 0x31, 0x38],
            [0x35, 0x47, 0x34, 0x3C],
            [0x36, 0x3B, 0x36, 0x3D],
            [0x36, 0x3D, 0x3E, 0x47],
            [0x36, 0x3F, 0x45, 0x42],
            [0x36, 0x40, 0x36, 0x3D],
            [0x37, 0x39, 0x3C, 0x47],
            [0x37, 0x3B, 0x32, 0x02],
            [0x37, 0x3D, 0x43, 0x43],
            [0x37, 0x42, 0x47, 0x43],
            [0x38, 0x34, 0x34, 0x37],
            [0x38, 0x37, 0x3E, 0x31],
            [0x38, 0x39, 0x39, 0x40],
            [0x38, 0x39, 0x3A, 0x47],
            [0x38, 0x3F, 0x40, 0x35],
            [0x38, 0x43, 0x38, 0x3F],
            [0x38, 0x47, 0x34, 0x3A],
            [0x39, 0x34, 0x34, 0x40],
            [0x39, 0x43, 0x43, 0x43],
            [0x3A, 0x31, 0x31, 0x36],
            [0x3A, 0x34, 0x47, 0x38],
            [0x3A, 0x39, 0x31, 0x43],
            [0x3A, 0x39, 0x41, 0x43],
            [0x3A, 0x3B, 0x35, 0x3C],
            [0x3A, 0x3B, 0x35, 0x4C],
            [0x3A, 0x3D, 0x35, 0x3E],
            [0x3B, 0x33, 0x3E, 0x37],
            [0x3B, 0x3A, 0x37, 0x3E],
            [0x3B, 0x46, 0x23, 0x10],
            [0x3B, 0x46, 0x23, 0x1B],
            [0x3B, 0x46, 0x23, 0x1D],
            [0x3B, 0x47, 0x03, 0x02],
            [0x3C, 0x31, 0x3C, 0x35],
            [0x3C, 0x34, 0x47, 0x35],
            [0x3D, 0x36, 0x40, 0x36],
            [0x3D, 0x39, 0x3B, 0x35],
            [0x3E, 0x35, 0x3D, 0x3A],
            [0x3E, 0x35, 0x43, 0x30],
            [0x3E, 0x35, 0x43, 0x39],
            [0x3E, 0x35, 0x43, 0x40],
            [0x3E, 0x35, 0x43, 0x41],
            [0x3E, 0x35, 0x43, 0x42],
            [0x3E, 0x35, 0x43, 0x43],
            [0x3E, 0x35, 0x43, 0x44],
            [0x3E, 0x39, 0x31, 0x43],
            [0x3E, 0x39, 0x35, 0x40],
            [0x3E, 0x39, 0x43, 0x34],
            [0x3E, 0x3F, 0x40, 0x35],
            [0x3E, 0x47, 0x3D, 0x48],
            [0x3F, 0x31, 0x3B, 0x47],
            [0x3F, 0x38, 0x43, 0x38],
            [0x3F, 0x43, 0x35, 0x3E],
            [0x40, 0x30, 0x3E, 0x39],
            [0x40, 0x34, 0x34, 0x39],
            [0x40, 0x39, 0x39, 0x38],
            [0x40, 0x43, 0x35, 0x3E],
            [0x41, 0x43, 0x35, 0x3E],
            [0x42, 0x43, 0x35, 0x3E],
            [0x42, 0x45, 0x3F, 0x36],
            [0x43, 0x31, 0x39, 0x3A],
            [0x43, 0x43, 0x35, 0x3E],
            [0x43, 0x43, 0x3D, 0x37],
            [0x43, 0x43, 0x43, 0x39],
            [0x43, 0x45, 0x31, 0x3D],
            [0x44, 0x43, 0x35, 0x3E],
            [0x45, 0x39, 0x34, 0x43],
            [0x47, 0x3A, 0x39, 0x38],
            [0x47, 0x3B, 0x31, 0x3F],
            [0x47, 0x3C, 0x39, 0x37],
            [0x47, 0x3E, 0x3D, 0x36],
            [0x47, 0x3F, 0x39, 0x44],
        ];

        private static string DumpMixedContent(Block block)
        {
            if (block.IsNak)
            {
                return "NAK";
            }

            return Utils.DumpMixedContent(block.Body);
        }

        private static string DumpBinaryContent(Block block)
        {
            if (block.IsNak)
            {
                return "NAK";
            }

            return Utils.DumpBytes(block.Body);
        }

        private void DumpEeprom(ushort startAddr, ushort length, byte maxReadLength, string fileName)
        {
            bool succeeded = true;

            using (var fs = File.Create(fileName, maxReadLength, FileOptions.WriteThrough))
            {
                for (uint addr = startAddr; addr < (startAddr + length); addr += maxReadLength)
                {
                    byte readLength = (byte)Math.Min(startAddr + length - addr, maxReadLength);
                    List<byte>? blockBytes = _kwp1281.ReadEeprom((ushort)addr, readLength);
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
                Mc.Add("Warning: Some bytes could not be read and were replaced with 0");
            }
        }

        private readonly IKW1281Dialog _kwp1281 = kwp1281;
        private bool _additionalCustomCommandsUnlocked = false;
    }
}