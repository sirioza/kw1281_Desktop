using BitFab.KW1281Test.Actions;
using BitFab.KW1281Test.Enums;
using BitFab.KW1281Test.Interface;
using BitFab.KW1281Test.Interface.EDC15;
using BitFab.KW1281Test.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace BitFab.KW1281Test;

public class Diagnostic
{
    private readonly Messenger Mc = Messenger.Instance;
    private readonly DataSender Ds = DataSender.Instance;

    public static ActuatorTestControl Control { get; } = new();
    internal static List<string> CommandAndArgs { get; private set; } = [];

    public async Task RunAsync(string portName, int baudRate, int controllerAddress, Commands command,
        params Arg[] args)
    {
        try
        {
            // This seems to increase the accuracy of our timing loops
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
        }
        catch (Win32Exception)
        {
            // Ignore if we don't have permission to increase our priority
        }

        uint address = 0;
        uint length = 0;
        byte value = 0;
        int softwareCoding = 0;
        int workshopCode = 0;
        byte channel = 0;
        ushort channelValue = 0;
        ushort? login = null;
        byte groupNumber = 0;
        var addressValuePairs = new List<KeyValuePair<ushort, byte>>();
        string fileDirectory = null!;

        try
        {
            if (command.Equals(Commands.ReadEeprom) ||
                command.Equals(Commands.ReadRAM) ||
                command.Equals(Commands.ReadROM))
            {
                address = Utils.ParseUint(args[0]);
            }
            else if (command.Equals(Commands.DumpMarelliMem) ||
                     command.Equals(Commands.DumpEeprom) ||
                     command.Equals(Commands.DumpMem) ||
                     command.Equals(Commands.DumpRam) ||
                     command.Equals(Commands.DumpRBxMem) ||
                     command.Equals(Commands.DumpRBxMemOdd))
            {
                address = Utils.ParseUint(args[0]);
                length = Utils.ParseUint(args[1]);
                fileDirectory = args[2];
            }
            else if (command.Equals(Commands.WriteEeprom))
            {
                address = Utils.ParseUint(args[0]);
                value = (byte)Utils.ParseUint(args[1]);
            }
            else if (command.Equals(Commands.LoadEeprom))
            {
                address = Utils.ParseUint(args[0]);
                fileDirectory = args[1];
            }
            else if (command.Equals(Commands.SetSoftwareCoding))
            {
                softwareCoding = (int)Utils.ParseUint(args[0]);
                workshopCode = (int)Utils.ParseUint(args[1]);
            }
            else if (command.Equals(Commands.DumpEdc15Eeprom) || command.Equals(Commands.MapEeprom))
            {
                fileDirectory = args[0];
            }
            else if (command.Equals(Commands.WriteEdc15Eeprom))
            {
                // WriteEdc15Eeprom ADDRESS1 VALUE1 [ADDRESS2 VALUE2 ... ADDRESSn VALUEn]

                var dateString = DateTime.Now.ToString("s").Replace(':', '-');
                fileDirectory = $"EDC15_EEPROM_{dateString}.bin";
                ParseAddressesAndValues([.. args.Select(arg => arg)], out addressValuePairs);
            }
            else if (command.Equals(Commands.AdaptationRead))
            {
                channel = byte.Parse(args[0]);

                if (args.Length > 1)
                {
                    login = ushort.Parse(args[1]);
                }
            }
            else if (command.Equals(Commands.AdaptationSave) || command.Equals(Commands.AdaptationTest))
            {
                channel = byte.Parse(args[0]);
                channelValue = ushort.Parse(args[1]);

                if (args.Length > 2)
                {
                    login = ushort.Parse(args[2]);
                }
            }
            else if (command.Equals(Commands.BasicSetting) || command.Equals(Commands.GroupRead))
            {
                groupNumber = byte.Parse(args[0]);
            }
            else if (command.Equals(Commands.FindLogins))
            {
                login = ushort.Parse(args[0]);
            }
        }
        catch
        {
            Ds.Error("Check parameters.");
            return;
        }

        try
        {
            using var @interface = OpenPort(portName, baudRate);
            var tester = new Tester(@interface, controllerAddress);

            switch (command)
            {
                case Commands.AutoScan:
                    AutoScan(@interface, args.Select(arg => (int)arg));
                    return;
                case Commands.DumpRBxMem:
                    tester.DumpRBxMem(address, length, fileDirectory);
                    tester.EndCommunication();
                    return;
                case Commands.DumpRBxMemOdd:
                    tester.DumpRBxMem(address, length, fileDirectory, evenParityWakeup: false);
                    tester.EndCommunication();
                    return;
                case Commands.GetSKC:
                    tester.GetSkc();
                    tester.EndCommunication();
                    return;
                case Commands.ToggleRB4Mode:
                    tester.ToggleRB4Mode();
                    tester.EndCommunication();
                    return;
                default:
                    break;
            }

            ControllerInfo ecuInfo;

            try
            {
                ecuInfo = tester.Kwp1281Wakeup();
            }
            catch (UnableToProceedException)
            {
                return;
            }

            switch (command)
            {
                case Commands.ActuatorTest:
                    await tester.ActuatorTestAsync(Control);
                    break;
                case Commands.AdaptationRead:
                    tester.AdaptationRead(channel, login, ecuInfo.WorkshopCode);
                    break;
                case Commands.AdaptationSave:
                    tester.AdaptationSave(channel, channelValue, login, ecuInfo.WorkshopCode);
                    break;
                case Commands.AdaptationTest:
                    tester.AdaptationTest(channel, channelValue, login, ecuInfo.WorkshopCode);
                    break;
                case Commands.BasicSetting:
                    tester.BasicSettingRead(groupNumber);
                    break;
                case Commands.ClarionVWPremium4SafeCode:
                    tester.ClarionVWPremium4SafeCode();
                    break;
                case Commands.ClearFaultCodes:
                    tester.ClearFaultCodes();
                    break;
                case Commands.DelcoVWPremium5SafeCode:
                    tester.DelcoVWPremium5SafeCode();
                    break;
                case Commands.DumpCcmRom:
                    tester.DumpCcmRom(fileDirectory);
                    break;
                case Commands.DumpClusterNecRom:
                    tester.DumpClusterNecRom(fileDirectory);
                    break;
                case Commands.DumpEdc15Eeprom:
                    {
                        var eeprom = tester.ReadWriteEdc15Eeprom(fileDirectory);
                        Edc15VM.DisplayEepromInfo(eeprom);
                    }
                    break;
                case Commands.DumpEeprom:
                    tester.DumpEeprom(address, length, fileDirectory);
                    break;
                case Commands.DumpMarelliMem:
                    tester.DumpMarelliMem(address, length, ecuInfo, fileDirectory);
                    return;
                case Commands.DumpMem:
                    tester.DumpMem(address, length, fileDirectory);
                    break;
                case Commands.DumpRam:
                    tester.DumpRam(address, length, fileDirectory);
                    break;
                case Commands.DumpRom:
                    tester.DumpRom(address, length, fileDirectory);
                    break;
                case Commands.FindLogins:
                    tester.FindLogins(login!.Value, ecuInfo.WorkshopCode);
                    break;
                //case "getclusterid":
                //    tester.GetClusterId();
                //    break;
                case Commands.GroupRead:
                    tester.GroupRead(groupNumber);
                    break;
                case Commands.LoadEeprom:
                    tester.LoadEeprom(address, fileDirectory);
                    break;
                case Commands.MapEeprom:
                    tester.MapEeprom(fileDirectory);
                    break;
                case Commands.ReadEeprom:
                    tester.ReadEeprom(address);
                    break;
                case Commands.ReadRAM:
                    tester.ReadRam(address);
                    break;
                case Commands.ReadROM:
                    tester.ReadRom(address);
                    break;
                case Commands.ReadFaultCodes:
                    tester.ReadFaultCodes();
                    break;
                case Commands.ReadIdent:
                    tester.ReadIdent();
                    break;
                case Commands.ReadSoftwareVersion:
                    tester.ReadSoftwareVersion();
                    break;
                case Commands.Reset:
                    tester.Reset();
                    break;
                case Commands.SetSoftwareCoding:
                    tester.SetSoftwareCoding(softwareCoding, workshopCode);
                    break;
                case Commands.WriteEdc15Eeprom:
                    tester.ReadWriteEdc15Eeprom(fileDirectory, addressValuePairs);
                    break;
                case Commands.WriteEeprom:
                    tester.WriteEeprom(address, value);
                    break;
            }

            tester.EndCommunication();
        }
        catch (Exception ex)
        {
            Ds.Error(ex);
        }
    }

    private void AutoScan(IInterface @interface, IEnumerable<int> addresses)
    {
        var kwp1281Addresses = new List<string>();
        var kwp2000Addresses = new List<string>();
        foreach (var evenParity in new bool[] { false, true })
        {
            var parity = evenParity ? "(EvenParity)" : string.Empty;
            foreach (int address in addresses)
            {
                var tester = new Tester(@interface, address);
                try
                {
                    Mc.AddLine($"Attempting to wake up controller at address {address:X}{parity}...");
                    tester.Kwp1281Wakeup(evenParity, failQuietly: true);
                    tester.EndCommunication();
                    Ds.Send($"KWP1281: {address:X}{parity}");
                }
                catch (UnableToProceedException)
                {
                    Ds.Send($"Controller {address:X} did not wake up.");
                }
                catch (UnexpectedProtocolException)
                {
                    Ds.Send($"KWP2000: {address:X}{parity}");
                }
            }
        }
    }

    /// <summary>
    /// Accept a series of string values in the format:
    /// ADDRESS1 VALUE1 [ADDRESS2 VALUE2 ... ADDRESSn VALUEn]
    ///     ADDRESS = EEPROM address in decimal (0-511) or hex ($00-$1FF)
    ///     VALUE = Value to be stored at address in decimal (0-255) or hex ($00-$FF)
    /// </summary>
    internal bool ParseAddressesAndValues(
        string[] addressesAndValues,
        out List<KeyValuePair<ushort, byte>> addressValuePairs)
    {
        addressValuePairs = [];

        if (addressesAndValues.Count() % 2 != 0)
        {
            return false;
        }

        for (var i = 0; i < addressesAndValues.Count(); i += 2)
        {
            uint address;
            var valueToParse = addressesAndValues[i];
            try
            {
                address = Utils.ParseUint(valueToParse);
            }
            catch (Exception)
            {
                Mc.AddLine($"Invalid address (bad format): {valueToParse}.");
                return false;
            }

            if (address > 0x1FF)
            {
                Mc.AddLine($"Invalid address (too large): {valueToParse}.");
                return false;
            }

            uint value;
            valueToParse = addressesAndValues[i + 1];
            try
            {
                value = Utils.ParseUint(valueToParse);
            }
            catch (Exception)
            {
                Mc.AddLine($"Invalid value (bad format): {valueToParse}.");
                return false;
            }

            if (value > 0xFF)
            {
                Mc.AddLine($"Invalid value (too large): {valueToParse}.");
                return false;
            }

            addressValuePairs.Add(new KeyValuePair<ushort, byte>((ushort)address, (byte)value));
        }

        return true;
    }

    /// <summary>
    /// Opens the serial port.
    /// </summary>
    /// <param name="portName">
    /// Either the device name of a serial port (e.g. COM1, /dev/tty23)
    /// or an FTDI USB->Serial device serial number (2 letters followed by 6 letters/numbers).
    /// </param>
    /// <param name="baudRate"></param>
    /// <returns></returns>
    private IInterface OpenPort(string portName, int baudRate)
    {
        if (Regex.IsMatch(portName.ToUpper(), @"\A[A-Z0-9]{8}\Z"))
        {
            Mc.Add($"Opening FTDI serial port {portName}");
            return new FtdiInterface(portName, baudRate);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            portName.StartsWith("/dev/", StringComparison.CurrentCultureIgnoreCase))
        {
            Mc.Add($"Opening Linux serial port {portName}");
            return new LinuxInterface(portName, baudRate);
        }
        else
        {
            Mc.Add($"Opening Generic serial port {portName}");
            return new GenericInterface(portName, baudRate);
        }
    }
}
