using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public enum ClawSharpApplicationInitializationMode
{
    Eager,
    LazyRuntime
}

public sealed record ClawSharpApplicationFactoryOptions(
    IPermissionPrompter? PermissionPrompter = null,
    ClawSharpApplicationInitializationMode InitializationMode = ClawSharpApplicationInitializationMode.Eager);
