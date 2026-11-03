// Stasis Capture — bottom bar button "Send to Stasis" for selected incapacitated beings.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace StasisCapture {
  /// <summary>
  /// Shown for a selected living, biological, incapacitated being of any faction.
  /// The button is disabled, with the reason in its tooltip, while no Stasis Array
  /// can take a being; it is hidden once the being already has this order.
  /// </summary>
  public sealed class SendToStasisEntityAction : BaseEntityAction {
    public override string Id => "StasisCapture_SendToStasis";

    [SelfInit]
    private static void Register() {
      RegisterAction(new SendToStasisEntityAction());
      KL.Utils.D.Warn("[StasisCapture] Registered bottom bar action");
    }

    protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
      return Offer(s, new List<Being> { entity }, false, out data);
    }

    protected override bool ApplyForMany(GameState s, List<Being> beings, out EntityActionData data) {
      return Offer(s, beings, true, out data);
    }

    /// <summary>Fills the action data when at least one of the beings can be sent.</summary>
    private bool Offer(GameState s, List<Being> beings, bool multi, out EntityActionData data) {
      var candidates = Candidates(beings);
      if (candidates.Count == 0) {
        data = EntityActionData.Unavailable;
        return false;
      }

      var problem = CaptureRules.FindProblem(s);
      data = new EntityActionData(this,
        (life, parent, idx) => CreateButton(life, parent, idx, candidates, problem, multi),
        Priority.Normal, 0);
      return true;
    }

    /// <summary>Beings that can be captured and are not ordered yet.</summary>
    private static List<Being> Candidates(List<Being> beings) {
      return beings
        .Where(b => CaptureRules.IsCapturable(b, out _) && !CaptureRules.IsOrdered(b))
        .ToList();
    }

    private WBase CreateButton(Lifetime life, RectTransform parent, int idx,
        List<Being> candidates, ArrayProblem problem, bool multi) {
      bool available = problem == ArrayProblem.None;
      Action onClick = available ? () => OrderAll(candidates) : (Action)(() => { });
      var button = multi
        ? SpawnButtonMulti(life, available, parent, idx)
        : SpawnButton(life, available, parent, idx);
      if (multi) {
        button.LoadMulti(candidates.Count, CaptureRules.Icon, T.StasisSendButton, onClick);
      } else {
        button.Load(CaptureRules.Icon, T.StasisSendButton, onClick);
      }
      UIUtils.AddTip(life, button, Tooltip(candidates, problem));
      return button;
    }

    private static void OrderAll(List<Being> candidates) {
      foreach (var being in candidates) { CaptureOrder.TryOrder(being); }
    }

    /// <summary>What happens, a warning for non-colonists, and why the button is disabled.</summary>
    private static string Tooltip(List<Being> candidates, ArrayProblem problem) {
      var tip = new StringBuilder("stasiscapture.tip".T(CaptureRules.ArrayName()));
      if (candidates.Any(b => !b.Data.IsPlayer)) {
        tip.Append("\n\n").Append(Texts.Red("stasiscapture.tip.faction".T()));
      }
      if (problem != ArrayProblem.None) {
        tip.Append("\n\n").Append(Texts.Red(CaptureOrder.ProblemText(problem)));
      }
      return tip.ToString();
    }
  }
}
