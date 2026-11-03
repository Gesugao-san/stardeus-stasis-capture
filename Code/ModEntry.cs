// Stasis Capture — Stardeus v0.15 mod.
// Lets colonists carry any living, biological, incapacitated being (raiders,
// animals, crawlers, own downed colonists) into a Stasis Array. Select the being
// and press "Send to Stasis" on the bottom bar. Sleepers keep their faction:
// a hostile being wakes up hostile, as the game does for any known sleeper.

using System;
using Game.Components;
using Game.Systems.AI;
using HarmonyLib;
using KL.Utils;
using UnityEngine;

namespace StasisCapture {
  /// <summary>
  /// Mod entry point: the mod loader invokes non-public static methods marked with
  /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/> right after loading the DLL.
  /// The AI action and the bottom bar action register themselves via [SelfInit].
  /// </summary>
  internal static class ModEntry {
    private const string HarmonyId = "gesu.stasiscapture";

    [RuntimeInitializeOnLoadMethod]
    private static void Init() {
      try {
        var harmony = new Harmony(HarmonyId);
        harmony.Patch(AccessTools.Method(typeof(StorableBeingComp), nameof(StorableBeingComp.OnGoalLoad)),
          postfix: new HarmonyMethod(typeof(ModEntry), nameof(AfterGoalLoad)));
        D.Warn("[StasisCapture] Loaded");
      } catch (Exception e) {
        // Without the patch only orders that were pending in a save are lost on load
        D.Err("[StasisCapture] Patching StorableBeingComp.OnGoalLoad failed: " + e);
      }
    }

    /// <summary>
    /// On load the game gives every goal on a being the "store being" check, which
    /// fails for a living being. Our goals get their own check back.
    /// </summary>
    private static void AfterGoalLoad(AIGoal goal) {
      if (!CaptureRules.IsOurGoal(goal)) { return; }
      goal.CheckDoable = CaptureOrder.Check;
    }
  }
}
