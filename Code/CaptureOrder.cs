// Stasis Capture — the player's order: a goal on the incapacitated being that colonists pick up.

using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Utils;
using KL.Utils;

namespace StasisCapture {
  /// <summary>
  /// Creates and checks the "send to stasis" goal. It is built like the game's own
  /// "store being" goal (StorableBeingComp.TryOrderStoring): the goal lives on the
  /// being, targets its <see cref="StorableBeingComp"/>, and colonists with the
  /// carrying abilities take it as work.
  /// </summary>
  internal static class CaptureOrder {
    /// <summary>Job type of the game's "store being" goal, so the same colonists do it.</summary>
    private const int JobTypeH = 105668070;
    /// <summary>Abilities the game requires for carrying a being to storage.</summary>
    public const int AbilityA = 961424176;
    public const int AbilityB = 658376509;
    /// <summary>How long a goal waits before re-checking a missing array.</summary>
    private const int RetryAfterTicks = 600;

    /// <summary>Orders colonists to carry the being into a Stasis Array.</summary>
    /// <returns>True when the order was placed.</returns>
    public static bool TryOrder(Being being) {
      if (!CaptureRules.IsCapturable(being, out var storable)) { return false; }
      if (CaptureRules.IsOrdered(being)) { return false; }

      var goal = AIGoal.Create(JobTypeH, T.StasisSendButton, storable, G.Ticks)
        .WithIcon(CaptureRules.Icon)
        .WithWorldToDesired(CaptureRules.SentToStasisKey, false, true)
        .ByPlayer(true)
        .WithRequiredAbilities(AbilityA, AbilityB)
        .WriteInitialState();
      goal.CheckDoable = Check;

      // An explicit order replaces whatever was planned for the being, e.g. a rescue to a bed
      if (being.Goal != null) {
        AIGoal.Cancel(being.Goal, G.Ticks, T.StasisSendButton, true);
      }
      // The being itself can't move, so its own task is pointless (same as the game does)
      var own = being.AIAgent?.Goal;
      if (own != null && being.Motion != null && being.Motion.IsImmobilized) {
        AIGoal.Interrupt(own, G.Ticks, T.StasisSendButton);
      }

      bool placed = being.TrySetGoal(goal, null, true);
      D.Warn("[StasisCapture] Ordered {0} into stasis: {1}", being.Data?.Name, placed);
      return placed;
    }

    /// <summary>
    /// Goal check, re-run by the AI while the goal waits: the being must still be
    /// capturable and some array must have room.
    /// </summary>
    public static AIGoalCheckResult Check(GameState S, AIGoal goal) {
      if (!(goal.Target is StorableBeingComp storable)) {
        return AIGoalCheckResult.Error("Stasis capture goal has no storable target");
      }
      if (!CaptureRules.IsCapturable(storable.Being, out _)) {
        // Woke up, died or was taken elsewhere: nothing left to do
        return AIGoalCheckResult.Fail(T.AdRejTargetUnavailable, false, CaptureRules.Icon, 0);
      }

      var problem = CaptureRules.FindProblem(S);
      if (problem == ArrayProblem.None) { return AIGoalCheckResult.Doable; }
      return AIGoalCheckResult.Fail(ProblemText(problem), true, CaptureRules.Icon, RetryAfterTicks);
    }

    /// <summary>Player-facing text per array problem; read on use, so a language switch applies.</summary>
    private static readonly Dictionary<ArrayProblem, Func<string>> problemTexts =
      new Dictionary<ArrayProblem, Func<string>> {
        [ArrayProblem.NoArray] = () => "stasiscapture.no.array".T(CaptureRules.ArrayName()),
        [ArrayProblem.Full] = () => "stasiscapture.full".T(CaptureRules.ArrayName()),
        [ArrayProblem.Unstable] = () => T.StasisSendStabilityTooLow,
      };

    /// <summary>Player-facing text of an array problem.</summary>
    public static string ProblemText(ArrayProblem problem) {
      return problemTexts.TryGetValue(problem, out var text) ? text() : string.Empty;
    }
  }
}
