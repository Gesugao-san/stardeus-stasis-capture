// Stasis Capture — marks non-colonists on the "Choose Who Awakens" screen.

using System;
using System.Runtime.CompilerServices;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using Game.Utils;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace StasisCapture {
  /// <summary>
  /// The wake-up choice shows sleepers as colonist cards with a "Select this colonist"
  /// button, even when the sleeper is a captured raider who wakes up hostile. A postfix on
  /// WakeUpBeingOptionWidget.Load turns such a card red and renames its button.
  /// Only the card's own frame and header are recolored: the persona and job rows are
  /// pooled widgets shared with other windows, and the button repaints its background
  /// by state, so both keep their colors.
  /// </summary>
  internal static class WakeUpCardStyle {
    private static readonly AccessTools.FieldRef<WakeUpBeingOptionWidget, WButtonText> buttonRef =
      AccessTools.FieldRefAccess<WakeUpBeingOptionWidget, WButtonText>("btnChoose");
    private static readonly AccessTools.FieldRef<WakeUpBeingOptionWidget, WEntityHeader> headerRef =
      AccessTools.FieldRefAccess<WakeUpBeingOptionWidget, WEntityHeader>("wEntityHeader");
    private static readonly AccessTools.FieldRef<WakeUpBeingOptionWidget, WGroup> itemsRef =
      AccessTools.FieldRefAccess<WakeUpBeingOptionWidget, WGroup>("wItems");

    /// <summary>Colors before recoloring, so a reused (pooled) card can be restored.</summary>
    private static readonly ConditionalWeakTable<Graphic, StrongBox<Color>> originals =
      new ConditionalWeakTable<Graphic, StrongBox<Color>>();

    public static void Apply(Harmony harmony) {
      harmony.Patch(AccessTools.Method(typeof(WakeUpBeingOptionWidget), nameof(WakeUpBeingOptionWidget.Load)),
        postfix: new HarmonyMethod(typeof(WakeUpCardStyle), nameof(AfterLoad)));
    }

    private static void AfterLoad(WakeUpBeingOptionWidget __instance, BeingData being) {
      try {
        bool stranger = being != null && !being.IsPlayer;
        Recolor(__instance, stranger);
        if (stranger) { RenameButton(__instance); }
      } catch (Exception e) {
        KL.Utils.D.Err("[StasisCapture] Styling the wake-up card failed: " + e);
      }
    }

    /// <summary>Same button, same click handler, a text that doesn't call the raider a colonist.</summary>
    private static void RenameButton(WakeUpBeingOptionWidget card) {
      var button = buttonRef(card);
      if (button == null) { return; }
      var choose = AccessTools.MethodDelegate<Action>(
        AccessTools.Method(typeof(WakeUpBeingOptionWidget), "Choose"), card);
      button.Load("stasiscapture.wake.button".T(), choose);
    }

    /// <summary>Turns green UI parts red for a stranger, restores them for a colonist.</summary>
    private static void Recolor(WakeUpBeingOptionWidget card, bool stranger) {
      var items = itemsRef(card)?.transform;
      // The button paints its own background by state, so a red text would end up on green
      var button = buttonRef(card)?.transform;
      var avatar = headerRef(card)?.Image;
      foreach (var graphic in card.GetComponentsInChildren<Graphic>(true)) {
        if (graphic == avatar) { continue; }
        if (IsUnder(graphic, items) || IsUnder(graphic, button)) { continue; }

        if (!originals.TryGetValue(graphic, out var original)) {
          if (!stranger) { continue; }  // never touched, nothing to restore
          original = new StrongBox<Color>(graphic.color);
          originals.Add(graphic, original);
        }
        graphic.color = stranger ? Redden(original.Value) : original.Value;
      }
    }

    private static bool IsUnder(Graphic graphic, Transform root) {
      return root != null && graphic.transform.IsChildOf(root);
    }

    /// <summary>Swaps the red and green channels of greenish colors; others (white icons) stay.</summary>
    private static Color Redden(Color c) {
      bool greenish = c.g > c.r && c.g > c.b;
      return greenish ? new Color(c.g, c.r, c.b, c.a) : c;
    }
  }
}
