using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;

namespace DungeonDrip;

/// <summary>
/// Claims chat commands, skipping any another plugin already owns.
/// </summary>
/// <remarks>
/// Dalamud refuses a second registration of an existing command and the failure is silent, so a
/// short alias like /drip going missing would look like a bug in this plugin rather than a clash
/// with whatever else the user has installed. Short aliases are attempted in order of preference and
/// the ones that land are reported, so what is actually available is never a guess. The long form is
/// unlikely to collide and is treated as required.
/// </remarks>
public sealed class CommandRegistration : IDisposable
{
    private readonly List<string> registered = [];

    /// <summary>The commands that were actually claimed, best first.</summary>
    public IReadOnlyList<string> Registered => registered;

    public CommandRegistration(string primary, IEnumerable<string> aliases, IReadOnlyCommandInfo.HandlerDelegate handler)
    {
        Claim(primary, new CommandInfo(handler)
        {
            HelpMessage = "Show dungeon gear you are missing. "
                        + $"\"{primary} <duty name>\" looks up any duty and \"<gear name>\" any piece, "
                        + $"\"{primary} item <name>\" forces the latter, \"config\" opens settings, "
                        + "\"refresh\" re-reads your dresser, \"update\" re-downloads the loot data.",
        });

        foreach (var alias in aliases)
            Claim(alias, new CommandInfo(handler) { HelpMessage = $"Alias for {primary}." });

        if (registered.Count == 0)
            Plugin.Log.Error("No chat command could be registered; every candidate was already taken.");
        else
            Plugin.Log.Information($"Commands registered: {string.Join(", ", registered)}");
    }

    public void Dispose()
    {
        foreach (var command in registered)
            Plugin.CommandManager.RemoveHandler(command);

        registered.Clear();
    }

    private void Claim(string command, CommandInfo info)
    {
        if (Plugin.CommandManager.Commands.ContainsKey(command))
        {
            Plugin.Log.Information($"Command {command} is already taken by another plugin; skipping it.");
            return;
        }

        if (Plugin.CommandManager.AddHandler(command, info))
            registered.Add(command);
        else
            Plugin.Log.Warning($"Command {command} could not be registered.");
    }
}
