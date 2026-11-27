using System.Reflection;
using System.Text.Json;
using Mountly.Cli;
using Mountly.Core.Images;
using Mountly.Core.Mounting;
using Mountly.Core.Util;
using Mountly.Formats;
using Mountly.Tools;
using Mountly.Windows;

return await Cli.RunAsync(args);

namespace Mountly.Cli
{
    internal static class ExitCodes
    {
        public const int Ok = 0;
        public const int Error = 1;
        public const int Usage = 2;
        public const int NeedsElevation = 3;
        public const int Mismatch = 4;
    }

    internal static class Cli
    {
        private const string Help = """
            Mountly CLI — mount disc and disk images, and the tools around them.

            Usage: mountly-cli <command> [arguments] [options]

            Mounting
              mount <image> [--letter X] [--no-letter] [--rw]
                                       Mount an ISO, VHD/VHDX, BIN/CUE, NRG, MDS/MDF or CloneCD image
              unmount <image|X:>       Eject a mounted image (by file or drive letter)
              unmount --all            Eject every mounted image
              list                     Show mounted images
              letter <X:> <Y:>         Move a mounted image from drive X: to Y: (administrator)

            Images
              info <image>             Format, tracks and file system of an image
              ls <image> [folder] [-r] List the files inside an image
              extract <image> <path> <destination>
                                       Copy a file or folder ("/" for everything) out of an image
              convert <image> <out.iso>
                                       Convert BIN/CUE, NRG, MDS/MDF or CloneCD to ISO
              create <folder> <out.iso> [--label NAME] [--boot FILE]
                                       Build an ISO (ISO 9660 + Joliet) from a folder
              rip <drive> <out.iso>    Copy a data disc in an optical drive to an ISO
              drives                   List optical drives with a disc in them

            Checksums
              hash <file> [--algo sha256,sha1,md5]
              verify <file> <checksum|checksum-file>
                                       Exit code 4 when the checksum doesn't match

            Options
              --json                   Machine-readable output (mount, unmount, list)
              -h, --help               Show this help
              --version                Show the version

            Exit codes: 0 ok, 1 error, 2 usage, 3 needs administrator rights, 4 checksum mismatch.
            """;

        public static async Task<int> RunAsync(string[] argv)
        {
            Args args;
            try
            {
                args = Args.Parse(argv);
            }
            catch (UsageException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitCodes.Usage;
            }

            using var cancel = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancel.Cancel();
            };

            var json = args.Has("--json") || args.Has("--result-file");
            try
            {
                if (args.Has("--version") || args.Command == "version")
                {
                    Console.WriteLine(Version);
                    return ExitCodes.Ok;
                }

                if (args.Has("--help") || args.Command is "help")
                {
                    Console.WriteLine(Help);
                    return ExitCodes.Ok;
                }

                return args.Command switch
                {
                    "mount" => await MountAsync(args, json, cancel.Token),
                    "unmount" or "eject" or "umount" => await UnmountAsync(args, json, cancel.Token),
                    "list" or "ls-mounts" => await ListAsync(args, json, cancel.Token),
                    "info" => Info(args),
                    "ls" => Ls(args),
                    "extract" => await ExtractAsync(args, cancel.Token),
                    "convert" => await ConvertAsync(args, cancel.Token),
                    "create" => await CreateAsync(args, cancel.Token),
                    "rip" => await RipAsync(args, cancel.Token),
                    "drives" => Drives(),
                    "letter" => Letter(args, json),
                    "hash" => await HashAsync(args, cancel.Token),
                    "verify" => await VerifyAsync(args, cancel.Token),
                    _ => throw new UsageException($"Unknown command '{args.Command}'. Run 'mountly-cli --help'."),
                };
            }
            catch (UsageException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitCodes.Usage;
            }
            catch (MountException ex)
            {
                Report(args, json, new CliResult { Ok = false, Error = ex.Error, Message = ex.Message });
                return ex.Error == MountError.NeedsElevation ? ExitCodes.NeedsElevation : ExitCodes.Error;
            }
            catch (OperationCanceledException)
            {
                Report(args, json, new CliResult { Ok = false, Error = MountError.Cancelled, Message = "Cancelled." });
                return ExitCodes.Error;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageFormatException or FormatException
                                           or NotSupportedException or InvalidOperationException or ArgumentException)
            {
                Report(args, json, new CliResult { Ok = false, Message = ex.Message });
                return ExitCodes.Error;
            }
        }

        private static string Version =>
            typeof(Cli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

        private static MountService CreateService()
        {
            if (!OperatingSystem.IsWindows()) throw new NotSupportedException("Mounting is only supported on Windows so far.");
            return new MountService(new StorageMountBackend(), new IsoConverter());
        }

        private static async Task<int> MountAsync(Args args, bool json, CancellationToken cancel)
        {
            var image = args.Require(0, "the image to mount");
            char? letter = null;
            if (args.Value("--letter") is { } l)
                letter = MountService.ParseLetter(l) ?? throw new UsageException($"'{l}' isn't a drive letter.");
            var options = new MountOptions { ReadOnly = !args.Has("--rw"), NoDriveLetter = args.Has("--no-letter"), PreferredLetter = letter };

            using var progress = new ConsoleProgress("Converting", json);
            var mounted = await CreateService().MountAsync(image, options,
                new Progress<MountProgress>(p => { if (p.Total > 0) progress.Report((p.Done, p.Total)); }), cancel);
            progress.Dispose();

            var warnings = new List<string>();
            if (letter is { } wanted && mounted.FirstLetter is { } got && char.ToUpperInvariant(got[0]) != wanted)
                warnings.Add(OperatingSystem.IsWindows() && !DriveLetters.IsElevated
                    ? $"Mounted as {got}: choosing {wanted}: needs administrator rights."
                    : $"Mounted as {got}: {wanted}: wasn't available.");

            Report(args, json, new CliResult { Ok = true, Images = [mounted], Warnings = warnings },
                () =>
                {
                    Console.WriteLine($"Mounted {Path.GetFileName(mounted.ImagePath)} as {mounted.FirstLetter ?? "(no drive letter)"}" +
                                      (mounted.IsConverted ? " (converted to ISO)" : ""));
                    foreach (var w in warnings) Console.Error.WriteLine("warning: " + w);
                });
            return ExitCodes.Ok;
        }

        private static async Task<int> UnmountAsync(Args args, bool json, CancellationToken cancel)
        {
            var service = CreateService();
            IReadOnlyList<MountedImage> done = args.Has("--all")
                ? await service.UnmountAllAsync(cancel)
                : [await service.UnmountAsync(args.Require(0, "the image or drive letter to unmount"), cancel)];
            Report(args, json, new CliResult { Ok = true, Images = done }, () =>
            {
                if (done.Count == 0) Console.WriteLine("Nothing was mounted.");
                foreach (var d in done) Console.WriteLine($"Ejected {Path.GetFileName(d.ImagePath)}{(d.FirstLetter is { } x ? " (" + x + ")" : "")}");
            });
            return ExitCodes.Ok;
        }

        private static async Task<int> ListAsync(Args args, bool json, CancellationToken cancel)
        {
            var mounted = await CreateService().ListAsync(cancel);
            Report(args, json, new CliResult { Ok = true, Images = mounted }, () =>
            {
                if (mounted.Count == 0)
                {
                    Console.WriteLine("No images are mounted.");
                    return;
                }

                foreach (var m in mounted.OrderBy(m => m.FirstLetter ?? "~"))
                    Console.WriteLine($"{(m.DriveLetters.Count > 0 ? string.Join(",", m.DriveLetters) : "-"),-6} {m.Format.DisplayName(),-8} " +
                                      $"{ByteSize.Format(m.SizeBytes),9}  {m.VolumeLabel,-20} {m.ImagePath}");
            });
            return ExitCodes.Ok;
        }

        private static int Info(Args args)
        {
            var path = args.Require(0, "the image");
            var format = ImageFormats.Detect(path);
            Console.WriteLine($"File:    {Path.GetFullPath(path)}");
            Console.WriteLine($"Format:  {format.DisplayName()}");
            if (!format.IsOptical())
            {
                Console.WriteLine($"Size:    {ByteSize.Format(new FileInfo(path).Length)}");
                return ExitCodes.Ok;
            }

            var layout = OpticalImage.ReadLayout(path);
            Console.WriteLine("Tracks:");
            foreach (var t in layout.Tracks)
                Console.WriteLine($"  {t.Number,2}  {t.ModeName,-20} {t.SectorCount,9} sectors  {ByteSize.Format(t.Length),9}  {Path.GetFileName(t.FilePath)} @ {t.FileOffset}");
            if (layout.FirstDataTrack is null) return ExitCodes.Ok;
            using var inspector = ImageInspector.Open(path);
            Console.WriteLine($"Volume:  {inspector.VolumeLabel}");
            Console.WriteLine($"FS:      {inspector.FileSystem}");
            foreach (var w in IsoConverter.Warnings(layout)) Console.WriteLine("Note:    " + w);
            return ExitCodes.Ok;
        }

        private static int Ls(Args args)
        {
            using var inspector = ImageInspector.Open(args.Require(0, "the image"));
            var folder = args.Positional.Count > 1 ? args.Positional[1] : "";
            if (!inspector.IsDirectory(folder)) throw new UsageException($"'{folder}' isn't a folder in the image.");
            var entries = args.Has("--recursive") ? inspector.Walk(folder) : inspector.List(folder);
            foreach (var e in entries)
                Console.WriteLine($"{(e.IsDirectory ? "<DIR>" : ByteSize.Format(e.Size)),10}  {e.ModifiedUtc?.ToLocalTime():yyyy-MM-dd HH:mm}  " +
                                  (args.Has("--recursive") ? e.Path : e.Name) + (e.IsDirectory ? "\\" : ""));
            return ExitCodes.Ok;
        }

        private static async Task<int> ExtractAsync(Args args, CancellationToken cancel)
        {
            using var inspector = ImageInspector.Open(args.Require(0, "the image"));
            var inner = args.Require(1, "the path inside the image (\"/\" for everything)");
            var destination = args.Require(2, "the destination folder");
            if (!inspector.Exists(inner)) throw new UsageException($"'{inner}' isn't in the image.");
            using var progress = new ConsoleProgress("Extracting", false);
            var bytes = await inspector.ExtractAsync(inner, destination, new Progress<long>(d => progress.Report((d, 0))), cancel);
            progress.Dispose();
            Console.WriteLine($"Extracted {ByteSize.Format(bytes)} to {Path.GetFullPath(destination)}");
            return ExitCodes.Ok;
        }

        private static async Task<int> ConvertAsync(Args args, CancellationToken cancel)
        {
            var source = args.Require(0, "the image to convert");
            var target = args.Require(1, "the ISO to write");
            if (File.Exists(target) && !args.Has("--force")) throw new UsageException($"'{target}' exists; add --force to overwrite it.");
            var layout = OpticalImage.ReadLayout(source);
            using var progress = new ConsoleProgress("Converting", false);
            var warnings = await new IsoConverter().ConvertToIsoAsync(source, target,
                progress.Bytes(layout.FirstDataTrack?.Length ?? 0), cancel);
            progress.Dispose();
            foreach (var w in warnings) Console.Error.WriteLine("warning: " + w);
            Console.WriteLine($"Wrote {Path.GetFullPath(target)} ({ByteSize.Format(new FileInfo(target).Length)})");
            return ExitCodes.Ok;
        }

        private static async Task<int> CreateAsync(Args args, CancellationToken cancel)
        {
            var folder = args.Require(0, "the folder");
            var target = args.Require(1, "the ISO to write");
            if (File.Exists(target) && !args.Has("--force")) throw new UsageException($"'{target}' exists; add --force to overwrite it.");
            using var progress = new ConsoleProgress("Building", false);
            var result = await IsoBuilder.BuildAsync(folder, target,
                new IsoBuildOptions { VolumeLabel = args.Value("--label"), BootImagePath = args.Value("--boot") }, progress, cancel);
            progress.Dispose();
            foreach (var s in result.Skipped) Console.Error.WriteLine($"warning: skipped '{s}' (names are limited to {IsoBuilder.MaxNameLength} characters)");
            Console.WriteLine($"Wrote {Path.GetFullPath(target)}: {result.Files} files, {ByteSize.Format(result.IsoBytes)}");
            return ExitCodes.Ok;
        }

        private static async Task<int> RipAsync(Args args, CancellationToken cancel)
        {
            if (!OperatingSystem.IsWindows()) throw new NotSupportedException("Ripping is only supported on Windows so far.");
            var drive = MountService.ParseLetter(args.Require(0, "the drive letter")) ?? throw new UsageException("Give the drive as a letter, e.g. D:");
            var target = args.Require(1, "the ISO to write");
            if (File.Exists(target) && !args.Has("--force")) throw new UsageException($"'{target}' exists; add --force to overwrite it.");
            using var progress = new ConsoleProgress("Reading", false);
            var result = await DiscRipper.RipAsync(drive, target, progress, cancel);
            progress.Dispose();
            if (result.UnreadableSectors > 0)
                Console.Error.WriteLine($"warning: {result.UnreadableSectors} sector(s) couldn't be read and were filled with zeros.");
            Console.WriteLine($"Wrote {Path.GetFullPath(target)} ({ByteSize.Format(result.Bytes)})");
            return result.UnreadableSectors > 0 ? ExitCodes.Error : ExitCodes.Ok;
        }

        private static int Letter(Args args, bool json)
        {
            if (!OperatingSystem.IsWindows()) throw new NotSupportedException("Only supported on Windows so far.");
            var from = MountService.ParseLetter(args.Require(0, "the current drive letter")) ?? throw new UsageException("Give drive letters like E: M:");
            var to = MountService.ParseLetter(args.Require(1, "the new drive letter")) ?? throw new UsageException("Give drive letters like E: M:");
            if (!DriveLetters.TryMove(from, to, out var error))
                throw new MountException(DriveLetters.IsElevated ? MountError.Unknown : MountError.NeedsElevation, error ?? "Couldn't move the drive.");
            Report(args, json, new CliResult { Ok = true, Message = $"Moved {from}: to {to}:" }, () => Console.WriteLine($"Moved {from}: to {to}:"));
            return ExitCodes.Ok;
        }

        private static int Drives()
        {
            if (!OperatingSystem.IsWindows()) throw new NotSupportedException("Only supported on Windows so far.");
            var drives = DiscRipper.OpticalDrives();
            if (drives.Count == 0) Console.WriteLine("No optical drive has a disc in it.");
            foreach (var d in drives) Console.WriteLine($"{d.Name.TrimEnd('\\'),-4} {d.VolumeLabel,-24} {ByteSize.Format(d.TotalSize),9}");
            return ExitCodes.Ok;
        }

        private static async Task<int> HashAsync(Args args, CancellationToken cancel)
        {
            var file = args.Require(0, "the file to hash");
            var names = (args.Value("--algo") ?? "sha256").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var algorithms = names.Select(n => Checksums.Parse(n) ?? throw new UsageException($"Unknown algorithm '{n}' (use md5, sha1, sha256).")).ToList();
            using var progress = new ConsoleProgress("Hashing", false);
            var hashes = await Checksums.ComputeAsync(file, algorithms, progress, cancel);
            progress.Dispose();
            foreach (var (algorithm, digest) in hashes) Console.WriteLine($"{algorithm.Name(),-8} {digest}  {Path.GetFileName(file)}");
            return ExitCodes.Ok;
        }

        private static async Task<int> VerifyAsync(Args args, CancellationToken cancel)
        {
            var file = args.Require(0, "the file to verify");
            var expected = args.Require(1, "the expected checksum or a checksum file");
            using var progress = new ConsoleProgress("Hashing", false);
            var match = await Checksums.VerifyAsync(file, expected, progress, cancel);
            progress.Dispose();
            Console.WriteLine(match.IsMatch
                ? $"OK      {match.Algorithm.Name()} matches: {match.Actual}"
                : $"FAILED  {match.Algorithm.Name()} is {match.Actual}, expected {match.Expected}");
            return match.IsMatch ? ExitCodes.Ok : ExitCodes.Mismatch;
        }

        /// <summary>Prints the result as JSON (--json), writes it for the app (--result-file), or runs the human output.</summary>
        private static void Report(Args args, bool json, CliResult result, Action? human = null)
        {
            if (args.Value("--result-file") is { } file)
            {
                File.WriteAllText(file, JsonSerializer.Serialize(result, CliJson.Default.CliResult));
                return;
            }

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, CliJson.Default.CliResult));
                return;
            }

            if (result.Ok) human?.Invoke();
            else Console.Error.WriteLine("error: " + result.Message);
        }
    }
}
