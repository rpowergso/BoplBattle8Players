using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem.Controls;

namespace MorePlayers;

[HarmonyPatch]
internal static class LobbyPracticeInputPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(SelectColor), "Update");
        yield return AccessTools.Method(typeof(SelectAbility), "Update");
        yield return AccessTools.Method(typeof(CharacterSelectBox), "LateUpdate");
        yield return AccessTools.Method(typeof(CharacterSelectHandler_online), "Update");
    }

    // Filter input at the menu reads. Pointer clicks, rebinding, and the
    // lobby's ready broadcasts continue through their original paths.
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var pressed = AccessTools.PropertyGetter(typeof(ButtonControl), nameof(ButtonControl.wasPressedThisFrame));
        var menuPressed = AccessTools.Method(typeof(LobbyPracticeInputPatch), nameof(MenuPressed));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(pressed))
            {
                var loadMenu = new CodeInstruction(OpCodes.Ldarg_0);
                loadMenu.labels.AddRange(instruction.labels);
                loadMenu.blocks.AddRange(instruction.blocks);
                yield return loadMenu;
                yield return new CodeInstruction(OpCodes.Call, menuPressed);
            }
            else
                yield return instruction;
        }
    }

    private static bool MenuPressed(ButtonControl button, MonoBehaviour menu)
    {
        if (!button.wasPressedThisFrame) return false;
        var selector = menu switch
        {
            CharacterSelectBox box => box,
            SelectColor color => color.csb,
            SelectAbility ability => ability.csb,
            CharacterSelectHandler_online handler => handler.characterSelectBox,
            _ => null,
        };
        var motion = LobbyMotion.ForSelector(selector);
        return motion == null || !motion.ClaimsMenuButton(button);
    }
}
