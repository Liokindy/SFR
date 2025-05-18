using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using SFD;
using SFD.MenuControls;
using SFD.SFDOnlineServices;
using SFR.Misc;

namespace SFR.OnlineServices;

/// <summary>
/// Handles in-game browser and server join requests.
/// </summary>
[HarmonyPatch]
internal static class Browser
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameBrowserMenuItem), nameof(GameBrowserMenuItem.Game), MethodType.Setter)]
    private static bool PatchBrowser(SFDGameServerInstance value, GameBrowserMenuItem __instance)
    {
        if (__instance.m_game != value)
        {
            __instance.m_game = value;
            if (__instance.labels is not null)
            {
                var color = Color.Red;
                if (__instance.m_game is { SFDGameServer: not null })
                {
                    if (__instance.m_game.SFDGameServer.Version == Globals.ServerVersion)
                    {
                        color = Color.White;
                    }
                    else if (__instance.m_game.SFDGameServer.Version == Globals.VanillaVersion)
                    {
                        color = Color.Yellow;
                    }
                }

                foreach (var label in __instance.labels)
                {
                    label.Color = color;
                }
            }
        }

        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SFDGameServer), nameof(SFDGameServer.Version), MethodType.Setter)]
    private static bool GameServerVersion(ref string value)
    {
        if (value == Globals.VanillaVersion)
        {
            value = Globals.ServerVersion;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Constants), nameof(Constants.VersionCheckDifference), typeof(string))]
    private static bool VersionCheckPatch(string versionToCheck, ref VersionDifference __result)
    {
        __result = versionToCheck == Globals.ServerVersion ? VersionDifference.Same : VersionDifference.Older;
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetMessage.Connection.DiscoveryResponse.Data), MethodType.Constructor, new Type[6] { typeof(ServerResponse), typeof(string), typeof(bool), typeof(string), typeof(string), typeof(Guid) })]
    private static void PatchServerVersionResponse(ref NetMessage.Connection.DiscoveryResponse.Data __instance)
    {
        if (__instance.Version == Globals.VanillaVersion)
        {
            __instance.Version = Globals.ServerVersion;
        }
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(Server), nameof(Server.DoReadRun))]
    private static IEnumerable<CodeInstruction> ServerReadRun(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is null)
            {
                continue;
            }

            if (instruction.operand.Equals(Globals.VanillaVersion))
            {
                instruction.operand = Globals.ServerVersion;
            }
        }

        return instructions;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetMessage.Connection.DiscoveryRequest.Data), MethodType.Constructor, typeof(Guid), typeof(int), typeof(bool), typeof(string), typeof(string))]
    private static void PatchServerVersionRequest(ref NetMessage.Connection.DiscoveryRequest.Data __instance)
    {
        if (__instance.Version == Globals.VanillaVersion)
        {
            __instance.Version = Globals.ServerVersion;
        }
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(NetMessage.Connection.DiscoveryResponse), nameof(NetMessage.Connection.DiscoveryResponse.Read))]
    private static IEnumerable<CodeInstruction> PatchServerVersion(IEnumerable<CodeInstruction> instructions)
    {
        instructions.ElementAt(49).operand = Globals.ServerVersion;
        return instructions;
    }
}