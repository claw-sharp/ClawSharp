using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed record ClawSharpApplicationFactoryOptions(
    IPermissionPrompter? PermissionPrompter = null);
