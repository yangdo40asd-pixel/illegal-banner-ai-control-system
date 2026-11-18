using System.Text.Json;
using BannerGuardSystem.Helpers;

Console.WriteLine(JsonSerializer.Serialize(new
{
    root = RuntimePaths.Root,
    backend = RuntimePaths.Resolve("BackendEntry"),
    database = RuntimePaths.Resolve("DatabasePath"),
    cctv = RuntimePaths.Resolve("CctvMediaDirectory")
}));
