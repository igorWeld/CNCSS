using CNCSS.Vis;
using Xunit;

namespace CNCSS.Tests;

public sealed class OcctNativeLoaderTests
{
    [Fact]
    public void EnsureInitialized_LoadsOcctFromNativeFolder()
    {
        string nativeDir = Path.Combine(AppContext.BaseDirectory, "occt", "x64");
        if (!Directory.Exists(nativeDir))
        {
            return;
        }

        bool ok = OcctNativeLoader.EnsureInitialized();
        Assert.True(ok, OcctNativeLoader.InitializationError ?? "Occt.NET failed to initialize.");
    }
}
