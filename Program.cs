using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace Gallagher.Utilities
{
    #region Configuration
    class Configuration
    {
        public const int DEFAULT_BAUD = 1000000;
        public const int DEFAULT_DATA = 1000000;
        public const int DEFAULT_DATA_BITS = 8;
        public const StopBits DEFAULT_STOP_BITS = StopBits.One;
        public const Parity DEFAULT_PARITY = Parity.None;
        public const Handshake DEFAULT_FLOW_CONTROL = Handshake.None;
        public const int DEFAULT_REPEATS = 1;
        public static readonly TimeSpan DEFAULT_GAP = TimeSpan.Zero;

        public string ComPort { get; set; }
        public int BaudRate { get; set; } = DEFAULT_BAUD;

        public int DataBits { get; set; } = DEFAULT_DATA_BITS;

        public StopBits StopBits { get; set; } = DEFAULT_STOP_BITS;

        public Parity Parity { get; set; } = DEFAULT_PARITY;

        public Handshake FlowControl { get; set; } = DEFAULT_FLOW_CONTROL;
        public byte[] Data { get; set; }

        public int Repeats { get; set; } = DEFAULT_REPEATS;

        public TimeSpan Gap { get; set; } = DEFAULT_GAP;
    }
    #endregion Configuration

    class HBusTrafficGenerator
    {
        HBusTrafficGenerator(Configuration config)
        {
            Console.Clear();
            Console.WriteLine(GenerateHeader(config));
            var cancelSource = new CancellationTokenSource();

            try
            {
                var taskList = new List<Tuple<string, Task>>()
                {
                    new Tuple<string, Task>( "Key handling", KeyHandlingAsync(config, cancelSource)),
                    new Tuple<string, Task>( "Serial output", SerialOutput(config, cancelSource))
                };

                // Wait for any of the tasks to end
                var index = Task.WaitAny(taskList.Select(t => t.Item2).ToArray());

                // Cancel the other tasks
                cancelSource.Cancel();

                // Wait for serial output to end
                Task.WaitAll(taskList.Select(t => t.Item2).Last());
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellations
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{Environment.NewLine}{Environment.NewLine}ERROR: {ex.Message}{Environment.NewLine}{Environment.NewLine}{ex}");
            }
        }

        static async Task Run(Configuration config)
        {
            var repeats = config.Repeats;
            var infinite = repeats <= -1;
            if (repeats == 0)
                repeats = 1;

            var first = true;
            while (infinite || repeats-- > 0)
            {
                // Don't pause on the first iteration
                if (first)
                    first = false;
                else
                    await Task.Delay(config.Gap);

                Console.WriteLine(config.Data.BinToHexString());
            }
        }

        #region Entry and command line handling
        static void Main(string[] args)
        {
            Console.TreatControlCAsInput = true;
            if (ParseCommandLine(args, out Configuration config))
                new HBusTrafficGenerator(config);
        }

        static bool ParseCommandLine(string[] args, out Configuration config)
        {
            config = new Configuration();
            foreach (var arg in args)
            {
                var split = arg.Split('=');
                switch (split.Length)
                {
                    case 1:
                        // Com port
                        if (IsComPort(arg))
                            config.ComPort = arg.ToUpper();

                        // Baud rate
                        else if (int.TryParse(arg, out var baud))
                            config.BaudRate = baud;

                        //// Ignore empty lines
                        //else if (IsSwitchPresent(arg, CMD_NOEMPTY))
                        //    config.IgnoreEmptyLines = true;

                        // Anything else
                        else
                        {
                            Console.WriteLine($"ERROR: Unknown argument '{arg}'.");
                            return false;
                        }
                        break;

                    case 2:
                        var name = split[0];
                        var value = split[1];
                        switch (name.ToLower())
                        {
                            case CMD_COMPORT:
                                if (IsComPort(value))
                                    config.ComPort = value.ToUpper();
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid COM port argument '{value}'.");
                                    return false;
                                }
                                break;

                            case CMD_BAUD:
                                if (int.TryParse(value, out var baud))
                                    config.BaudRate = baud;
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid baud rate argument '{value}'.");
                                    return false;
                                }
                                break;

                            case CMD_DATA:
                                if (IsHexData(value))
                                {
                                    if (value.Length % 2 != 0)
                                        Console.WriteLine($"WARNING: Hex data string is not even. The last nibble (0x{value[^1]}) will be discarded.");
                                    config.Data = value.HexStringToBin().ToArray();
                                }
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid hex data string '{value}'.");
                                    return false;
                                }
                                break;

                            case CMD_REPEATS:
                                if (int.TryParse(value, out var repeats) && repeats >= -1)
                                    config.Repeats = repeats;
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid repeats argument '{value}'.");
                                    return false;
                                }
                                break;

                            case CMD_GAP:
                                if (int.TryParse(value, out var gap) && gap > 0)
                                    config.Gap = TimeSpan.FromMilliseconds(gap);
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid gap argument '{value}'.");
                                    return false;
                                }
                                break;

                            default:
                                Console.WriteLine($"ERROR: Unknown argument '{name}'.");
                                return false;
                        }
                        break;

                    default:

                        Console.WriteLine($"ERROR: Unknown argument '{arg}'.");
                        return false;
                }
            } //foreach

            // No repeats is read as run once
            if (config.Repeats == 0)
                config.Repeats = 1;

            if (string.IsNullOrEmpty(config.ComPort))
            {
                PrintUsage();
                return false;
            }

            return true;
        }

        static void PrintUsage()
        {
            var filePath = Process.GetCurrentProcess().MainModule.FileName;
            var version = FileVersionInfo.GetVersionInfo(filePath).FileVersion;
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            const int alignment = -13;

            Console.WriteLine(
                $"{fileName}, version: {version}{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"   Usage: {fileName,alignment} [{CMD_COMPORT}=]<{CMD_COMPORT}> [{CMD_BAUD}=<{CMD_BAUD}>] [{CMD_DATA}=<{CMD_DATA}>]{Environment.NewLine}" +
                $"                               [{CMD_REPEATS}=<{CMD_REPEATS}>] [{CMD_GAP}=<{CMD_GAP}>]{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"      where:{Environment.NewLine}" +
                $"         {CMD_REPEATS + ":",alignment}The COM port to connect to, eg. COM1.{Environment.NewLine}" +
                $"         {CMD_BAUD + ":",alignment}The baud rate. Default is {Configuration.DEFAULT_BAUD}.{Environment.NewLine}" +
                $"         {CMD_DATA + ":",alignment}The data to send, in hex, eg. 01020E0F{Environment.NewLine}" +
                $"         {CMD_REPEATS + ":",alignment}The number of times to repeat the transmission (-1 for infinite){Environment.NewLine}" +
                $"         {CMD_GAP + ":",alignment}The number of milliseconds delay between repetitions. Default is {Configuration.DEFAULT_GAP.TotalMilliseconds}ms{Environment.NewLine}" +
                $"{Environment.NewLine}"
            );
        }

        static string GenerateHeader(Configuration config)
        {
            const int alignment = -15;
            const string COM_PORT = "COM port";
            const string BAUD_RATE = "Baud rate";
            const string REPEATS = "Repeats";
            const string GAP = "Gap";
            const string DATA = "Data";

            var dataLen = (Console.WindowWidth - Math.Abs(alignment)) / 3 - 2;
            var data = string.Join(Environment.NewLine + new string(' ', Math.Abs(alignment)), config.Data.Chunk(dataLen).Select(line => line.BinToHexString(" ")));

            return
                $"{COM_PORT + ":",alignment}{config.ComPort}{Environment.NewLine}" +
                $"{BAUD_RATE + ":",alignment}{config.BaudRate}{Environment.NewLine}" +
                $"{REPEATS + ":",alignment}{config.Repeats}{Environment.NewLine}" +
                $"{GAP + ":",alignment}{config.Gap.TotalMilliseconds}ms{Environment.NewLine}" +
                $"{DATA + ":",alignment}{data}{Environment.NewLine}";
        }

        static bool IsComPort(string inp) => _comPortRegex.Match(inp).Success;
        static bool IsHexData(string inp) => _hexStringRegex.Match(inp).Success;
        static bool IsSwitchPresent(string arg, string swtch) => string.Compare(arg, swtch, true) == 0;

        static readonly Regex _comPortRegex = new(@"^COM\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex _hexStringRegex = new(@"^[0-9a-f]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        const string CMD_COMPORT = "com";
        const string CMD_BAUD = "baud";
        const string CMD_DATA = "data";
        const string CMD_GAP = "gap";
        const string CMD_REPEATS = "repeat";

        #endregion Entry and command line handling

        #region Key handling
        static Task KeyHandlingAsync(Configuration config, CancellationTokenSource cancelSource)
            => Task.Run(() =>
            {
                Console.WriteLine("Ctrl-C to quit");
                Console.WriteLine("Ctrl-X to clear screen");
                Console.WriteLine();

                while (!cancelSource.IsCancellationRequested)
                {
                    var keyInfo = Console.ReadKey(true);
                    // Application control
                    if (keyInfo.Modifiers == ConsoleModifiers.Control)
                    {
                        switch (keyInfo.Key)
                        {
                            case ConsoleKey.C:
                                cancelSource.Cancel();
                                break;
                            case ConsoleKey.X:
                                Console.Clear();
                                Console.WriteLine(GenerateHeader(config));
                                Console.WriteLine("Ctrl-C to quit");
                                Console.WriteLine("Ctrl-X to clear screen");
                                Console.WriteLine();
                                break;
                        }
                    }
                }
            });
        #endregion Key handling

        static Task SerialOutput(Configuration config, CancellationTokenSource cancelSource)
            => Task.Run(async () =>
            {
                var token = cancelSource.Token;
                var totalRepeats = config.Repeats == 0 ? 1 : config.Repeats;
                var infinite = totalRepeats <= -1;
                var repeatCount = 1;

                var first = true;
                try
                {
                    var port = new SerialPort(config.ComPort)
                    {
                        BaudRate = config.BaudRate,
                        DataBits = config.DataBits,
                        StopBits = config.StopBits,
                        Parity = config.Parity,
                        Handshake = config.FlowControl,
                    };
                    port.Open();

                    while (!cancelSource.IsCancellationRequested && (infinite || repeatCount <= totalRepeats))
                    {
                        // Don't pause on the first iteration
                        if (first)
                            first = false;
                        else
                            await Task.Delay(config.Gap, token);

                        await port.BaseStream.WriteAsync(config.Data, 0, config.Data.Length, token);
                        var progress = infinite ? $"({repeatCount})" : $"({repeatCount}/{totalRepeats})";
                        Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}: {progress} - Send {config.Data.Length} bytes.");
                        repeatCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellations
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{Environment.NewLine}{Environment.NewLine}ERROR: {ex.Message}{Environment.NewLine}{Environment.NewLine}{ex}");
                }
            });
    }
}
