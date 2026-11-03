# Changelog

The release workflow uses the section of the released version as the release notes,
so keep one `## <version>` heading per `ModVersion` from `ModInfo.json`.

## 1.0.0

- First release, for Stardeus v0.15.
- **Send to Stasis** on the bottom bar for a selected living, incapacitated biological being of
  any faction (several at once too): a colonist who can carry beings takes it to the nearest
  reachable Stasis Array with a free chamber.
- An **Incapacitated** section in a Stasis Array's Send to Stasis panel; an order given there
  prefers that array.
- The game's own rules: robots can't go into stasis, and arrays below 90% stability don't take
  anyone. Tooltips say why the button is disabled.
- Sleepers keep their faction: a hostile being wakes up hostile. On the wake-up choice screen
  such a sleeper gets a red card and a **Wake up (not a colonist)** button.
- If the being dies or comes to on the way, the carrier puts it down and the order is cancelled.
- Orders pending in a save continue after loading.
