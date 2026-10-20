using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

// Runner xUnit officiel dans le même processus : utile lorsque l'IPC de VSTest est indisponible.
// La CI conserve VSTest et ses rapports TRX comme contrôle principal.
if (args.Length < 1) throw new ArgumentException("Usage : InProcessTests <test.dll> [résultat.json]");
var testAssembly = Path.GetFullPath(args[0]);
var resolver = new AssemblyDependencyResolver(testAssembly);
var testDirectory = Path.GetDirectoryName(testAssembly)!;
foreach (var file in Directory.EnumerateFiles(testDirectory, "*.deps.json"))
    File.Copy(file, Path.Combine(AppContext.BaseDirectory, Path.GetFileName(file)), true);
var manifest = Path.Combine(testDirectory, "MvcTestingAppManifest.json");
if (File.Exists(manifest)) File.Copy(manifest, Path.Combine(AppContext.BaseDirectory, "MvcTestingAppManifest.json"), true);
AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
{
    var path = resolver.ResolveUnmanagedDllToPath(name)
        ?? Path.Combine(testDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", name + ".dll");
    return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
};
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = resolver.ResolveAssemblyToPath(name);
    return path is null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
};
using var sink = new ResultsSink();
using var controller = new XunitFrontController(AppDomainSupport.Denied, testAssembly, diagnosticMessageSink: sink);
var execution = TestFrameworkOptions.ForExecution();
execution.SetDisableParallelization(true);
controller.RunAll(sink, TestFrameworkOptions.ForDiscovery(), execution);
if (!sink.Finished.Wait(TimeSpan.FromMinutes(15))) throw new TimeoutException("xUnit n'a pas terminé.");
var report = new { sink.Total, sink.Failed, sink.Skipped, sink.Errors, sink.Failures };
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
if (args.Length > 1) File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return sink.Failed + sink.Errors > 0 || sink.Total == 0 ? 1 : 0;

sealed class ResultsSink : IMessageSink, IDisposable
{
    public ManualResetEventSlim Finished { get; } = new();
    public int Total { get; private set; }
    public int Failed { get; private set; }
    public int Skipped { get; private set; }
    public int Errors { get; private set; }
    public List<object> Failures { get; } = [];
    public bool OnMessage(IMessageSinkMessage message)
    {
        if (message is ITestFailed failure)
        {
            var entry = new { Test = failure.Test.DisplayName, failure.Messages, failure.StackTraces };
            Failures.Add(entry); Console.WriteLine(JsonSerializer.Serialize(entry));
        }
        if (message is IErrorMessage error)
        {
            Errors++; Console.WriteLine(string.Join("\n", error.Messages));
        }
        if (message is ITestAssemblyFinished done)
        {
            Total = done.TestsRun; Failed = done.TestsFailed; Skipped = done.TestsSkipped; Finished.Set();
        }
        return true;
    }
    public void Dispose() => Finished.Dispose();
}
