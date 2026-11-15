using System.Diagnostics;

namespace AgfAdmin.Tests.Scripts;

// scripts/demo-reset.sh maakt altijd een NIEUWE database en verwijdert of overschrijft nooit iets: een naam die niet met agf_demo begint,
// een vreemde naam of een bestaande database wordt geweigerd vóór er iets verandert. Docker is hier nagebootst (een script dat alleen
// opschrijft hoe het werd aangeroepen); er draait geen echte container of database.
public class DemoResetScriptTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("agf-demo-reset-").FullName;
    private string Log => Path.Combine(_work, "docker.log");
    private string EnvFile => Path.Combine(_work, ".env");

    public DemoResetScriptTests()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_work, "bin")).FullName;
        // Antwoordt op de vragen van het script: bestaat de database, hoeveel migraties; het vulscript slaagt of faalt (FAKE_SEED_EXIT).
        File.WriteAllText(Path.Combine(bin, "docker"),
            "#!/usr/bin/env bash\necho \"$*\" >> \"$FAKE_DOCKER_LOG\"\ncase \"$*\" in\n"
            + "  *'from pg_database where datname'*) echo \"${FAKE_EXISTS:-0}\" ;;\n"
            + "  *'__EFMigrationsHistory'*) echo \"${FAKE_MIGRATIONS:-0}\" ;;\n"
            + "  *demo-seed.sh*) exit \"${FAKE_SEED_EXIT:-0}\" ;;\nesac\nexit 0\n");
        File.SetUnixFileMode(Path.Combine(bin, "docker"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(EnvFile, "POSTGRES_USER=agf\nPOSTGRES_PASSWORD=geheim\nAGF_DEMO_DATABASE=agf_demo_20261101\n");
    }

    public void Dispose() => Directory.Delete(_work, true);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Software-AGF.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("De hoofdmap van de repo is niet gevonden.");
    }

    private static string Migrations() => Directory.GetFiles(Path.Combine(RepoRoot(), "AgfAdmin", "Migrations"), "*.cs")
        .Count(f => !f.EndsWith(".Designer.cs") && !f.EndsWith("ModelSnapshot.cs")).ToString();

    private (int Exit, string Error) Run(string envFile, string exists, params string[] args) => RunSeeding(envFile, exists, "0", args);

    private (int Exit, string Error) RunSeeding(string envFile, string exists, string seedExit, params string[] args)
    {
        var start = new ProcessStartInfo("bash") { RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(Path.Combine(RepoRoot(), "scripts", "demo-reset.sh"));
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["PATH"] = Path.Combine(_work, "bin") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["AGF_DEMO_ENV_FILE"] = envFile;
        start.Environment["FAKE_DOCKER_LOG"] = Log;
        start.Environment["FAKE_EXISTS"] = exists;
        start.Environment["FAKE_MIGRATIONS"] = Migrations();
        start.Environment["FAKE_SEED_EXIT"] = seedExit;
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, error.Result);
    }

    private string DockerCalls => File.Exists(Log) ? File.ReadAllText(Log) : "";

    [Fact]
    public void A_name_that_does_not_start_with_agf_demo_or_contains_other_characters_is_refused_before_docker_is_called()
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var name in new[] { "agf_smoke", "AGF_DEMO_X", "agf_demo_x'; drop database agf_smoke; --", "demo_agf_demo", "agf_demo-x", "agf_demo_" + new string('x', 60) })
        {
            var (exit, error) = Run(EnvFile, "0", name);
            Assert.Equal(2, exit);
            Assert.Contains("begint niet met agf_demo", error);
        }
        Assert.Equal("", DockerCalls);
        Assert.Equal(2, Run(EnvFile, "0", "agf_demo_a", "agf_demo_b").Exit);
        Assert.Equal(2, Run(EnvFile, "0", "--wis").Exit);
        Assert.Equal("", DockerCalls);
    }

    [Fact]
    public void An_existing_database_is_refused_and_nothing_is_created_or_changed()
    {
        if (OperatingSystem.IsWindows()) return;
        var before = File.ReadAllText(EnvFile);
        var (exit, error) = Run(EnvFile, "1", "agf_demo_20261115");
        Assert.Equal(2, exit);
        Assert.Contains("bestaat al", error);
        Assert.Contains("datname = 'agf_demo_20261115'", DockerCalls);
        Assert.DoesNotContain("createdb", DockerCalls);
        Assert.DoesNotContain(" up -d\n", DockerCalls.Replace(" up -d db", ""));
        Assert.Equal(before, File.ReadAllText(EnvFile));
        Assert.False(File.Exists(EnvFile + ".vorige"));
    }

    [Fact]
    public void A_reset_makes_a_new_database_switches_the_settings_keeps_the_old_one_and_fills_it()
    {
        if (OperatingSystem.IsWindows()) return;
        var (exit, error) = Run(EnvFile, "0", "agf_demo_20261115");
        Assert.True(exit == 0, error);
        var calls = DockerCalls;
        Assert.True(calls.IndexOf("build api web", StringComparison.Ordinal) < calls.IndexOf("createdb", StringComparison.Ordinal), "eerst bouwen, dan pas iets veranderen");
        Assert.Contains("createdb -U agf -T template0 agf_demo_20261115", calls);
        Assert.Contains("/app/scripts/demo-seed.sh", calls);
        Assert.DoesNotContain("dropdb", calls);
        Assert.DoesNotContain("DROP", calls, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AGF_DEMO_DATABASE=agf_demo_20261115", File.ReadAllText(EnvFile));
        Assert.Contains("AGF_DEMO_DATABASE=agf_demo_20261101", File.ReadAllText(EnvFile + ".vorige"));
    }

    [Fact]
    public void When_filling_fails_the_script_stops_and_says_how_to_go_back()
    {
        if (OperatingSystem.IsWindows()) return;
        var (exit, error) = RunSeeding(EnvFile, "0", "1", "agf_demo_20261115");
        Assert.Equal(1, exit);
        Assert.Contains("half gevulde agf_demo_20261115", error);
        Assert.Contains("AGF_DEMO_DATABASE=agf_demo_20261101", error);
    }

    [Fact]
    public void Without_the_settings_file_nothing_happens()
    {
        if (OperatingSystem.IsWindows()) return;
        var (exit, error) = Run(Path.Combine(_work, "bestaat-niet.env"), "0", "agf_demo_20261115");
        Assert.Equal(2, exit);
        Assert.Contains("bestaat niet", error);
        Assert.Equal("", DockerCalls);
    }
}
