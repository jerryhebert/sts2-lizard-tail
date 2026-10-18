using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// The mark on something you chose last time: a green glow lying under it, larger than it, so what
/// you see is a rim of light around the edges and a soft bloom beyond. It breathes, so it reads as
/// something burning rather than as a drawn shape.
///
/// Under rather than over, and that is the whole design. An outline on top reads as part of the art
/// - the first attempt put a rectangle inside the card, competing with the frame and leaving it
/// indistinguishable from a card whose art happens to be green. Light leaking out from behind
/// cannot be mistaken for anything the card itself is doing.
/// </summary>
public partial class ShadowGlow : Panel
{
    public const string NodeName = "LizardTailShadowGlow";

    /// <summary>Green, and not a green the game uses: its own accents are gold, red and purple.</summary>
    private static readonly Color Line = new("5fd18f");

    private const float QuietAlpha = 0.45f;
    private const float BrightAlpha = 0.95f;
    private const double BreathSeconds = 1.0;

    /// <summary>How far the glow reaches past the edges of what it marks.</summary>
    private const float Spill = 15f;

    /// <summary>
    /// Marks a control, or does nothing if it is already marked.
    ///
    /// <paramref name="centredSize"/> is for a target with no rect of its own: a card is drawn
    /// centred on its node's origin and leaves that node sized zero, so the glow takes the card's
    /// own dimensions and is centred the same way instead of collapsing to nothing.
    /// </summary>
    public static void Mark(Control? target, Vector2? centredSize = null)
    {
        if (target == null || !IsInstanceValid(target) || target.GetNodeOrNull<Node>(NodeName) != null)
        {
            return;
        }

        try
        {
            var glow = new ShadowGlow();
            target.AddChild(glow);

            // First child, so everything the target draws lands on top of it. ShowBehindParent is
            // not enough on its own: it orders the glow against its parent, not against the
            // parent's other children, which is where the art actually is.
            target.MoveChild(glow, 0);

            if (centredSize is { } size)
            {
                glow.Size = size + new Vector2(Spill * 2, Spill * 2);
                glow.Position = -size / 2 - new Vector2(Spill, Spill);
            }
            else
            {
                glow.SetAnchorsPreset(LayoutPreset.FullRect);
                glow.OffsetLeft = -Spill;
                glow.OffsetTop = -Spill;
                glow.OffsetRight = Spill;
                glow.OffsetBottom = Spill;
            }

            glow.PivotOffset = glow.Size / 2;
        }
        catch (Exception ex)
        {
            // A missing highlight is a cosmetic loss; it must never take a screen down with it.
            ModLog.Warn($"Could not mark a previously chosen option: {ex.Message}");
        }
    }

    public override void _Ready()
    {
        Name = NodeName;
        MouseFilter = MouseFilterEnum.Ignore;
        ShowBehindParent = true;
        ZIndex = -1;

        // Filled, not outlined: the card covers all of it but the rim, and the shadow carries the
        // light further out so the edge is soft instead of a drawn line.
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(Line, 0.85f),
            CornerRadiusTopLeft = 24,
            CornerRadiusTopRight = 24,
            CornerRadiusBottomLeft = 24,
            CornerRadiusBottomRight = 24,
            ShadowColor = new Color(Line, 0.5f),
            ShadowSize = 26,
        });

        Modulate = new Color(1, 1, 1, QuietAlpha);
        Breathe();
    }

    private void Breathe()
    {
        try
        {
            // Brightness and size together: light that only blinks reads as a shape being toggled,
            // where light that also swells reads as something burning behind the card.
            var tween = CreateTween().SetLoops();
            tween.TweenProperty(this, "modulate:a", BrightAlpha, BreathSeconds).SetTrans(Tween.TransitionType.Sine);
            tween.Parallel().TweenProperty(this, "scale", Vector2.One * 1.04f, BreathSeconds).SetTrans(Tween.TransitionType.Sine);
            tween.TweenProperty(this, "modulate:a", QuietAlpha, BreathSeconds).SetTrans(Tween.TransitionType.Sine);
            tween.Parallel().TweenProperty(this, "scale", Vector2.One, BreathSeconds).SetTrans(Tween.TransitionType.Sine);
        }
        catch (Exception ex)
        {
            // Still a perfectly good highlight standing still.
            ModLog.Warn($"Could not animate the shadow glow: {ex.Message}");
        }
    }

    /// <summary>Every node under one, for finding option nodes without reaching into private fields.</summary>
    internal static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }
}

/// <summary>
/// Marks the card you took from this reward last time.
///
/// RefreshOptions is the one method that fills the row, and it runs again on a reroll, so the marks
/// are rebuilt along with the cards rather than going stale.
/// </summary>
[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
public static class ShadowCardRewardPatch
{
    public static void Postfix(NCardRewardSelectionScreen __instance, IReadOnlyList<CardCreationResult> options)
    {
        if (!Shadowing.IsOn)
        {
            return;
        }

        try
        {
            var floor = Shadowing.CurrentFloor();
            var wanted = floor?.PickedCards ?? Array.Empty<string>();
            var offered = options.Select(o => o.Card?.Id?.ToString() ?? "?").ToList();

            // The holders are found by walking the screen rather than through the game's own
            // GetCardHolder, which matches on object identity and throws when nothing matches.
            var holders = ShadowGlow.Descendants(__instance).OfType<NGridCardHolder>().ToList();
            var marked = 0;
            foreach (var holder in holders)
            {
                var id = holder.CardModel?.Id?.ToString();
                if (id == null || !wanted.Contains(id, StringComparer.Ordinal))
                {
                    continue;
                }

                // The card, not its holder: the holder is laid out by position and has no rect at
                // all, while the card is drawn centred on its own node at a size the game states.
                ShadowGlow.Mark(holder.CardNode, NCard.defaultSize);
                marked++;
            }

            ModLog.Info($"Card reward {Shadowing.Where()}: offered [{string.Join(", ", offered)}], "
                + $"took last time [{string.Join(", ", wanted)}], {holders.Count} holder(s), marked {marked}.");
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not mark the card taken last time: {ex.Message}");
        }
    }
}

/// <summary>
/// Marks the event option you took last time, and the Neow blessing you started with.
///
/// One patch covers both: the Ancient layout is a subclass of the event layout and fills its
/// options through the same method.
/// </summary>
[HarmonyPatch(typeof(NEventLayout), nameof(NEventLayout.AddOptions))]
public static class ShadowEventOptionPatch
{
    public static void Postfix(NEventLayout __instance)
    {
        if (!Shadowing.IsOn)
        {
            return;
        }

        try
        {
            var floor = Shadowing.CurrentFloor();
            var wanted = floor?.ChosenOptions.ToList() ?? new List<string>();
            var buttons = __instance.OptionButtons.ToList();
            var seen = new List<string>();
            var marked = 0;

            foreach (var button in buttons)
            {
                var key = KeyOf(button.Option);
                seen.Add(key);
                if (key.Length > 0 && wanted.Contains(key, StringComparer.Ordinal))
                {
                    ShadowGlow.Mark(button);
                    marked++;
                }
            }

            ModLog.Info($"Event options {Shadowing.Where()}: on screen [{string.Join(", ", seen)}], "
                + $"chose last time [{string.Join(", ", wanted)}], marked {marked}.");
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not mark the event option chosen last time: {ex.Message}");
        }
    }

    /// <summary>
    /// How the game identifies an option in its own history: the second-from-last part of the
    /// option's localization key. Matching on the displayed text would break with the language.
    /// </summary>
    private static string KeyOf(EventOption? option) =>
        option?.HistoryName?.LocEntryKey is { } key ? ShadowRun.OptionKey(key) : "";
}
