// Stasis Capture — who can be carried into a Stasis Array, and which arrays can take them.

using System.Collections.Generic;
using Game;
using Game.Components;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using KL.Utils;

namespace StasisCapture {
  /// <summary>Why no Stasis Array can take a being right now.</summary>
  internal enum ArrayProblem {
    /// <summary>At least one array can take a being.</summary>
    None,
    /// <summary>No constructed, working Stasis Array.</summary>
    NoArray,
    /// <summary>Every array is full.</summary>
    Full,
    /// <summary>Arrays have room, but their stability is below the game's sending limit.</summary>
    Unstable,
  }

  /// <summary>
  /// Rules shared by the UI action, the goal check and the AI action, so that what the
  /// player is offered and what a colonist will actually do never disagree.
  /// </summary>
  internal static class CaptureRules {
    /// <summary>World state key of the goal: true once the being is in stasis.</summary>
    public static readonly int SentToStasisKey = Hashes.S("StasisCapture_SentToStasis");

    /// <summary>
    /// The game refuses to send colonists into an array below this stability
    /// (StasisArrayTab: "Stasis Array stability is too low"); captured beings follow the same rule.
    /// </summary>
    public const float MinStability = 0.9f;

    /// <summary>Definition id of the Stasis Array device; its translated name goes into our texts.</summary>
    private const string ArrayDefId = "Objects/Devices/StasisArray";

    /// <summary>Icon of the game's own "Send to Stasis" button.</summary>
    public const string Icon = "Icons/Color/Cryogenics";

    /// <summary>States in which a being is not on the map as a normal entity.</summary>
    private const BeingState AwayStates =
      BeingState.Hidden | BeingState.Expedition | BeingState.Stored | BeingState.InDevice;

    private static readonly List<StasisArrayComp> arrays = new List<StasisArrayComp>();

    /// <summary>
    /// A living, biological, incapacitated being of any faction that is on the map
    /// and has no colony role yet (passengers, deserters and sleepers are excluded).
    /// </summary>
    public static bool IsCapturable(Being being, out StorableBeingComp storable) {
      storable = null;
      if (being == null || !being.IsActive || !being.IsAlive) { return false; }

      var data = being.Data;
      if (data?.Species == null || data.Body == null || data.Mind == null) { return false; }
      if (!data.Species.IsBiological) { return false; }
      if (!data.Body.IsIncapacitated) { return false; }
      if (data.Mind.Role != ColonyRole.Unassigned) { return false; }
      if ((data.StateFlags & AwayStates) != 0) { return false; }
      return being.TryGetComponent(out storable);
    }

    /// <summary>True when a "send to stasis" order for this being is already active.</summary>
    public static bool IsOrdered(Being being) {
      var goal = being?.Goal;
      return goal != null && IsOurGoal(goal) && AIGoal.IsActive(goal);
    }

    /// <summary>True for goals created by this mod.</summary>
    public static bool IsOurGoal(AIGoal goal) {
      return goal != null && goal.DesiredState.Get(SentToStasisKey, false);
    }

    /// <summary>Array that is built, active and still part of this ship.</summary>
    public static bool IsWorking(StasisArrayComp array) {
      var tile = array?.Tile;
      return tile != null && tile.IsActive && tile.IsConstructed && !array.IsSentToColony;
    }

    /// <summary>Array that can take one more being right now.</summary>
    public static bool CanAccept(StasisArrayComp array) {
      return IsWorking(array) && HasRoom(array) && IsStableEnough(array);
    }

    public static bool HasRoom(StasisArrayComp array) => array.BeingCount < array.Capacity;

    public static bool IsStableEnough(StasisArrayComp array) {
      return A.HasAsimovOverride || array.Stability >= MinStability;
    }

    /// <summary>
    /// Working arrays of the current game. The returned list is reused between calls:
    /// read it right away and don't keep it.
    /// </summary>
    public static List<StasisArrayComp> WorkingArrays(GameState s) {
      arrays.Clear();
      s?.Components?.FindAll(arrays);
      arrays.RemoveAll(a => !IsWorking(a));
      return arrays;
    }

    /// <summary>The game's translated name of the Stasis Array, for our texts.</summary>
    public static string ArrayName() {
      return The.Defs?.TryGet(ArrayDefId)?.NameT ?? "Stasis Array";
    }

    /// <summary>Why this particular array can't take a being, or None when it can.</summary>
    public static ArrayProblem ProblemOf(StasisArrayComp array) {
      if (!IsWorking(array)) { return ArrayProblem.NoArray; }
      if (!HasRoom(array)) { return ArrayProblem.Full; }
      return IsStableEnough(array) ? ArrayProblem.None : ArrayProblem.Unstable;
    }

    /// <summary>Reports the most relevant reason why no array can take a being.</summary>
    public static ArrayProblem FindProblem(GameState s) {
      var working = WorkingArrays(s);
      if (working.Count == 0) { return ArrayProblem.NoArray; }

      bool anyRoom = false;
      foreach (var array in working) {
        if (!HasRoom(array)) { continue; }
        if (IsStableEnough(array)) { return ArrayProblem.None; }
        anyRoom = true;
      }
      return anyRoom ? ArrayProblem.Unstable : ArrayProblem.Full;
    }
  }
}
