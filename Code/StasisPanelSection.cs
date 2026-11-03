// Stasis Capture — "Incapacitated" section in the Stasis Array's "Send to Stasis" panel.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Game;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using HarmonyLib;
using KL.Utils;

namespace StasisCapture {
  /// <summary>
  /// The Stasis Array tab has a "Send to Stasis" button whose panel lists colonists
  /// who can walk in themselves (StasisArrayTab.ShowSendToStasisSubPanel). A
  /// transpiler adds a call right before the panel is shown, so the game builds its
  /// own list as usual and this mod appends a second block below it: every
  /// incapacitated being that a colonist can carry into this array.
  /// </summary>
  internal static class StasisPanelSection {
    private const string Target = "ShowSendToStasisSubPanel";
    private const int SectionSiblingIdx = 1;

    private static readonly AccessTools.FieldRef<StasisArrayTab, StasisArrayComp> stasisRef =
      AccessTools.FieldRefAccess<StasisArrayTab, StasisArrayComp>("stasis");

    public static void Apply(Harmony harmony) {
      harmony.Patch(AccessTools.Method(typeof(StasisArrayTab), Target),
        transpiler: new HarmonyMethod(typeof(StasisPanelSection), nameof(Transpiler)));
    }

    /// <summary>
    /// Inserts <c>AddSection(this, panel, panelLifetime)</c> before
    /// <c>panel.Show(forWidget, "SendToStasis")</c>. The panel and its lifetime are
    /// locals of the game method, taken from where it creates them. If the method
    /// doesn't look as expected, it is left untouched and the bottom bar button
    /// still works.
    /// </summary>
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
      var code = instructions.ToList();
      var createLifetime = AccessTools.Method(typeof(SubPanelWidget), nameof(SubPanelWidget.CreateLifetime));
      var show = AccessTools.Method(typeof(SubPanelWidget), nameof(SubPanelWidget.Show),
        new[] { typeof(WBase), typeof(string) });

      int create = code.FindIndex(c => c.Calls(createLifetime));
      int showAt = code.FindLastIndex(c => c.Calls(show));
      // panel.CreateLifetime(this.lifetime): ldloc panel, ldarg.0, ldfld lifetime, callvirt; then stloc life
      // panel.Show(forWidget, "SendToStasis"): ldloc panel, ldarg.1, ldstr, callvirt
      bool matches = create >= 3 && create + 1 < code.Count && showAt >= 3
        && code[create - 3].IsLdloc() && code[create + 1].IsStloc() && code[showAt - 3].IsLdloc();
      if (!matches) {
        D.Err("[StasisCapture] {0} changed in this game version; the panel section is off", Target);
        return code;
      }

      var call = new List<CodeInstruction> {
        new CodeInstruction(OpCodes.Ldarg_0),
        new CodeInstruction(code[create - 3].opcode, code[create - 3].operand),
        LoadOf(code[create + 1]),
        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StasisPanelSection), nameof(AddSection))),
      };
      // The "list is empty" branch jumps to the Show call: the jump must land on the new code
      call[0].MoveLabelsFrom(code[showAt - 3]);
      code.InsertRange(showAt - 3, call);
      return code;
    }

    /// <summary>ldloc matching a stloc instruction.</summary>
    private static CodeInstruction LoadOf(CodeInstruction store) {
      if (store.opcode == OpCodes.Stloc_0) { return new CodeInstruction(OpCodes.Ldloc_0); }
      if (store.opcode == OpCodes.Stloc_1) { return new CodeInstruction(OpCodes.Ldloc_1); }
      if (store.opcode == OpCodes.Stloc_2) { return new CodeInstruction(OpCodes.Ldloc_2); }
      if (store.opcode == OpCodes.Stloc_3) { return new CodeInstruction(OpCodes.Ldloc_3); }
      return new CodeInstruction(OpCodes.Ldloc, store.operand);
    }

    /// <summary>Builds the section. Errors are logged and never break the game's panel.</summary>
    public static void AddSection(StasisArrayTab tab, SubPanelWidget panel, Lifetime life) {
      try {
        Build(A.S, stasisRef(tab), panel, life);
      } catch (System.Exception e) {
        D.Err("[StasisCapture] Building the panel section failed: " + e);
      }
    }

    private static void Build(GameState s, StasisArrayComp array, SubPanelWidget panel, Lifetime life) {
      if (s == null || panel?.Container == null) { return; }

      var block = UISpawn.Block(life, panel.Container, SectionSiblingIdx, "WContentBlock");
      int idx = 0;
      UISpawn.Widget<WText>(life, "WContentHeader", block.Container, idx++)
        .Load("stasiscapture.section".T());

      var problem = CaptureRules.ProblemOf(array);
      if (problem != ArrayProblem.None) {
        UISpawn.Widget<WIconText>(life, "WContent2Text", block.Container, idx++)
          .Load("Icons/Color/Warning", Texts.Red(CaptureOrder.ProblemText(problem)));
      }

      var beings = s.Beings.Alive.Where(b => CaptureRules.IsCapturable(b, out _)).ToList();
      foreach (var being in beings) {
        AddRow(s, array, problem == ArrayProblem.None, being, block, life, ref idx);
      }
      if (beings.Count == 0) {
        UISpawn.Widget<WIconText>(life, "WContent2Text", block.Container, idx++)
          .Load("Icons/Color/Info", "stasiscapture.section.empty".T());
      }
    }

    /// <summary>
    /// One being: its name (click selects it, red for non-colonists) and a Send button,
    /// or "Ordered" once a colonist has the job.
    /// </summary>
    private static void AddRow(GameState s, StasisArrayComp array, bool arrayOk, Being being,
        WContentBlock block, Lifetime life, ref int idx) {
      var data = being.Data;
      var row = UISpawn.Widget<WContentButton2>(life, "WContent2Button2", block.Container, idx++);
      string name = data.IsPlayer ? data.Name : Texts.Red(data.Name);
      row.Item.Load(null, name, () => BeingUtils.Select(data, true));
      data.Avatar.Load(row.Item.Image);
      if (!data.IsPlayer) { UIUtils.AddTip(life, row, "stasiscapture.tip.faction".T()); }

      if (CaptureRules.IsOrdered(being)) {
        row.Button.Load("stasiscapture.ordered".T(), () => { });
        row.Button.SetInteractable(false);
        return;
      }
      row.Button.Load(T.VerbSend, () => {
        CaptureOrder.TryOrder(being, array.EntityId);
        s.UIQuery.CloseEntitySubPanel.Ask();
      });
      row.Button.SetInteractable(arrayOk);
    }
  }
}
