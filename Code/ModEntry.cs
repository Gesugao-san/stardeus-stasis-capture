// Stasis Capture — Stardeus v0.15 mod.
// Lets colonists carry any living, biological, incapacitated being (raiders,
// animals, crawlers, own downed colonists) into a Stasis Array. Select the being
// and press "Send to Stasis" on the bottom bar, or use the "Incapacitated"
// section of a Stasis Array's "Send to Stasis" panel. Sleepers keep their faction:
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
      var harmony = new Harmony(HarmonyId);
      // Without this patch only orders that were pending in a save are lost on load
      ApplyPatch("saved orders", () => harmony.Patch(
        AccessTools.Method(typeof(StorableBeingComp), nameof(StorableBeingComp.OnGoalLoad)),
        postfix: new HarmonyMethod(typeof(ModEntry), nameof(AfterGoalLoad))));
      // Without this patch the bottom bar button is the only way to give the order
      ApplyPatch("Stasis Array panel section", () => StasisPanelSection.Apply(harmony));
      // Without this patch captured beings look like colonists on the wake-up screen
      ApplyPatch("wake-up card style", () => WakeUpCardStyle.Apply(harmony));
      D.Warn("[StasisCapture] Loaded");
    }

    /// <summary>Runs one setup step; a failure disables only that feature.</summary>
    private static void ApplyPatch(string feature, Action apply) {
      try {
        apply();
      } catch (Exception e) {
        D.Err("[StasisCapture] Setup of " + feature + " failed: " + e);
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
