using System.Text.Json;
using HNAUnlocker.Core;

internal static class GameClientPathsVerification
{
    public static void Run(Action<bool, string> check)
    {
        check(GameClientPaths.Normalize(new[]
        {
            @" D:/custom folder/./NexusAnima.exe ",
            @"d:\CUSTOM folder\NexusAnima.exe",
            "E:\\\u81ea\u5b9a\u4e49\\\u6e38\u620f\\NexusAnima.exe",
            @"F:\manual\old\..\NexusAnima.exe"
        }).SequenceEqual(new[]
        {
            @"D:\custom folder\NexusAnima.exe",
            "E:\\\u81ea\u5b9a\u4e49\\\u6e38\u620f\\NexusAnima.exe",
            @"F:\manual\NexusAnima.exe"
        }), "remembered clients normalize local paths, preserve Unicode and keep first case-insensitive duplicates");

        foreach (string invalid in new[]
        {
            null, "", "  ", @"NexusAnima.exe", @"games\NexusAnima.exe", @"C:NexusAnima.exe",
            @"\games\NexusAnima.exe", @"\\server\games\NexusAnima.exe",
            @"\\?\C:\games\NexusAnima.exe", @"\\.\C:\games\NexusAnima.exe",
            @"https://example/NexusAnima.exe", @"C:\games\NexusAnima.exe:stream",
            @"C:\games:stream\NexusAnima.exe", @"C:\bad|folder\NexusAnima.exe",
            @"C:\bad*folder\NexusAnima.exe", @"C:\bad?folder\NexusAnima.exe",
            "C:\\bad\0folder\\NexusAnima.exe", @"C:\games\Other.exe",
            @"C:\games\NexusAnima.exe.bak", @"C:\games\NexusAnima.exe\",
            "C:\\" + new string('x', 32768) + "\\NexusAnima.exe"
        })
        {
            check(GameClientPaths.Normalize(new[] { invalid }).Length == 0,
                "remembered clients reject malformed, nonlocal or wrong-executable paths: " +
                (invalid == null ? "(null)" : invalid.Length > 100 ? "(oversized)" :
                    invalid.Any(char.IsControl) ? "(control character)" : invalid));
        }
        check(GameClientPaths.Normalize(null).Length == 0,
            "absent remembered client collections normalize to an empty list");

        string[] normalizedLimit = GameClientPaths.Normalize(
            Enumerable.Range(0, 200).Select(index => $@"D:\client{index}\NexusAnima.exe"));
        check(normalizedLimit.Length == 64 && normalizedLimit[0] == @"D:\client0\NexusAnima.exe" &&
              normalizedLimit[^1] == @"D:\client63\NexusAnima.exe",
            "remembered client limits preserve the earliest 64 eligible paths");

        int normalizationReads = 0;
        string[] bounded = GameClientPaths.Normalize(Enumerable.Range(0, 8192).Select(_ =>
        {
            normalizationReads++;
            return (string)null;
        }));
        check(bounded.Length == 0 && normalizationReads > 0 && normalizationReads < 8192,
            "normalization bounds source enumeration even when all entries are invalid");

        string current = @"E:\selected\NexusAnima.exe";
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            current, @"D:\manual\NexusAnima.exe", @"F:\discovered\NexusAnima.exe"
        };
        check(GameClientPaths.Available(current,
            new[] { @"D:/manual/NexusAnima.exe", @"e:\SELECTED\NexusAnima.exe", @"C:\deleted\NexusAnima.exe" },
            new[] { @"d:\MANUAL\NexusAnima.exe", @"F:\discovered\NexusAnima.exe" },
            existing.Contains).SequenceEqual(new[]
            {
                current, @"D:\manual\NexusAnima.exe", @"F:\discovered\NexusAnima.exe"
            }), "available clients keep the selected client first, then remembered manual and newly discovered clients");

        check(GameClientPaths.Available(@"C:\missing\NexusAnima.exe",
            new[] { @"D:\denied\NexusAnima.exe", @"relative\NexusAnima.exe", @"D:\valid\Other.exe" },
            new[] { @"F:\working\NexusAnima.exe" },
            path => path == @"D:\denied\NexusAnima.exe" ? throw new IOException() :
                path == @"F:\working\NexusAnima.exe").SequenceEqual(new[] { @"F:\working\NexusAnima.exe" }),
            "missing, inaccessible and ineligible clients do not hide an available installation");
        check(GameClientPaths.Available(null, null, null, _ => throw new IOException()).Length == 0,
            "absent client sources require no filesystem probes");

        string[] remembered = GameClientPaths.Normalize(
            new[] { @"D:\manual\NexusAnima.exe", @"F:\discovered\NexusAnima.exe" });
        check(GameClientPaths.Available(null, remembered, Array.Empty<string>(), existing.Contains)
                .SequenceEqual(new[] { @"D:\manual\NexusAnima.exe", @"F:\discovered\NexusAnima.exe" }),
            "a later empty rescan retains remembered manual and discovered clients that still exist");

        string[] availableLimit = GameClientPaths.Available(null,
            Enumerable.Range(0, 64).Select(index => $@"C:\missing{index}\NexusAnima.exe"),
            Enumerable.Range(0, 200).Select(index => $@"E:\found{index}\NexusAnima.exe"),
            path => path.StartsWith(@"E:\", StringComparison.Ordinal));
        check(availableLimit.Length == 64 && availableLimit[0] == @"E:\found0\NexusAnima.exe" &&
              availableLimit[^1] == @"E:\found63\NexusAnima.exe",
            "unavailable history does not consume the 64 available-client slots");

        int availabilityReads = 0;
        string[] boundedAvailable = GameClientPaths.Available(null,
            Enumerable.Range(0, 8192).Select(index =>
            {
                availabilityReads++;
                return $@"C:\missing{index}\NexusAnima.exe";
            }), new[] { @"E:\found\NexusAnima.exe" },
            path => path == @"E:\found\NexusAnima.exe");
        check(boundedAvailable.SequenceEqual(new[] { @"E:\found\NexusAnima.exe" }) &&
              availabilityReads > 0 && availabilityReads < 8192,
            "available-client enumeration is bounded per source while discovery remains reachable");

        var legacy = JsonSerializer.Deserialize<UnlockConfig>(
            """{"ProcessPath":" D:\\old client\\NexusAnima.exe ","TargetFramerate":165,"FovDelta":8.5,"UseMobileUI":true,"Language":"ja-JP"}""");
        legacy.Normalize();
        check(legacy.ProcessPath == @"D:\old client\NexusAnima.exe" &&
              legacy.GamePaths.SequenceEqual(new[] { @"D:\old client\NexusAnima.exe" }) &&
              legacy.TargetFramerate == 165 && legacy.FovDelta == 8.5 &&
              legacy.UseMobileUI && legacy.Language == "ja-JP",
            "legacy settings seed remembered clients from the selected path without changing other preferences");

        var absent = JsonSerializer.Deserialize<UnlockConfig>("""{"ProcessPath":null,"GamePaths":null}""");
        absent.Normalize();
        check(absent.ProcessPath == "" && absent.GamePaths.Length == 0,
            "null JSON client fields normalize without filesystem access");

        var selected = new UnlockConfig
        {
            ProcessPath = @" E:\selected\NexusAnima.exe ",
            GamePaths = new[]
            {
                @"D:\manual\NexusAnima.exe", @"e:\SELECTED\NexusAnima.exe", null, @"relative\NexusAnima.exe"
            }
        };
        selected.Normalize();
        check(selected.GamePaths.SequenceEqual(new[] { current, @"D:\manual\NexusAnima.exe" }),
            "settings normalization keeps the current selection first and removes malformed remembered paths");

        var noSelection = new UnlockConfig
        {
            ProcessPath = " ",
            GamePaths = new[] { @"D:\manual\NexusAnima.exe" }
        };
        noSelection.Normalize();
        check(noSelection.ProcessPath == "" &&
              noSelection.GamePaths.SequenceEqual(new[] { @"D:\manual\NexusAnima.exe" }),
            "remembered clients do not automatically replace an empty selection");

        var deletedSelection = new UnlockConfig
        {
            ProcessPath = @"C:\deleted\NexusAnima.exe",
            GamePaths = new[] { @"D:\manual\NexusAnima.exe" }
        };
        deletedSelection.Normalize();
        string[] withoutDeleted = GameClientPaths.Available(deletedSelection.ProcessPath,
            deletedSelection.GamePaths, Array.Empty<string>(), existing.Contains);
        check(deletedSelection.ProcessPath == @"C:\deleted\NexusAnima.exe" &&
              deletedSelection.GamePaths.Contains(@"C:\deleted\NexusAnima.exe") &&
              withoutDeleted.SequenceEqual(new[] { @"D:\manual\NexusAnima.exe" }),
            "deleted clients remain structural history while availability does not replace the selected path");

        var snapshot = selected.Clone();
        check(snapshot.GamePaths.SequenceEqual(new[] { current, @"D:\manual\NexusAnima.exe" }),
            "config snapshots retain the remembered client list");
        snapshot.GamePaths[0] = @"G:\snapshot\NexusAnima.exe";
        selected.GamePaths[1] = @"H:\original\NexusAnima.exe";
        check(selected.GamePaths[0] == current && snapshot.GamePaths[1] == @"D:\manual\NexusAnima.exe",
            "config snapshots keep independent remembered-client arrays");

        var restored = JsonSerializer.Deserialize<UnlockConfig>(JsonSerializer.Serialize(selected));
        restored.Normalize();
        check(restored.ProcessPath == current &&
              restored.GamePaths.SequenceEqual(new[] { current, @"H:\original\NexusAnima.exe" }),
            "selected and remembered clients survive a JSON roundtrip without live settings IO");
    }
}
