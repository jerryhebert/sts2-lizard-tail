using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// The mark on something you chose last time: a green border with a soft glow around it, breathing
/// slowly so it reads as a highlight rather than as part of the art.
///
/// Added as the last child of whatever it marks and anchored to fill it, so it draws in front, has
/// a hollow centre that leaves the art readable, and follows its target's own layout and animation
/// without any per-screen positioning.
/// </summary>
public partial class ShadowGlow : Panel
{
    public const string NodeName = "LizardTailShadowGlow";

    /// <summary>Green, and not a green the game uses: its own accents are gold, red and purple.</summary>
    private static readonly Color Line = new("5fd18f");

    private const float QuietAlpha = 0.5f;
    private const float BrightAlpha = 1.0f;
    private const double BreathSeconds = 1.1;

    /// <summary>What to fill when the target has no size of its own yet. Roughly a card.</summary>
    private static readonly Vector2 Fallback = new(230, 320);

    /// <summary>Marks a control, or does nothing if it is already marked.</summary>
    public static void Mark(Control? target)
    {
        if (target == null || !IsInstanceValid(target) || target.GetNodeOrNull<Node>(NodeName) != null)
        {
            return;
        }

        try
        {
            var glow = new ShadowGlow();
            target.AddChild(glow);
            glow.SetAnchorsPreset(LayoutPreset.FullRect);

            // A target laid out by position rather than by size would collapse the glow to nothing.
            if (target.Size.X < 1 || target.Size.Y < 1)
            {
                glow.CustomMinimumSize = Fallback;
                glow.Size = Fallback;
                glow.Position = -Fallback / 2;
                ModLog.Info($"Marked {target.Name}, which had no size yet, at a card's size.");
            }
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
        // In front of the art, which is why the fill is fully transparent.
        ZIndex = 50;

        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            BorderColor = Line,
            BorderWidthLeft = 4,
            BorderWidthTop = 4,
            BorderWidthRight = 4,
            BorderWidthBottom = 4,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            // The drop shadow is what makes it read as a glow rather than a drawn outline.
            ShadowColor = new Color(Line, 0.6f),
            ShadowSize = 18,
            ExpandMarginLeft = 8,
            ExpandMarginTop = 8,
            ExpandMarginRight = 8,
            ExpandMarginBottom = 8,
        });

        Modulate = new Color(1, 1, 1, QuietAlpha);
        Breathe();
    }

    private void Breathe()
    {
        try
        {
            var tween = CreateTween().SetLoops();
            tween.TweenProperty(this, "modulate:a", BrightAlpha, BreathSeconds).SetTrans(Tween.TransitionType.Sine);
            tween.TweenProperty(this, "modulate:a", QuietAlpha, BreathSeconds).SetTrans(Tween.TransitionType.Sine);
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
                if (id != null && wanted.Contains(id, StringComparer.Ordinal))
                {
                    ShadowGlow.Mark(holder);
                    marked++;
                }
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
