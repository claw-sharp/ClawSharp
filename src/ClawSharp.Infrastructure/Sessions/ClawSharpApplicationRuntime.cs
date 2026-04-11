using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.Infrastructure;

public sealed class ClawSharpApplicationRuntime
{
    public ClawSharpApplicationRuntime(
        CommandRegistry commands,
        ToolRegistry tools,
        TaskRegistry tasks,
        QueryEngine queryEngine,
        TerminalShell terminalShell,
        IQueryModelTurnContextProvider? modelTurnContextProvider = null)
    {
        Commands = commands;
        Tools = tools;
        Tasks = tasks;
        QueryEngine = queryEngine;
        TerminalShell = terminalShell;
        ModelTurnContextProvider = modelTurnContextProvider;
    }

    public CommandRegistry Commands { get; }

    public ToolRegistry Tools { get; }

    public TaskRegistry Tasks { get; }

    public QueryEngine QueryEngine { get; }

    public TerminalShell TerminalShell { get; }

    public IQueryModelTurnContextProvider? ModelTurnContextProvider { get; }
}
