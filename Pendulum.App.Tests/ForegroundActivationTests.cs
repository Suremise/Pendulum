using System.Reflection;
using System.Runtime.InteropServices;
using Pendulum.App.Services;

namespace Pendulum.App.Tests;

/// Inspect the imports without calling them: a check must never take the user's foreground.
public sealed class ForegroundActivationTests
{
    [Fact]
    public void Foreground_requests_cannot_join_another_apps_input_queue()
    {
        var imports = typeof(Win32Interop).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<DllImportAttribute>())
            .OfType<DllImportAttribute>();

        Assert.DoesNotContain(imports, import => import.EntryPoint == "AttachThreadInput");
    }
}
