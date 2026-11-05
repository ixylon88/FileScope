using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Formats.Asn1;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using FileScope.Core.Abstractions;
using FileScope.Core.Checksums;
using FileScope.Core.Models;
using FileScope.Core.Metadata;
using FileScope.Core.Services;
using FileScope.Core.Signatures;
using FileScope.Core.Files;
using FileScope.Core.Export;

internal static class CoreChecks
{
    private static int _passed;

    private static async Task<int> Main(string[] args)
    {
        var temp = Path.Combine(Path.GetTempPath(), "FileScope-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            CheckTimestamps();
            CheckPeHeaders();
            CheckNeHeaders();
            CheckFolderScan(temp);
            await CheckCsvExport(temp);
            await CheckJsonExport(temp);
            var algorithms = Enum.GetValues<ChecksumAlgorithm>();
            var checksumService = new StreamChecksumService(new ChecksumStrategyFactory());
            var abcExpected = new Dictionary<ChecksumAlgorithm, string>
            {
                [ChecksumAlgorithm.MD5] = "900150983CD24FB0D6963F7D28E17F72",
                [ChecksumAlgorithm.SHA1] = "A9993E364706816ABA3E25717850C26C9CD0D89D",
                [ChecksumAlgorithm.SHA256] = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
                [ChecksumAlgorithm.SHA384] = "CB00753F45A35E8BB5A03D699AC65007272C32AB0EDED1631A8B605A43FF5BED8086072BA1E7CC2358BAECA134C825A7",
                [ChecksumAlgorithm.SHA512] = "DDAF35A193617ABACC417349AE20413112E6FA4E89A97EA20A9EEEE64B55D39A2192992A274FC1A836BA3C23A3FEEBBD454D4423643CE80E2A9AC94FA54CA49F"
            };
            using (var stream = new CountingStream(Encoding.ASCII.GetBytes("abc")))
            {
                var result = await checksumService.ComputeAsync(stream, algorithms);
                foreach (var algorithm in algorithms) Equal(abcExpected[algorithm], result[algorithm], $"abc / {algorithm}");
                Equal(3L, stream.BytesRead, "all five hashes read data only once");
                Check(stream.CanRead, "checksum service leaves caller-owned stream open");
            }
            using (var empty = new MemoryStream())
            {
                var result = await checksumService.ComputeAsync(empty, [ChecksumAlgorithm.SHA256]);
                Equal("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", result[ChecksumAlgorithm.SHA256], "empty file hash");
            }
            using (var stream = new CountingStream(Encoding.ASCII.GetBytes("abc")))
            {
                var result = await checksumService.ComputeAsync(stream, []);
                Equal(0, result.Count, "no algorithms / no hashes");
                Equal(0L, stream.BytesRead, "no algorithms / no data read");
            }
            using (var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc")))
            {
                var result = await checksumService.ComputeAsync(stream, [ChecksumAlgorithm.SHA256, ChecksumAlgorithm.SHA256]);
                Equal(1, result.Count, "repeated algorithm is deduplicated");
            }
            var sourceSelection = new List<ChecksumAlgorithm> { ChecksumAlgorithm.SHA256 };
            var snapshot = new AnalysisOptions(sourceSelection, false);
            sourceSelection.Clear();
            Equal(ChecksumAlgorithm.SHA256, snapshot.Algorithms.Single(), "options are an immutable snapshot");

            var largeData = Enumerable.Range(0, 2 * 1024 * 1024 + 13).Select(i => (byte)(i % 251)).ToArray();
            using (var stream = new CountingStream(largeData))
            {
                var result = await checksumService.ComputeAsync(stream, algorithms);
                // Independent whole-buffer API checks chunk boundaries and streaming composition.
                Equal(Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(largeData)),
                    result[ChecksumAlgorithm.SHA512], "multi-buffer streaming hash");
                Equal((long)largeData.Length, stream.BytesRead, "large file / one pass");
                Check(stream.ReadCalls > 2, "large file uses bounded chunks");
            }
            using (var cancelled = new CancellationTokenSource())
            using (var stream = new CountingStream(largeData))
            {
                cancelled.Cancel();
                await ExpectCancellation(() => checksumService.ComputeAsync(stream, algorithms, cancellationToken: cancelled.Token));
                Equal(0L, stream.BytesRead, "pre-cancelled operation performs no reads");
            }
            var factory = new TrackingFactory();
            using (var cancelled = new CancellationTokenSource())
            using (var stream = new CountingStream(largeData, () => cancelled.Cancel()))
            {
                await ExpectCancellation(() => new StreamChecksumService(factory).ComputeAsync(stream, algorithms, cancellationToken: cancelled.Token));
                Equal(5, factory.Disposed, "all strategies disposed after cancellation");
            }
            var partialFactory = new TrackingFactory();
            using (var stream = new MemoryStream())
            {
                try
                {
                    await new StreamChecksumService(partialFactory).ComputeAsync(stream,
                        [ChecksumAlgorithm.SHA256, (ChecksumAlgorithm)999]);
                    throw new Exception("Expected invalid-algorithm failure.");
                }
                catch (ArgumentOutOfRangeException) { }
                Equal(1, partialFactory.Disposed, "partial construction cleans up strategies");
            }

            var path = Path.Combine(temp, "Prüfung 中文 ' sample.bin");
            await File.WriteAllTextAsync(path, "abc", new UTF8Encoding(false));
            CheckFileProperties(path);
            await CheckMetadataPipeline(path, checksumService);
            var signatures = new StubSignatures();
            var analyzer = new FileAnalysisService(checksumService, signatures);
            var analyzed = await analyzer.AnalyzeAsync(path, new AnalysisOptions(algorithms, true));
            Check(analyzed.Succeeded, "Unicode and apostrophe path");
            Equal(3L, analyzed.Length!.Value, "file size");
            Equal(abcExpected[ChecksumAlgorithm.SHA256], analyzed.Checksums[ChecksumAlgorithm.SHA256], "file pipeline hash");
            Check(analyzed.LastWriteTime.HasValue, "modification timestamp");
            Equal(1, signatures.Calls, "signature strategy is invoked");
            Check(signatures.SawReadableHandle, "signature receives the open readable stream");
            var metadataOnly = await analyzer.AnalyzeAsync(path, new AnalysisOptions([], false));
            Check(metadataOnly.Succeeded && metadataOnly.Checksums.Count == 0, "metadata-only analysis");
            Equal(1, signatures.Calls, "disabled signatures do not invoke native strategy");
            Equal(SignatureStatus.NotChecked, metadataOnly.Signature.Status, "not checked is explicit");
            Equal(AnalysisError.NotFound, (await analyzer.AnalyzeAsync(Path.Combine(temp, "missing.bin"), snapshot)).Error,
                "missing file becomes a per-file failure");
            Equal(AnalysisError.DirectoryNotSupported, (await analyzer.AnalyzeAsync(temp, snapshot)).Error,
                "directory drop is reported, never traversed");

            var native = typeof(WindowsAuthenticodeInspector).GetNestedType("Native", BindingFlags.NonPublic)!;
            var nativeData = native.GetNestedType("WinTrustData", BindingFlags.NonPublic)!;
            var nativeFile = native.GetNestedType("WinTrustFileInfo", BindingFlags.NonPublic)!;
            var nativeSettings = native.GetNestedType("WinTrustSignatureSettings", BindingFlags.NonPublic)!;
            Equal(IntPtr.Size == 8 ? 88 : 52, Marshal.SizeOf(nativeData), "WINTRUST_DATA ABI size");
            Equal(IntPtr.Size == 8 ? 32 : 16, Marshal.SizeOf(nativeFile), "WINTRUST_FILE_INFO ABI size");
            Equal(IntPtr.Size == 8 ? 32 : 24, Marshal.SizeOf(nativeSettings), "WINTRUST_SIGNATURE_SETTINGS ABI size");

            var nativeSigner = native.GetNestedType("ProviderSigner", BindingFlags.NonPublic)!;
            var nativeMessage = native.GetNestedType("MessageSignerInfo", BindingFlags.NonPublic)!;
            var nativeAttribute = native.GetNestedType("CryptAttribute", BindingFlags.NonPublic)!;
            Equal(IntPtr.Size == 8 ? 64 : 44, Marshal.SizeOf(nativeSigner), "CRYPT_PROVIDER_SGNR ABI size");
            Equal(4L, Marshal.OffsetOf(nativeSigner, "VerifyAsOf").ToInt64(), "FILETIME keeps DWORD alignment");
            Equal(IntPtr.Size == 8 ? 136 : 68, Marshal.SizeOf(nativeMessage), "CMSG_SIGNER_INFO ABI size");
            Equal(IntPtr.Size == 8 ? 120L : 60L, Marshal.OffsetOf(nativeMessage, "UnauthenticatedAttributes").ToInt64(),
                "CMSG_SIGNER_INFO unsigned attributes offset");
            Equal(IntPtr.Size == 8 ? 24 : 12, Marshal.SizeOf(nativeAttribute), "CRYPT_ATTRIBUTE ABI size");

            if (OperatingSystem.IsWindows())
            {
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Equal(AnalysisError.ReadFailure, (await analyzer.AnalyzeAsync(path, snapshot)).Error,
                        "locked file is reported as a read failure");
                }
                var nativeAnalyzer = new FileAnalysisService(checksumService, new WindowsAuthenticodeInspector());
                var unsigned = await nativeAnalyzer.AnalyzeAsync(typeof(FileAnalysisService).Assembly.Location,
                    new AnalysisOptions([], true));
                Equal(SignatureStatus.NoEmbeddedSignature, unsigned.Signature.Status, "unsigned PE / native WinVerifyTrust");
                Check(unsigned.Signature.Certificate is null, "unsigned PE has no signer metadata");
                Equal(0, unsigned.Signature.Signatures.Count, "unsigned PE has no selectable signature");
                if (args.Length == 2 && args[0] == "--signed-file")
                {
                    var signed = await nativeAnalyzer.AnalyzeAsync(args[1], new AnalysisOptions([], true));
                    Check(signed.Succeeded, "signed fixture can be read");
                    Check(signed.Signature.Certificate is not null, "embedded signer metadata extracted");
                    Check(signed.Signature.NativeStatusCode.HasValue, "native trust result retained");
                    Check(signed.Signature.Signatures.Count >= 1, "signed fixture exposes signature entries");
                    Check(signed.Signature.Signatures.Select(s => s.Index).SequenceEqual(
                        Enumerable.Range(0, signed.Signature.Signatures.Count).Select(i => (uint)i)),
                        "signature indices are complete and unique");
                    foreach (var entry in signed.Signature.Signatures)
                        Console.WriteLine($"INFO signature {entry.Index + 1}: {entry.Status}, {entry.DigestAlgorithm}, {entry.Timestamp}");
                    Console.WriteLine($"INFO signed fixture: {signed.Signature.Status}, 0x{signed.Signature.NativeStatusCode:X8}");
                    // Trust depends on this machine's roots, clock and cached revocation data.
                    // This does not assert that an arbitrary supplied file is trusted.
                }
                else Console.WriteLine("SKIP signed-file check: pass -SignedFile to Verify.ps1.");
            }
            else
            {
                await using var file = File.OpenRead(path);
                Equal(SignatureStatus.UnsupportedPlatform,
                    new WindowsAuthenticodeInspector().Inspect(file, path, RevocationMode.OfflineCache).Status,
                    "non-Windows signature inspection is explicit");
                Console.WriteLine("SKIP native Windows checks on this operating system.");
            }
            Console.WriteLine($"PASS: {_passed} core assertions.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temp, true); }
    }

    private static void CheckFolderScan(string temp)
    {
        var root = Path.Combine(temp, "folder-input");
        var nested = Path.Combine(root, "sub", "中文");
        Directory.CreateDirectory(nested);
        var first = Path.Combine(root, "first.bin");
        var second = Path.Combine(nested, "Prüfung.txt");
        var hidden = Path.Combine(root, ".hidden");
        File.WriteAllText(first, "one");
        File.WriteAllText(second, "two");
        File.WriteAllText(hidden, "three");
        if (OperatingSystem.IsWindows()) File.SetAttributes(hidden, FileAttributes.Hidden);
        var scanner = new FolderScanner();
        var found = scanner.Scan(root);
        Check(found.Files.ToHashSet(StringComparer.Ordinal).SetEquals([first, second, hidden]),
            "folder scan includes nested, Unicode and hidden files exactly once");
        Check(found.Issues.Count == 0 && found.SkippedDirectoryLinks == 0, "normal scan has no omissions");
        var empty = Path.Combine(temp, "empty-folder");
        Directory.CreateDirectory(empty);
        Check(scanner.Scan(empty).Files.Count == 0, "empty folder is a valid empty result");
        try { scanner.Scan(Path.Combine(root, "missing")); throw new Exception("Expected missing root failure."); }
        catch (DirectoryNotFoundException) { Check(true, "missing selected folder is reported"); }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { scanner.Scan(root, cancelled.Token); throw new Exception("Expected scan cancellation."); }
            catch (OperationCanceledException) { Check(true, "folder enumeration observes cancellation"); }
        }

        var link = Path.Combine(nested, "back-to-root");
        var created = false;
        try { Directory.CreateSymbolicLink(link, root); created = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        { Console.WriteLine("SKIP symbolic-link fixture: " + ex.Message); }
        if (created)
        {
            try
            {
                found = scanner.Scan(root);
                Check(found.SkippedDirectoryLinks == 1 && found.Files.Count == 3,
                    "directory link to an ancestor is counted and never traversed");
            }
            finally { Directory.Delete(link); }
        }
    }

    private static async Task CheckCsvExport(string temp)
    {
        var exporter = new ResultExporter([new CsvExportStrategy(), new JsonExportStrategy()]);
        var path = Path.Combine(temp, "results.csv");
        var table = new ResultExportTable([new("Name", "Name"), new("Notes", "Notes"), new("Hash", "Hash")],
            [["Prüfung 中文, sample.exe", "Line 1\r\n\"quoted\"; line 2", "+///"], ["second", "", "00FF"]]);
        await exporter.ExportAsync(path, table);
        var bytes = await File.ReadAllBytesAsync(path);
        Check(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "CSV has a UTF-8 BOM for Unicode readers");
        Equal("\"Name\";\"Notes\";\"Hash\"\r\n\"Prüfung 中文, sample.exe\";\"Line 1\r\n\"\"quoted\"\"; line 2\";\"+///\"\r\n\"second\";\"\";\"00FF\"\r\n",
            await File.ReadAllTextAsync(path), "CSV uses semicolon by default and preserves embedded semicolons");
        await exporter.ExportAsync(path, table, new ResultExportOptions(CsvDelimiter: CsvDelimiter.Comma));
        Equal("\"Name\",\"Notes\",\"Hash\"\r\n\"Prüfung 中文, sample.exe\",\"Line 1\r\n\"\"quoted\"\"; line 2\",\"+///\"\r\n\"second\",\"\",\"00FF\"\r\n",
            await File.ReadAllTextAsync(path), "CSV quotes delimiters, embedded quotes/newlines and empty fields without altering hashes");

        var sentinel = "existing export must survive";
        await File.WriteAllTextAsync(path, sentinel);
        try { await exporter.ExportAsync(path, table, new ResultExportOptions(CsvDelimiter: (CsvDelimiter)999)); throw new Exception("Expected invalid delimiter failure."); }
        catch (ArgumentOutOfRangeException) { Check(true, "unsupported delimiters are rejected before writing"); }
        Equal(sentinel, await File.ReadAllTextAsync(path), "an invalid delimiter preserves an existing destination");
        try
        {
            await exporter.ExportAsync(path, new ResultExportTable([new("A", "A"), new("B", "B")], [["1", "2"], ["bad width"]]));
            throw new Exception("Expected invalid row failure.");
        }
        catch (ArgumentException) { Check(true, "CSV rejects inconsistent row lengths"); }
        Equal(sentinel, await File.ReadAllTextAsync(path), "a failed export preserves an existing destination");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var absent = Path.Combine(temp, "cancelled.csv");
            await ExpectCancellation(() => exporter.ExportAsync(absent, table, cancellationToken: cancelled.Token));
            Check(!File.Exists(absent), "pre-cancelled export creates no destination");
        }
        using (var cancelled = new CancellationTokenSource())
        {
            await ExpectCancellation(() => exporter.ExportAsync(path,
                new ResultExportTable([new("Name", "Name")], new CancellingRows(cancelled)), cancellationToken: cancelled.Token));
            Equal(sentinel, await File.ReadAllTextAsync(path), "cancellation after writing begins preserves the existing destination");
        }
        try { await exporter.ExportAsync(path, new ResultExportTable([], [])); throw new Exception("Expected empty columns failure."); }
        catch (ArgumentException) { Check(true, "CSV requires at least one column"); }
        await exporter.ExportAsync(path, new ResultExportTable([new("Name", "Name")], [["replacement"]]));
        Equal("\"Name\"\r\n\"replacement\"\r\n", await File.ReadAllTextAsync(path), "a successful export replaces the destination completely");
        Check(!Directory.EnumerateFiles(temp, ".filescope-export-*.tmp").Any(), "successful, failed and cancelled exports clean up temporary files");

        // Parse with a separate BCL CSV reader, not with the writer's escaping logic.
        var many = new ResultExportTable([new("Name", "Name"), new("Comment", "Comment"), new("Hash", "Hash")], Enumerable.Range(0, 1500)
            .Select(index => (IReadOnlyList<string>)new[]
            {
                "Prüfung 中文; file," + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                " Line 1\r\n\"quoted\"; line 2, ", "0000ABCDEF+/=="
            }).ToArray());
        foreach (var delimiter in Enum.GetValues<CsvDelimiter>())
        {
            await exporter.ExportAsync(path, many, new ResultExportOptions(CsvDelimiter: delimiter));
            using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(path, Encoding.UTF8)
            { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
            parser.SetDelimiters(delimiter == CsvDelimiter.Semicolon ? ";" : ",");
            Check(parser.ReadFields()!.SequenceEqual(many.Columns.Select(column => column.Header)), "CSV header imports correctly / " + delimiter);
            var imported = 0;
            var matches = true;
            while (!parser.EndOfData)
            {
                var row = parser.ReadFields();
                matches &= imported < many.Rows.Count && row is not null && row.SequenceEqual(many.Rows[imported]);
                imported++;
            }
            Check(matches && imported == 1500,
                "all 1500 rows round-trip with delimiters, quotes, CRLF, Unicode, spaces and leading zeros / " + delimiter);
        }
    }

    private static async Task CheckJsonExport(string temp)
    {
        var exporter = new ResultExporter([new CsvExportStrategy(), new JsonExportStrategy()]);
        var options = new ResultExportOptions(ResultExportFormat.Json);
        var path = Path.Combine(temp, "results.JSON");
        var table = new ResultExportTable([new("Name", "Dateiname"), new("SHA256", "Wert"), new("Notes", "Wert")],
            Enumerable.Range(0, 1500).Select(index => (IReadOnlyList<string>)new[]
            {
                "Prüfung 中文 😀; file," + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "0000ABCDEF+/==", index == 0 ? "" : "Line 1\r\n\"quoted\"\t\\path\u0001"
            }).ToArray());
        await exporter.ExportAsync(path, table, options);
        var bytes = await File.ReadAllBytesAsync(path);
        Check(bytes[0] == (byte)'[', "JSON is UTF-8 without BOM");
        using (var parsed = System.Text.Json.JsonDocument.Parse(bytes))
        {
            var rows = parsed.RootElement;
            Check(rows.GetArrayLength() == 1500, "JSON contains all 1500 exported rows");
            Check(rows.EnumerateArray().Select((row, index) =>
                row.EnumerateObject().Select(property => property.Name).SequenceEqual(table.Columns.Select(column => column.Id))
                && row.EnumerateObject().Select(property => property.Value.GetString()).SequenceEqual(table.Rows[index])).All(match => match),
                "JSON round-trips selected IDs/order, Unicode, quotes, controls, leading zeros and empty strings");
            Check(rows[0].GetProperty("SHA256").ValueKind == System.Text.Json.JsonValueKind.String,
                "JSON retains display values as strings instead of coercing hashes and versions");
        }
        var sentinel = "existing export must survive";
        await File.WriteAllTextAsync(path, sentinel);
        try
        {
            await exporter.ExportAsync(path, new ResultExportTable([new("Name", "Name")], [["good"], []]), options);
            throw new Exception("Expected invalid row failure.");
        }
        catch (ArgumentException) { Check(true, "JSON rejects inconsistent row widths"); }
        Equal(sentinel, await File.ReadAllTextAsync(path), "failed JSON export preserves the destination");
        foreach (var precancel in new[] { false, true })
        {
            using var cancelled = new CancellationTokenSource();
            if (precancel) cancelled.Cancel();
            await ExpectCancellation(() => exporter.ExportAsync(path,
                new ResultExportTable([new("Name", "Name")], new CancellingRows(cancelled)), options, cancelled.Token));
            Equal(sentinel, await File.ReadAllTextAsync(path), "cancelled JSON preserves the destination / " + precancel);
        }
        foreach (var columns in new ResultExportColumn[][]
        {
            [], [new("duplicate", "A"), new("duplicate", "B")], [new("", "A")]
        })
        {
            try
            {
                await exporter.ExportAsync(path, new ResultExportTable(columns, []), options);
                throw new Exception("Expected invalid columns failure.");
            }
            catch (ArgumentException) { Check(true, "JSON rejects missing or duplicate column IDs"); }
        }
        try
        {
            await exporter.ExportAsync(path, table, new ResultExportOptions((ResultExportFormat)999));
            throw new Exception("Expected unsupported format failure.");
        }
        catch (ArgumentOutOfRangeException) { Check(true, "unsupported export strategies are rejected"); }
        Equal(sentinel, await File.ReadAllTextAsync(path), "invalid export options preserve an existing destination");
        await exporter.ExportAsync(path, new ResultExportTable([new("Name", "Name")], []), options);
        using (var parsed = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path)))
            Check(parsed.RootElement.GetArrayLength() == 0, "empty JSON data produces a valid empty array");
        Check(!Directory.EnumerateFiles(temp, ".filescope-export-*.tmp").Any(), "JSON failure/cancellation/success leaves no temporary files");
        Check(ResultExportFormats.TryFromPath("folder.csv/result.JsOn", out var json) && json == ResultExportFormat.Json
            && ResultExportFormats.TryFromPath("result.json.CSV", out var csv) && csv == ResultExportFormat.Csv,
            "format follows the final extension, case-insensitively");
        Check(!ResultExportFormats.TryFromPath("result", out _) && !ResultExportFormats.TryFromPath("result.txt", out _),
            "unsupported or missing extensions cannot silently select a format");
    }

    private sealed class CancellingRows(CancellationTokenSource cancellation) : IReadOnlyList<IReadOnlyList<string>>
    {
        public int Count => 2;
        public IReadOnlyList<string> this[int index] => new[] { index.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        public IEnumerator<IReadOnlyList<string>> GetEnumerator()
        {
            yield return this[0];
            cancellation.Cancel();
            yield return this[1];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void CheckPeHeaders()
    {
        var inspector = new PeHeaderInspector();
        foreach (var (code, name, plus) in new (ushort, string, bool)[]
        {
            (0x014c, "i386", false), (0x0184, "Alpha", false), (0x0284, "Alpha64", true),
            (0x01c0, "ARM", false), (0x01c4, "ARMNT (Thumb-2)", false),
            (0x8664, "AMD64 (x64)", true), (0xaa64, "ARM64", true),
            (0xa641, "ARM64EC", true), (0xa64e, "ARM64X", true), (0xffff, "0xFFFF", false)
        })
        {
            using var stream = new MemoryStream(PeFixture(code, plus));
            stream.Position = 17;
            var result = inspector.Inspect(stream);
            Check(result is { Diagnostic: null, Header: not null } && result.Header.MachineCode == code
                && result.Header.MachineName == name && result.Header.Format == (plus ? "PE32+" : "PE32")
                && result.Header.ManagedPlatform == ManagedPlatform.None, "native PE machine and format / " + name);
            Check(stream.CanRead && stream.Position == 17, "PE inspection preserves stream ownership and position / " + name);
        }
        foreach (var (flags, dll, nativeHeader, expected) in new (uint, bool, bool, ManagedPlatform)[]
        {
            (1, false, false, ManagedPlatform.AnyCpu), (1, true, false, ManagedPlatform.AnyCpu),
            (3, false, false, ManagedPlatform.MachineSpecific),
            (0x20001, false, false, ManagedPlatform.Prefer32Bit),
            (0x20003, false, false, ManagedPlatform.Prefer32Bit),
            (0x20001, true, false, ManagedPlatform.MachineSpecific),
            (0, false, false, ManagedPlatform.MachineSpecific),
            (17, false, false, ManagedPlatform.MachineSpecific),
            (1, false, true, ManagedPlatform.MachineSpecific)
        })
        {
            using var stream = new MemoryStream(PeFixture(0x014c, false, flags, dll, nativeHeader));
            var result = inspector.Inspect(stream);
            Check(result.Diagnostic is null && result.Header?.ManagedPlatform == expected,
                $"CLR flags / {flags:X} / DLL={dll} / native header={nativeHeader}: {result.Diagnostic}");
        }
        using (var stream = new MemoryStream(PeFixture(0x8664, true, 1)))
            Equal(ManagedPlatform.MachineSpecific, inspector.Inspect(stream).Header!.ManagedPlatform, "IL-only x64 stays machine-specific");
        using (var stream = File.OpenRead(typeof(FileAnalysisService).Assembly.Location))
            Equal(ManagedPlatform.AnyCpu, inspector.Inspect(stream).Header!.ManagedPlatform, "real SDK-built AnyCPU assembly");

        var dosOnly = new byte[128];
        dosOnly[0] = (byte)'M'; dosOnly[1] = (byte)'Z';
        var le = PeFixture(0x014c, false); le[128] = (byte)'L';
        var hugeOffset = (byte[])dosOnly.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(hugeOffset.AsSpan(60), uint.MaxValue);
        foreach (var bytes in new byte[][] { [], "ordinary text"u8.ToArray(), new byte[512], dosOnly, le, hugeOffset })
        {
            using var stream = new MemoryStream(bytes);
            Check(inspector.Inspect(stream) is { Header: null, Diagnostic: null }, "unsupported header input stays empty without error");
        }
        foreach (var bytes in new[] { PeFixture(0x014c, false)[..140], PeFixture(0x014c, false)[..200] })
        {
            using var stream = new MemoryStream(bytes);
            stream.Position = 7;
            Check(inspector.Inspect(stream) is { Header: null, Diagnostic: not null } && stream.Position == 7,
                "truncated PE reports a diagnostic and restores the stream");
        }
        using (var stream = new PeCountingStream(new byte[2 * 1024 * 1024]))
        {
            stream.Write(PeFixture(0x014c, false));
            stream.Position = 35;
            Check(inspector.Inspect(stream).Header?.MachineCode == 0x014c && stream.BytesRead < 64 * 1024,
                "PE inspection reads headers without scanning the file payload");
        }
        using (var cancelled = new CancellationTokenSource())
        using (var stream = new PeCountingStream(PeFixture(0x014c, false)))
        {
            cancelled.Cancel();
            try { inspector.Inspect(stream, cancelled.Token); throw new Exception("Expected PE cancellation."); }
            catch (OperationCanceledException) { }
            Equal(0L, stream.BytesRead, "cancelled PE inspection performs no reads");
        }
    }

    private static void CheckNeHeaders()
    {
        var inspector = new PeHeaderInspector();
        foreach (var dll in new[] { false, true })
        {
            using var stream = new PeCountingStream(NeFixture(dll));
            stream.Position = 17;
            var result = inspector.Inspect(stream);
            Check(result is { Diagnostic: null, Header: { MachineCode: null, MachineName: "x86 (16-bit)", Format: "NE (Win16)", ManagedPlatform: ManagedPlatform.None } },
                "Win16 NE header is recognized without a fabricated PE machine code / DLL=" + dll);
            Check(stream.CanRead && stream.Position == 17 && stream.BytesRead <= 128,
                "NE detection reuses the stream, restores its position and reads only fixed headers / DLL=" + dll);
        }
        foreach (var (target, format, machine) in new (byte, string, string)[]
        {
            (1, "NE (OS/2)", "x86 (16-bit)"), (3, "NE (DOS 4.x)", "x86 (16-bit)"),
            (4, "NE (Windows/386)", "x86"), (255, "NE (target 0xFF)", "x86")
        })
        {
            using var stream = new MemoryStream(NeFixture(false, target));
            var result = inspector.Inspect(stream);
            Check(result.Header is { } header && header.Format == format && header.MachineName == machine,
                "NE target is preserved without assuming every NE image is Win16 / " + target);
        }
        var shortOs2 = NeFixture(true, 1)[..188];
        BinaryPrimitives.WriteUInt16LittleEndian(shortOs2.AsSpan(128 + 6), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(shortOs2.AsSpan(128 + 0x1c), 0);
        using (var stream = new MemoryStream(shortOs2))
            Check(inspector.Inspect(stream).Header?.Format == "NE (OS/2)", "original 60-byte OS/2 NE header is accepted");
        var hugeSegments = NeFixture(false);
        BinaryPrimitives.WriteUInt16LittleEndian(hugeSegments.AsSpan(128 + 0x1c), ushort.MaxValue);
        var overlap = NeFixture(false);
        BinaryPrimitives.WriteUInt16LittleEndian(overlap.AsSpan(128 + 0x22), 32);
        var outsideEntry = NeFixture(false);
        BinaryPrimitives.WriteUInt16LittleEndian(outsideEntry.AsSpan(128 + 4), ushort.MaxValue);
        foreach (var bytes in new[] { NeFixture(false)[..130], NeFixture(false)[..188], hugeSegments, overlap, outsideEntry })
        {
            using var stream = new MemoryStream(bytes);
            stream.Position = 7;
            Check(inspector.Inspect(stream) is { Header: null, Diagnostic: not null } && stream.Position == 7,
                "truncated NE header or invalid table bounds report a diagnostic and restore the stream");
        }
        using var cancellation = new CancellationTokenSource();
        using var cancelled = new PeCountingStream(NeFixture(false));
        cancellation.Cancel();
        try { inspector.Inspect(cancelled, cancellation.Token); throw new Exception("Expected NE cancellation."); }
        catch (OperationCanceledException) { Check(cancelled.BytesRead == 0, "cancelled NE inspection reads nothing"); }
    }

    // A small Win16 NE image: one code segment, entry table and DOS stub.
    // Offsets follow the NE specification, independently of the production reader.
    private static byte[] NeFixture(bool dll, byte target = 2)
    {
        var bytes = new byte[1024];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        U16(2, 128); U16(4, 1); U16(8, 4); U16(24, 64); U32(60, 128);
        bytes[128] = (byte)'N'; bytes[129] = (byte)'E'; bytes[130] = 5;
        U16(128 + 4, 72); U16(128 + 6, 2);
        U16(128 + 0x0c, (ushort)(dll ? 0x8000 : 0));
        U16(128 + 0x16, 1); U16(128 + 0x1c, 1);
        U16(128 + 0x22, 64); U16(128 + 0x32, 4); bytes[128 + 0x36] = target;
        U16(128 + 0x3e, 0x030a); // Windows 3.10
        U16(192, 32); U16(194, 16); U16(196, 0); U16(198, 16);
        bytes[512] = 0xcb; // RETF; inspected as bytes, never executed.
        return bytes;
    }

    // A minimal PE image with one .text section and an optional CLR header.
    // Fields are encoded independently of the framework parser used by production code.
    private static byte[] PeFixture(ushort machine, bool plus, uint? corFlags = null, bool dll = false, bool nativeHeader = false)
    {
        var bytes = new byte[1024];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; U32(60, 128);
        bytes[128] = (byte)'P'; bytes[129] = (byte)'E';
        U16(132, machine); U16(134, 1);
        var optionalSize = plus ? 240 : 224;
        U16(148, (ushort)optionalSize); U16(150, (ushort)(2 | (dll ? 0x2000 : 0)));
        const int optional = 152;
        U16(optional, (ushort)(plus ? 0x20b : 0x10b));
        U32(optional + 32, 0x2000); U32(optional + 36, 0x200);
        U32(optional + 56, 0x4000); U32(optional + 60, 0x200); U16(optional + 68, 2);
        U32(optional + (plus ? 108 : 92), 16);
        var section = optional + optionalSize;
        Encoding.ASCII.GetBytes(".text").CopyTo(bytes, section);
        U32(section + 8, 512); U32(section + 12, 0x2000);
        U32(section + 16, 512); U32(section + 20, 512); U32(section + 36, 0x60000020);
        if (corFlags.HasValue)
        {
            var corDirectory = optional + (plus ? 112 : 96) + 14 * 8;
            U32(corDirectory, 0x2000); U32(corDirectory + 4, 72);
            U32(512, 72); U16(516, 2); U16(518, 5); U32(528, corFlags.Value);
            U32(520, 0x2100); U32(524, 24); // Bounded metadata directory.
            U32(768, 0x424A5342); U16(772, 1); U16(774, 1); U32(780, 4);
            Encoding.ASCII.GetBytes("v4\0\0").CopyTo(bytes, 784);
            if (nativeHeader) { U32(576, 0x2180); U32(580, 4); }
        }
        return bytes;
    }

    private static void CheckFileProperties(string plainFile)
    {
        var native = typeof(FilePropertiesInspector).GetNestedType("Native", BindingFlags.NonPublic)!;
        var shellInfo = native.GetNestedType("ShellFileInfo", BindingFlags.NonPublic)!;
        Equal(IntPtr.Size == 8 ? 696 : 692, Marshal.SizeOf(shellInfo), "SHFILEINFOW ABI size");
        var inspector = new FilePropertiesInspector();
        var plain = inspector.Inspect(plainFile);
        Check(plain is { Diagnostic: null, Info: not null } && string.IsNullOrEmpty(plain.Info.FileVersion)
            && string.IsNullOrEmpty(plain.Info.ProductName), "ordinary file has no fabricated version information");
        var assembly = inspector.Inspect(typeof(FileAnalysisService).Assembly.Location);
        Check(assembly is { Diagnostic: null, Info: not null } && !string.IsNullOrWhiteSpace(assembly.Info.FileVersion)
            && assembly.Info.OriginalFilename == "FileScope.Core.dll", "read VERSIONINFO from the built core assembly");
        Check(!string.IsNullOrWhiteSpace(assembly.Info?.FileType), "file type is available independently of version fields");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { inspector.Inspect(plainFile, cancelled.Token); throw new Exception("Expected properties cancellation."); }
        catch (OperationCanceledException) { Check(true, "cancelled properties inspection propagates cancellation"); }
    }

    private static async Task CheckMetadataPipeline(string path, IChecksumService checksums)
    {
        foreach (var flags in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var metadata = new StubMetadata();
            var signatures = new StubSignatures();
            var analyzer = new FileAnalysisService(checksums, signatures, metadata, metadata);
            var result = await analyzer.AnalyzeAsync(path, new AnalysisOptions([ChecksumAlgorithm.SHA256], true,
                inspectFileProperties: flags.Item1, inspectPeHeader: flags.Item2));
            Check(result.Succeeded && metadata.PropertyCalls == (flags.Item1 ? 1 : 0)
                && metadata.PeCalls == (flags.Item2 ? 1 : 0) && (result.Properties is not null) == flags.Item1
                && (result.PeHeader is not null) == flags.Item2, "metadata options independently control inspection and results / " + flags);
            Check(signatures.SawReadableHandle && result.Checksums.Count == 1,
                "metadata inspection retains hashes and resets the stream for signature verification / " + flags);
        }
        var throwing = new StubMetadata { ThrowProperties = true, ThrowPe = true };
        var service = new FileAnalysisService(checksums, new StubSignatures(), throwing, throwing);
        var partial = await service.AnalyzeAsync(path, new AnalysisOptions([ChecksumAlgorithm.SHA256], false,
            inspectFileProperties: true, inspectPeHeader: true));
        Check(partial.Succeeded && partial.Properties?.Diagnostic == "Properties unavailable"
            && partial.PeHeader?.Diagnostic == "PE unreadable" && throwing.PeCalls == 1 && partial.Checksums.Count == 1,
            "optional metadata failures preserve hashes and allow the other inspector to run");
        using (var cancelled = new CancellationTokenSource())
        {
            var metadata = new StubMetadata { Cancel = cancelled };
            service = new FileAnalysisService(checksums, new StubSignatures(), metadata, metadata);
            await ExpectCancellation(() => service.AnalyzeAsync(path, new AnalysisOptions([], false,
                inspectFileProperties: true, inspectPeHeader: true), cancellationToken: cancelled.Token));
            Equal(0, metadata.PeCalls, "cancellation during properties does not become a diagnostic or run the next inspector");
        }
        // Content, not an extension, identifies the PE header.
        await File.WriteAllBytesAsync(path, PeFixture(0x0184, false));
        var real = await new FileAnalysisService(checksums, new StubSignatures()).AnalyzeAsync(path,
            new AnalysisOptions([], false, inspectPeHeader: true));
        Check(real.PeHeader?.Header?.MachineName == "Alpha" && real.Properties is null,
            "real pipeline reads PE from a .bin file with properties disabled");
        foreach (var dll in new[] { false, true })
        {
            var image = NeFixture(dll);
            var legacyPath = path + (dll ? ".dll" : ".exe");
            await File.WriteAllBytesAsync(legacyPath, image);
            var legacy = await new FileAnalysisService(checksums, new StubSignatures()).AnalyzeAsync(legacyPath,
                new AnalysisOptions([ChecksumAlgorithm.SHA256], false, inspectPeHeader: true));
            Check(legacy.PeHeader is { Diagnostic: null, Header.Format: "NE (Win16)" }
                && legacy.Checksums[ChecksumAlgorithm.SHA256] == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image)),
                "real pipeline hashes and recognizes Win16 EXE/DLL without execution / DLL=" + dll);
        }
        await File.WriteAllTextAsync(path, "abc", new UTF8Encoding(false));
    }

    private sealed class StubMetadata : IFilePropertiesInspector, IPeHeaderInspector
    {
        public int PropertyCalls, PeCalls;
        public bool ThrowProperties, ThrowPe;
        public CancellationTokenSource? Cancel;
        public FilePropertiesResult Inspect(string path, CancellationToken cancellationToken = default)
        {
            PropertyCalls++;
            Cancel?.Cancel(); cancellationToken.ThrowIfCancellationRequested();
            if (ThrowProperties) throw new IOException("Properties unavailable");
            return new(new FilePropertiesInfo { ProductName = "Example product" });
        }
        public PeHeaderResult Inspect(Stream stream, CancellationToken cancellationToken = default)
        {
            PeCalls++;
            if (ThrowPe) throw new BadImageFormatException("PE unreadable");
            Check(stream.Position == 0 && stream.CanRead, "PE strategy receives the original readable stream at offset zero");
            stream.Position = stream.Length;
            return new(new PeHeaderInfo(0x014c, "i386", "PE32", ManagedPlatform.AnyCpu));
        }
    }

    private sealed class PeCountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long BytesRead;
        public override int Read(byte[] buffer, int offset, int count)
        { var read = base.Read(buffer, offset, count); BytesRead += read; return read; }
        public override int Read(Span<byte> buffer)
        {
            // MemoryStream subclasses may route this overload through Read(byte[],...).
            // Count that read once, including when the base implementation delegates.
            var before = BytesRead;
            var read = base.Read(buffer);
            BytesRead = before + read;
            return read;
        }
    }

    private static void CheckTimestamps()
    {
        var time = new DateTimeOffset(2024, 2, 3, 12, 34, 56, TimeSpan.Zero);
        var legacy = LegacyTimestamp(time);
        var rfc = RfcTimestamp(time.AddMilliseconds(123));
        Equal<DateTimeOffset?>(time, AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.CounterSignatureOid, legacy),
            "legacy countersignature signing-time decoded");
        Equal<DateTimeOffset?>(time.AddMilliseconds(123), AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.Rfc3161Oid, rfc),
            "RFC 3161 genTime decoded with fractional seconds");
        Check(AuthenticodeTimestampReader.Read("1.2.840.113549.1.9.5", legacy) is null,
            "self-declared signing time is not accepted as a countersignature");
        foreach (var oid in new[] { AuthenticodeTimestampReader.CounterSignatureOid, AuthenticodeTimestampReader.Rfc3161Oid })
        {
            Check(AuthenticodeTimestampReader.Read(oid, new byte[] { 0x30, 0x82, 0xff }) is null,
                "truncated timestamp is handled / " + oid);
            Check(AuthenticodeTimestampReader.Read(oid, Array.Empty<byte>()) is null,
                "empty timestamp is handled / " + oid);
        }
        Check(AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.CounterSignatureOid,
            LegacyTimestamp(time, duplicate: true)) is null, "ambiguous duplicate signing times are rejected");
        Check(AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.CounterSignatureOid,
            LegacyTimestamp(time, includeTime: false)) is null, "countersignature without signing-time has no invented date");
        Check(AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.Rfc3161Oid, rfc.AsMemory(0, rfc.Length - 10)) is null,
            "truncated RFC 3161 container is rejected");
        var confirmed = AuthenticodeTimestampReader.Select([time.AddDays(-1), time], true, time, true);
        Check(confirmed.Time == time && confirmed.Status == TimestampStatus.Verified,
            "only the timestamp used by a successful Windows check is confirmed");
        Equal(TimestampStatus.Unverified, AuthenticodeTimestampReader.Select([time], true, time, false).Status,
            "decoded time alone is not proof of timestamp trust");
        Equal(TimestampStatus.Unverified, AuthenticodeTimestampReader.Select([time], true, time.AddDays(1), true).Status,
            "Windows verification time cannot replace a different embedded timestamp");
        Equal(TimestampStatus.NotPresent, AuthenticodeTimestampReader.Select([], false, DateTimeOffset.UtcNow, true).Status,
            "current verification time never becomes a missing signing date");
        Equal(TimestampStatus.Unreadable, AuthenticodeTimestampReader.Select([], true, time, true).Status,
            "unreadable and absent timestamps are distinguished");
    }

    private static byte[] LegacyTimestamp(DateTimeOffset time, bool duplicate = false, bool includeTime = true)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        var tag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
        writer.PushSequence();
        writer.WriteInteger(1);
        writer.PushSequence(); writer.PushSequence(); writer.PopSequence(); writer.WriteInteger(1); writer.PopSequence();
        WriteAlgorithm(writer);
        writer.PushSetOf(tag);
        if (includeTime)
            for (var i = 0; i < (duplicate ? 2 : 1); i++)
            {
                writer.PushSequence(); writer.WriteObjectIdentifier("1.2.840.113549.1.9.5");
                writer.PushSetOf(); writer.WriteUtcTime(time); writer.PopSetOf(); writer.PopSequence();
            }
        writer.PopSetOf(tag);
        WriteAlgorithm(writer);
        writer.WriteOctetString([1, 2, 3]);
        writer.PopSequence();
        return writer.Encode();
    }

    private static byte[] RfcTimestamp(DateTimeOffset time)
    {
        var tst = new AsnWriter(AsnEncodingRules.DER);
        tst.PushSequence(); tst.WriteInteger(1); tst.WriteObjectIdentifier("1.2.3");
        tst.PushSequence(); WriteAlgorithm(tst); tst.WriteOctetString(new byte[32]); tst.PopSequence();
        tst.WriteInteger(123); tst.WriteGeneralizedTime(time, omitFractionalSeconds: false); tst.PopSequence();
        var writer = new AsnWriter(AsnEncodingRules.DER);
        var tag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
        writer.PushSequence(); writer.WriteObjectIdentifier("1.2.840.113549.1.7.2");
        writer.PushSequence(tag); writer.PushSequence(); writer.WriteInteger(3);
        writer.PushSetOf(); WriteAlgorithm(writer); writer.PopSetOf();
        writer.PushSequence(); writer.WriteObjectIdentifier("1.2.840.113549.1.9.16.1.4");
        writer.PushSequence(tag); writer.WriteOctetString(tst.Encode()); writer.PopSequence(tag); writer.PopSequence();
        writer.PushSetOf(); writer.PopSetOf();
        writer.PopSequence(); writer.PopSequence(tag); writer.PopSequence();
        return writer.Encode();
    }

    private static void WriteAlgorithm(AsnWriter writer)
    {
        writer.PushSequence(); writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1"); writer.PopSequence();
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        _passed++;
        Console.WriteLine("PASS: " + label);
    }
    private static void Equal<T>(T expected, T actual, string label) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{label} (expected {expected}; actual {actual})");
    private static async Task ExpectCancellation(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { Check(true, "operation cancels"); return; }
        throw new Exception("Expected OperationCanceledException.");
    }

    private sealed class StubSignatures : ISignatureInspector
    {
        public int Calls { get; private set; }
        public bool SawReadableHandle { get; private set; }
        public SignatureResult Inspect(FileStream file, string fullPath, RevocationMode mode, CancellationToken cancellationToken = default)
        {
            Calls++;
            SawReadableHandle = file.CanRead && !file.SafeFileHandle.IsInvalid && file.Position == 0;
            return new SignatureResult(SignatureStatus.NoEmbeddedSignature, mode);
        }
    }

    private sealed class TrackingFactory : IChecksumStrategyFactory
    {
        public int Disposed;
        public IChecksumStrategy Create(ChecksumAlgorithm algorithm) =>
            new TrackedStrategy(new ChecksumStrategyFactory().Create(algorithm), this);
        private sealed class TrackedStrategy(IChecksumStrategy inner, TrackingFactory owner) : IChecksumStrategy
        {
            public ChecksumAlgorithm Algorithm => inner.Algorithm;
            public void Append(ReadOnlySpan<byte> data) => inner.Append(data);
            public string Complete() => inner.Complete();
            public void Dispose() { inner.Dispose(); owner.Disposed++; }
        }
    }

    private sealed class CountingStream(byte[] bytes, Action? afterRead = null) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, false);
        public long BytesRead { get; private set; }
        public int ReadCalls { get; private set; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await _inner.ReadAsync(buffer, cancellationToken);
            ReadCalls++;
            BytesRead += count;
            if (count > 0) afterRead?.Invoke();
            return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
