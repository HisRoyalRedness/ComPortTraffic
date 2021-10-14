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
    readonly struct Range<T>
        where T : struct, IEquatable<T>
    {
        public Range(T from, T to)
        {
            From = from;
            To = to;
        }

        public Range(T both)
        {
            From = both;
            To = both;
        }

        public T From { get; }
        public T To { get; } 
        public bool IsRange => !From.Equals(To);

        public string ToString(Func<T, string> formatter) => (IsRange) ? $"{formatter(From)} to {formatter(To)}" : $"{formatter(From)}";
    }

    class Configuration
    {
        public const int DEFAULT_BAUD = 1000000;
        public const int DEFAULT_DATA = 1000000;
        public const int DEFAULT_DATA_BITS = 8;
        public const StopBits DEFAULT_STOP_BITS = StopBits.One;
        public const Parity DEFAULT_PARITY = Parity.None;
        public const Handshake DEFAULT_FLOW_CONTROL = Handshake.None;
        public const int DEFAULT_REPEATS = 1;
        public static readonly Range<TimeSpan> DEFAULT_GAP = new Range<TimeSpan>(TimeSpan.FromSeconds(1));
        public static readonly Range<int> DEFAULT_LEN = new Range<int>(-1);

        public string ComPort { get; set; }
        public int BaudRate { get; set; } = DEFAULT_BAUD;

        public int DataBits { get; set; } = DEFAULT_DATA_BITS;

        public StopBits StopBits { get; set; } = DEFAULT_STOP_BITS;

        public Parity Parity { get; set; } = DEFAULT_PARITY;

        public Handshake FlowControl { get; set; } = DEFAULT_FLOW_CONTROL;

        public byte[] Data { get; set; } = Array.Empty<byte>();

        public Range<int> DataLen { get; set; } = DEFAULT_LEN;

        public int Repeats { get; set; } = DEFAULT_REPEATS;

        public Range<TimeSpan> Gap { get; set; } = DEFAULT_GAP;

        public bool ShowData { get; set; } = false;
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

                        else if (IsSwitchPresent(arg, CMD_SHOW_DATA))
                            config.ShowData = true;

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
                                    // Don't overwrite previous data
                                    if (config.Data.Length == 0)
                                    {
                                        if (value.Length % 2 != 0)
                                            Console.WriteLine($"WARNING: Hex data string is not even. The last nibble (0x{value[^1]}) will be discarded.");
                                        config.Data = value.HexStringToBin().ToArray();
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid hex data string '{value}'.");
                                    return false;
                                }
                                break;

                            case CMD_FILE:
                                if (File.Exists(value))
                                {
                                    config.Data = File.ReadAllBytes(value);
                                }
                                else
                                {
                                    Console.WriteLine($"ERROR: Can't find file '{value}'.");
                                    return false;
                                }
                                break;

                            case CMD_LEN:
                                if (ParseRange(value, IntParser, out var len))
                                {
                                    if (len.IsRange && (len.From > len.To || len.From < 0 || len.To < 1))
                                    {
                                        Console.WriteLine($"ERROR: Invalid data length range '{value}'.");
                                        return false;
                                    }
                                    else
                                        config.DataLen = len;
                                }
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid data length argument '{value}'.");
                                    return false;
                                }
                                break;

                            case CMD_REPEATS:
                                if (int.TryParse(value, out var repeats))
                                {
                                    if (repeats == 0)
                                        config.Repeats = 1;
                                    else if (repeats < 0)
                                        config.Repeats = -1;
                                    else
                                        config.Repeats = repeats;
                                }
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid repeats argument '{value}'.");
                                    return false;
                                }
                                break;

                            case CMD_GAP:
                                if (ParseRange(value, TimeSpanParser, out var gap))
                                    config.Gap = gap;
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

            // If -1, set to length of data
            if (!config.DataLen.IsRange || config.DataLen.From < 0)
                config.DataLen = new Range<int>(config.Data?.Length ?? 0);

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
                $"   Usage: {fileName,alignment} [{CMD_COMPORT}=]<{CMD_COMPORT}> [{CMD_BAUD}=<{CMD_BAUD}>] [{CMD_DATA}=<{CMD_DATA}>] [{CMD_LEN}=<{CMD_LEN}>[,<{CMD_LEN}>]]{Environment.NewLine}" +
                $"                               [{CMD_FILE}=<{CMD_FILE}>] [{CMD_REPEATS}=<{CMD_REPEATS}>] [{CMD_GAP}=<{CMD_GAP}[,{CMD_GAP}]>]{Environment.NewLine}" +
                $"                               [{CMD_SHOW_DATA}] {Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"      where:{Environment.NewLine}" +
                $"         {CMD_REPEATS + ":",alignment}The COM port to connect to, eg. COM1.{Environment.NewLine}" +
                $"         {CMD_BAUD + ":",alignment}The baud rate. Default is {Configuration.DEFAULT_BAUD}.{Environment.NewLine}" +
                $"         {CMD_DATA + ":",alignment}The data to send, in hex, eg. 01020E0F{Environment.NewLine}" +
                $"         {CMD_LEN + ":",alignment}The data length. It will truncate data is smaller than the length{Environment.NewLine}" +
                $"         {CMD_EMPTY,alignment}of data. If it is longer, data will be padded with random bytes.{Environment.NewLine}" +
                $"         {CMD_FILE + ":",alignment}Path to a file to load binary data from.{Environment.NewLine}" +
                $"         {CMD_EMPTY,alignment}The <{CMD_FILE}> argument overrides <{CMD_DATA}>.{Environment.NewLine}" +
                $"         {CMD_REPEATS + ":",alignment}The number of times to repeat the transmission (-1 for infinite){Environment.NewLine}" +
                $"         {CMD_GAP + ":",alignment}The number of milliseconds delay between repetitions. Default is 1s{Environment.NewLine}" +
                $"         {CMD_SHOW_DATA + ":",alignment}If present, prints the data at each transmission{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"    Some arguments optionally allow a range to be specified (eg. 1,3). When a range is given,{Environment.NewLine}" +
                $"    a random value within that range (inclusive) is chosen.{Environment.NewLine}" +
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
            const string SHOW = "Show data";
            const string DATA = "Data";
            const string LEN = "Data len";

            var dataLen = (Console.WindowWidth - Math.Abs(alignment)) / 3 - 2;
            var data = string.Join(Environment.NewLine + new string(' ', Math.Abs(alignment)), config.Data.Chunk(dataLen).Select(line => line.BinToHexString(" ")));

            var header =
                $"{COM_PORT + ":",alignment}{config.ComPort}{Environment.NewLine}" +
                $"{BAUD_RATE + ":",alignment}{config.BaudRate}{Environment.NewLine}" +
                $"{REPEATS + ":",alignment}{config.Repeats}{Environment.NewLine}" +
                $"{GAP + ":",alignment}{config.Gap.ToString(val => FormatTimeSpan(val))}{Environment.NewLine}" +
                $"{SHOW + ":",alignment}{(config.ShowData ? "Yes" : "No")}{Environment.NewLine}" +
                $"{LEN + ":",alignment}{config.DataLen.ToString(val => $"{val}")} bytes{Environment.NewLine}" +
                $"{DATA + ":",alignment}{data}{Environment.NewLine}";

            return header;
        }

        static bool IsComPort(string inp) => _comPortRegex.Match(inp).Success;
        static bool IsHexData(string inp) => _hexStringRegex.Match(inp).Success;
        static bool IsSwitchPresent(string arg, string swtch) => string.Compare(arg, swtch, true) == 0;
        static bool ParseRange<T>(string arg, Func<string,Tuple<bool, T>> converter, out Range<T> rng)
            where T : struct, IEquatable<T>
        {
            var split = arg?.Trim()?.Split(',');
            if (split.Length == 2)
            {
                var from = converter(split[0].Trim());
                var to = converter(split[1].Trim());
                if (from.Item1 && to.Item1)
                {
                    rng = new Range<T>(from.Item2, to.Item2);
                    return true;
                }
            }
            else
            {
                var both = converter(arg ?? string.Empty);
                if (both.Item1)
                {
                    rng = new Range<T>(both.Item2);
                    return true;
                }
            }
            rng = default;
            return false;
        }

        static Tuple<bool, TimeSpan> TimeSpanParser(string val) => new Tuple<bool, TimeSpan>(IsTimeSpan(val, out var ts), ts);
        static Tuple<bool, int> IntParser(string val) => new Tuple<bool, int>(int.TryParse(val, out var iVal), iVal);

        static bool IsTimeSpan(string value, out TimeSpan timespan)
        {
            var match = _timeSpanRegex.Match(value);

            if (match.Success && double.TryParse(match.Groups[1].Value, out var ts))
            {
                switch (match.Groups[2].Value.ToLower())
                {
                    case "us":
                        timespan = TimeSpan.FromMilliseconds(ts / 1000);
                        return true;
                    case "ms":
                        timespan = TimeSpan.FromMilliseconds(ts);
                        return true;
                    case "s":
                    case "":
                        timespan = TimeSpan.FromSeconds(ts);
                        return true;
                    default:
                        // Not a recognised unit
                        break;
                }
            }

            timespan = TimeSpan.Zero;
            return false;
        }

        static string FormatTimeSpan(TimeSpan ts)
        {
            var ms = ts.TotalMilliseconds;
            if (ms < 1)
                return $"{(ms * 1000):#,0.###}us";
            else if (ms >= 1000)
                return $"{(ms / 1000):#,0.###}s";
            else
                return $"{ms:#,0.###}ms";
        }

        static readonly Regex _comPortRegex = new Regex(@"^COM\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex _hexStringRegex = new Regex(@"^[0-9a-f]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex _timeSpanRegex = new Regex(@"([0-9]+\.?[0-9]*|\.[0-9]+)(us|ms|s)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        const string CMD_COMPORT = "com";
        const string CMD_BAUD = "baud";
        const string CMD_DATA = "data";
        const string CMD_FILE = "file";
        const string CMD_LEN = "len";
        const string CMD_GAP = "gap";
        const string CMD_REPEATS = "repeat";
        const string CMD_SHOW_DATA = "show";
        const string CMD_EMPTY = "";

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

        #region Serial output
        static Task SerialOutput(Configuration config, CancellationTokenSource cancelSource)
            => Task.Run(async () =>
            {
                var token = cancelSource.Token;
                var totalRepeats = config.Repeats == 0 ? 1 : config.Repeats;
                var infinite = totalRepeats <= -1;
                var repeatCount = 1;
                var rnd = new Random();
                var pauseStr = string.Empty;

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

                    // Don't pause on first repetition
                    var pause = TimeSpan.Zero;
                    while (!cancelSource.IsCancellationRequested && (infinite || repeatCount <= totalRepeats))
                    {
                        var dataLen = config.DataLen.IsRange
                            ? config.DataLen.From + (int)((config.DataLen.To - config.DataLen.From + 1) * rnd.NextDouble())
                            : config.DataLen.From;

                        byte[] data;
                        if (config.Data.Length == dataLen)
                            data = config.Data;
                        else if (config.Data.Length > dataLen)
                            data = config.Data.Take(dataLen).ToArray();
                        else
                        {
                            var bytesNeeded = dataLen - config.Data.Length;
                            var rndData = new byte[bytesNeeded];
                            rnd.NextBytes(rndData);
                            data = config.Data.Concat(rndData).ToArray();
                        }


                        if (pause == TimeSpan.Zero)
                            pause = config.Gap.From;
                        else
                            await Task.Delay(pause, token);

                        if (data.Length > 0)
                            await port.BaseStream.WriteAsync(data, 0, data.Length, token);
                        var progress = infinite ? $"({repeatCount})" : $"({repeatCount}/{totalRepeats})";
                        repeatCount++;

                        var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}: {progress} - Send {data.Length} bytes.";

                        if (config.Gap.IsRange && !infinite && repeatCount <= totalRepeats)
                        {
                            pause = config.Gap.From + TimeSpan.FromMilliseconds((config.Gap.To - config.Gap.From).TotalMilliseconds * rnd.NextDouble());
                            line += $" Pause for {FormatTimeSpan(pause)}.";
                        }

                        if (config.ShowData)
                            line += $" Data: {data.BinToHexString(" ")}";
                        Console.WriteLine(line);
                        


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
        #endregion Serial output
    }
}
