namespace ClawSharp.Core;

public static class CliCommandText
{
    private static readonly string[] TopLevelCompletions =
    [
        "repl",
        "remote-control",
        "tasks",
        "completion",
        "update",
        "--provider",
        "--model",
        "help",
        "--help",
        "-h",
        "--version",
        "-v",
        "-V"
    ];

    private static readonly string[] ReplCompletions =
    [
        "--continue",
        "-c",
        "--resume",
        "-r",
        "--name",
        "-n",
        "--provider",
        "--model",
        "--help",
        "-h"
    ];

    private static readonly string[] RemoteControlCompletions =
    [
        "--name",
        "--continue",
        "-c",
        "--session-id",
        "--permission-mode",
        "--spawn",
        "--capacity",
        "--create-session-in-dir",
        "--no-create-session-in-dir",
        "--debug-file",
        "--session-timeout",
        "--verbose",
        "-v",
        "--help",
        "-h"
    ];

    private static readonly string[] CompletionShells =
    [
        "bash",
        "zsh",
        "powershell"
    ];

    public static string GetTopLevelHelpText()
    {
        return $$"""
Usage:
  {{AppMetadata.CommandName}}
  {{AppMetadata.CommandName}} repl
  {{AppMetadata.CommandName}} repl --continue
  {{AppMetadata.CommandName}} repl --resume <session-id>
  {{AppMetadata.CommandName}} repl --name <name>
  {{AppMetadata.CommandName}} --provider <provider> [--model <model>]
  {{AppMetadata.CommandName}} remote-control [options]
  {{AppMetadata.CommandName}} tasks
  {{AppMetadata.CommandName}} completion <bash|zsh|powershell>
  {{AppMetadata.CommandName}} update
  {{AppMetadata.CommandName}} --version
  {{AppMetadata.CommandName}} --help
""";
    }

    public static string GetUpdateText()
    {
        return $$"""
ClawSharp is distributed through npm.

Install:
  {{AppMetadata.InstallCommand}}

Update:
  {{AppMetadata.UpdateInstallCommand}}

Reinstall the latest release:
  {{AppMetadata.LatestInstallCommand}}
""";
    }

    public static bool TryGetCompletionScript(string shell, out string script)
    {
        var normalized = shell.Trim().ToLowerInvariant();
        script = normalized switch
        {
            "bash" => GetBashCompletionScript(),
            "zsh" => GetZshCompletionScript(),
            "powershell" or "pwsh" => GetPowerShellCompletionScript(),
            _ => string.Empty
        };

        return script.Length > 0;
    }

    public static string GetCompletionUsageText()
    {
        return $"Usage:{Environment.NewLine}  {AppMetadata.CommandName} completion <bash|zsh|powershell>";
    }

    private static string GetBashCompletionScript()
    {
        return $$"""
#!/usr/bin/env bash
_{{AppMetadata.CommandName}}_completions() {
  local cur prev command
  cur="${COMP_WORDS[COMP_CWORD]}"
  prev=""
  if [[ $COMP_CWORD -gt 0 ]]; then
    prev="${COMP_WORDS[COMP_CWORD-1]}"
  fi
  command=""
  if [[ ${#COMP_WORDS[@]} -gt 1 ]]; then
    command="${COMP_WORDS[1]}"
  fi

  if [[ $COMP_CWORD -eq 1 ]]; then
    COMPREPLY=( $(compgen -W "{{string.Join(" ", TopLevelCompletions)}}" -- "$cur") )
    return 0
  fi

  case "$command" in
    completion)
      COMPREPLY=( $(compgen -W "{{string.Join(" ", CompletionShells)}}" -- "$cur") )
      return 0
      ;;
    repl)
      COMPREPLY=( $(compgen -W "{{string.Join(" ", ReplCompletions)}}" -- "$cur") )
      return 0
      ;;
    remote-control|rc|remote|sync|bridge)
      COMPREPLY=( $(compgen -W "{{string.Join(" ", RemoteControlCompletions)}}" -- "$cur") )
      return 0
      ;;
  esac
}

complete -F _{{AppMetadata.CommandName}}_completions {{AppMetadata.CommandName}}
""";
    }

    private static string GetZshCompletionScript()
    {
        return $$"""
#compdef {{AppMetadata.CommandName}}

local -a top_level
local -a repl_args
local -a remote_args
local -a shells
top_level=({{string.Join(" ", TopLevelCompletions)}})
repl_args=({{string.Join(" ", ReplCompletions)}})
remote_args=({{string.Join(" ", RemoteControlCompletions)}})
shells=({{string.Join(" ", CompletionShells)}})

if (( CURRENT == 2 )); then
  _describe 'command' top_level
  return
fi

case "${words[2]}" in
  completion)
    _describe 'shell' shells
    ;;
  repl)
    _describe 'repl argument' repl_args
    ;;
  remote-control|rc|remote|sync|bridge)
    _describe 'remote-control argument' remote_args
    ;;
esac
""";
    }

    private static string GetPowerShellCompletionScript()
    {
        return $$"""
Register-ArgumentCompleter -Native -CommandName {{AppMetadata.CommandName}} -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    $commandElements = @($commandAst.CommandElements | ForEach-Object { $_.Value })
    $topLevel = @({{string.Join(", ", TopLevelCompletions.Select(static value => $"'{value}'"))}})
    $replArgs = @({{string.Join(", ", ReplCompletions.Select(static value => $"'{value}'"))}})
    $remoteArgs = @({{string.Join(", ", RemoteControlCompletions.Select(static value => $"'{value}'"))}})
    $shells = @({{string.Join(", ", CompletionShells.Select(static value => $"'{value}'"))}})

    if ($commandElements.Count -le 2) {
        $topLevel | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
        }
        return
    }

    switch ($commandElements[1]) {
        'completion' {
            $shells | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
            }
        }
        'repl' {
            $replArgs | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
            }
        }
        'remote-control' {
            $remoteArgs | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
            }
        }
    }
}
""";
    }
}
