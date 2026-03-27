using Replica.Api.Infrastructure;

var parseResult = ParseArgs(args);
if (!parseResult.IsSuccess)
{
    Console.Error.WriteLine(parseResult.Error);
    PrintUsage();
    return 1;
}

try
{
    var report = await ReplicaApiReconciliationReportIo.BuildFromFilesAsync(
        parseResult.Options!.PgSnapshotPath,
        parseResult.Options.JsonSnapshotPath);

    await ReplicaApiReconciliationReportIo.WriteReportAsync(
        parseResult.Options.OutputReportPath,
        report);

    Console.WriteLine($"Report written: {parseResult.Options.OutputReportPath}");
    Console.WriteLine($"missing_in_pg={report.Summary.MissingInPg}");
    Console.WriteLine($"missing_in_json={report.Summary.MissingInJson}");
    Console.WriteLine($"version_mismatch={report.Summary.VersionMismatch}");
    Console.WriteLine($"payload_mismatch={report.Summary.PayloadMismatch}");
    Console.WriteLine($"is_zero_diff={report.Summary.IsZeroDiff}");

    // Exit with non-zero when mismatches are present so the tool can be used in CI/CD gates.
    return report.Summary.IsZeroDiff ? 0 : 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Failed to build reconciliation report.");
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static ParseArgsResult ParseArgs(string[] args)
{
    if (args == null || args.Length == 0)
        return ParseArgsResult.Fail("No arguments were provided.");

    string pgPath = string.Empty;
    string jsonPath = string.Empty;
    string outputPath = string.Empty;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (string.IsNullOrWhiteSpace(arg))
            continue;

        if (string.Equals(arg, "--pg", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadValue(args, ref i, out pgPath))
                return ParseArgsResult.Fail("Missing value for --pg.");
            continue;
        }

        if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadValue(args, ref i, out jsonPath))
                return ParseArgsResult.Fail("Missing value for --json.");
            continue;
        }

        if (string.Equals(arg, "--out", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadValue(args, ref i, out outputPath))
                return ParseArgsResult.Fail("Missing value for --out.");
            continue;
        }

        return ParseArgsResult.Fail($"Unknown argument: {arg}");
    }

    if (string.IsNullOrWhiteSpace(pgPath))
        return ParseArgsResult.Fail("--pg is required.");
    if (string.IsNullOrWhiteSpace(jsonPath))
        return ParseArgsResult.Fail("--json is required.");
    if (string.IsNullOrWhiteSpace(outputPath))
        return ParseArgsResult.Fail("--out is required.");

    return ParseArgsResult.Success(new ToolOptions(pgPath, jsonPath, outputPath));
}

static bool TryReadValue(string[] args, ref int index, out string value)
{
    value = string.Empty;
    if (index + 1 >= args.Length)
        return false;

    var candidate = args[index + 1];
    if (string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith("--", StringComparison.Ordinal))
        return false;

    value = candidate;
    index++;
    return true;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/Replica.Reconciliation.Cli -- --pg <pg_snapshot.json> --json <json_snapshot.json> --out <report.json>");
    Console.WriteLine();
    Console.WriteLine("Accepted snapshot shapes:");
    Console.WriteLine("  1) JSON array of orders");
    Console.WriteLine("  2) JSON object with Orders/orders array");
}

internal sealed record ToolOptions(
    string PgSnapshotPath,
    string JsonSnapshotPath,
    string OutputReportPath);

internal sealed class ParseArgsResult
{
    private ParseArgsResult(bool isSuccess, ToolOptions? options, string error)
    {
        IsSuccess = isSuccess;
        Options = options;
        Error = error;
    }

    public bool IsSuccess { get; }
    public ToolOptions? Options { get; }
    public string Error { get; }

    public static ParseArgsResult Success(ToolOptions options)
    {
        return new ParseArgsResult(isSuccess: true, options, error: string.Empty);
    }

    public static ParseArgsResult Fail(string error)
    {
        return new ParseArgsResult(isSuccess: false, options: null, error: error ?? string.Empty);
    }
}
