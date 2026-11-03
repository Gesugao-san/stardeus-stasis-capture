// Stasis Capture — AI action: a colonist carries an incapacitated being into a Stasis Array.

using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Path;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace StasisCapture {
  /// <summary>
  /// Mirrors the game's AIActionStoreBeing_Carry step by step (walk to the being,
  /// pick it up, choose the nearest reachable array, walk to its entrance), but
  /// finishes the way AIActionEnterStasis does: StoreColonist + SendToStasis and
  /// the being's entity removed on the next tick.
  /// </summary>
  public sealed class CarryToStasisAction : AIAction {
    public const string ActionId = "StasisCapture_CarryToStasis";

    /// <summary>Path flags the game uses to walk next to a being (AIActionStoreBeing_Carry).</summary>
    private const PathReqFlags ToBeingFlags = (PathReqFlags)259;
    /// <summary>Path flags the game uses to walk to a storage entrance.</summary>
    private const PathReqFlags ToStorageFlags = (PathReqFlags)257;
    /// <summary>Give up reaching the being after this many ticks of standing still.</summary>
    private const int MaxTicksToReach = 180;

    public CarryToStasisAction() : base(ActionId, () => "stasiscapture.action".T()) {
      Preconditions = AIState.With(CaptureRules.SentToStasisKey, false);
      Outcomes = AIState.With(CaptureRules.SentToStasisKey, true);
      WithRequiredAbilities(CaptureOrder.AbilityB, CaptureOrder.AbilityA);
      MarkAsWork();
      MarkAsMobile();
    }

    [SelfInit]
    private static void Register() {
      RegisterAction(new CarryToStasisAction());
      D.Warn("[StasisCapture] Registered AI action {0}", ActionId);
    }

    public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
      ResetTarget(agent);
      agent.BB.ClearMoveTarget();
      agent.BB.ClearBlacklist();
    }

    public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
      ResetTarget(agent);
    }

    /// <summary>Same cost as storing a being: three times the walk to it.</summary>
    public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
      var target = (goal.Target as IWithEntity)?.Entity;
      if (target == null || target == agent.Being) { return 0f; }
      return agent.Motion.GoToCost(target.PosIdx) * 3f;
    }

    public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
      if (agent.Motion.IsBusy) { return InProgress; }
      if (!(goal.Target is StorableBeingComp storable)) {
        return Error("Goal target not storable: {0}", goal);
      }

      // Only this very being counts as "carrying": anything else in the hands would
      // otherwise be dropped at the array while the being stays where it lies
      if (!IsCarrying(agent, storable.Being)) {
        ResetTarget(agent);
        agent.Being.Graphics.DropIfCarrying();
        var pickUp = PickUp(agent, storable);
        if (pickUp.HasValue) { return pickUp.Value; }
      }
      if (!(agent.BB.Object1 is StasisArrayComp array)) {
        return FindArray(S, agent, goal, ticks);
      }
      if (agent.PosIdx != array.Tile.Transform.InPort) {
        return TryAnotherArray(agent);
      }
      return StoreInto(S, agent, storable, array, goal, ticks);
    }

    private static bool IsCarrying(AIAgentComp agent, Being being) {
      return being != null && agent.Being.Graphics.Carrying == being;
    }

    /// <summary>
    /// Walks to the being and picks it up.
    /// </summary>
    /// <returns>Null once the being is carried, otherwise the result for this tick.</returns>
    private AIActionResult? PickUp(AIAgentComp agent, StorableBeingComp storable) {
      var being = storable.Being;
      if (!CaptureRules.IsCapturable(being, out _)) { return Failed(T.AdRejTargetUnavailable); }

      if (EntityUtils.IsNearObj(agent.PosIdx, being, 1)) {
        if (agent.Being.Graphics.Carry(being)) { return null; }
        return Failed(T.AdRejTargetUnavailable);
      }
      if (!agent.Motion.TryGoTo(being.PosIdx, ToBeingFlags)) {
        return FailedNavToTarget(agent, true);
      }
      if (agent.TicksInCurrentAction > MaxTicksToReach) {
        return FailedSoft(T.AdRejTargetUnavailable);
      }
      return InProgress;
    }

    /// <summary>Chooses the nearest reachable array that can take a being and walks to it.</summary>
    private AIActionResult FindArray(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
      bool grounded = agent.Motion.IsGrounded;
      int pos = agent.PosIdx;
      int island = grounded ? S.Sys.Areas.IslandAt(pos) : 0;

      StasisArrayComp best = null;
      float bestDist = float.MaxValue;
      bool skippedUnreachable = false;
      // ToList: the rules reuse one list, and blacklisting below must not disturb the loop
      foreach (var array in CaptureRules.WorkingArrays(S).ToList()) {
        if (agent.BB.IsBlacklisted(array.EntityId)) { continue; }
        if (!CaptureRules.CanAccept(array)) {
          agent.BB.AddToBlacklist(array.EntityId);
          continue;
        }

        int inPort = array.Tile.Transform.InPort;
        bool reachable = agent.IsFeelingSafeAt(inPort)
          && (!grounded || S.Sys.Areas.CanReachGroundedIslandToPos(island, inPort));
        if (!reachable) {
          agent.BB.AddToBlacklist(array.EntityId);
          skippedUnreachable = true;
          continue;
        }

        float dist = Pos.SqrDistance(pos, inPort);
        if (dist < bestDist) {
          best = array;
          bestDist = dist;
        }
      }

      if (best == null) { return NoArray(S, agent, goal, ticks, skippedUnreachable); }
      return WalkTo(agent, best);
    }

    private AIActionResult NoArray(GameState S, AIAgentComp agent, AIGoal goal, long ticks, bool skippedUnreachable) {
      if (skippedUnreachable) {
        agent.BB.SetGoalCooldown(goal.Id, ticks, 120, 240);
        return FailedSoft(T.NavFailUnreachableGrounded);
      }
      return Failed(CaptureOrder.ProblemText(CaptureRules.FindProblem(S)));
    }

    private AIActionResult WalkTo(AIAgentComp agent, StasisArrayComp array) {
      int inPort = array.Tile.Transform.InPort;
      agent.BB.MoveTargetPos = inPort;
      if (!agent.Motion.TryGoTo(inPort, ToStorageFlags, out agent.BB.MoveProgress, 0, 0)) {
        agent.Motion.StoreNavFailureAt(inPort, 60);
        agent.BB.AddToBlacklist(array.EntityId);
        return InProgress;
      }
      agent.BB.TargetTile = array.Tile;
      agent.BB.Object1 = array;
      return InProgress;
    }

    /// <summary>Puts the carried being into the array, the way a colonist enters stasis.</summary>
    private AIActionResult StoreInto(GameState S, AIAgentComp agent, StorableBeingComp storable,
        StasisArrayComp array, AIGoal goal, long ticks) {
      // Another colonist may have filled the last chamber while this one walked
      if (!CaptureRules.CanAccept(array)) { return TryAnotherArray(agent); }

      var being = storable.Being;
      var data = being.Data;
      agent.Being.Graphics.DropIfCarrying();
      if (!CaptureRules.IsCapturable(being, out _)) { return Failed(T.AdRejTargetUnavailable); }

      array.StoreColonist();
      StasisUtils.SendToStasis(S, data, array, ticks);
      goal.WorldState.Set(CaptureRules.SentToStasisKey, true);
      // The being's data stays in the stasis manifest; only the map entity goes
      S.Clock.OnNextTick(() => {
        if (data.Being != null) { S.Beings.Destroy(being, true); }
      });
      D.Warn("[StasisCapture] {0} put {1} into stasis ({2}/{3})",
        agent.BeingData?.Name, data.Name, array.BeingCount, array.Capacity);
      return Success;
    }

    /// <summary>Forgets the current array and lets the next tick choose another one.</summary>
    private AIActionResult TryAnotherArray(AIAgentComp agent) {
      if (agent.BB.TargetTile != null) { agent.BB.AddToBlacklist(agent.BB.TargetTile.Id); }
      ResetTarget(agent);
      return InProgress;
    }

    private static void ResetTarget(AIAgentComp agent) {
      agent.BB.TargetTile = null;
      agent.BB.Object1 = null;
    }
  }
}
