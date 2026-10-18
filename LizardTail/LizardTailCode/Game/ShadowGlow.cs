using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// The mark on something you chose last time: a green border with a soft glow behind it, breathing
/// slowly so it reads as a highlight rather than as part of the card art.
///
/// Added as a child of whatever it marks and anchored to fill it, so it follows that node's own
/// layout and animation without any per-screen positioning.
/// </summary>
public partial class ShadowGlow : Panel
{
    public const string NodeName = "LizardTailShadowGlow";

    /// <summary>Green, and not a green the game uses: its own accents are gold, red and purple.</summary>
    private static readonly Color Line = new("5fd18f");

    private const float QuietAlpha = 0.55f;
    private const float BrightAlpha = 1.0f;
    private const double BreathSeconds = 1.1;

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
        }
        catch (Exception ex)
        {
            // A missing highlight is a cosmetic loss; it must never take a screen down with it.
            ModLog.Warn($"Could not mark a previously chosen option: {ex.Message}");
        }
    }

    /// <summary>Removes the mark, for a screen that is rebuilt in place rather than thrown away.</summary>
    public static void Unmark(Control? target)
    {
        if (target != null && IsInstanceValid(target) && target.GetNodeOrNull<Node>(NodeName) is { } glow)
        {
            glow.QueueFree();
        }
    }

    public override void _Ready()
    {
        Name = NodeName;
        MouseFilter = MouseFilterEnum.Ignore;
        // Behind the thing it marks, so the glow spills out around the edges instead of over the art.
        ShowBehindParent = true;

        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            BorderColor = Line,
            BorderWidthLeft = 3,
            BorderWidthTop = 3,
            BorderWidthRight = 3,
            BorderWidthBottom = 3,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            // The drop shadow is what makes it read as a glow rather than a drawn outline.
            ShadowColor = new Color(Line, 0.55f),
            ShadowSize = 16,
            ExpandMarginLeft = 6,
            ExpandMarginTop = 6,
            ExpandMarginRight = 6,
            ExpandMarginBottom = 6,
        });

        Modulate = new Color(1, 1, 1, QuietAlpha);
        Breathe();
    }

    private void Breathe()
    {
        try
        {
            var tween = CreateTween().SetLoops();
            tween.TweenProperty(this, "modulate:a", BrightAlpha, BreathSeconds)
                .SetTrans(Tween.TransitionType.Sine);
            tween.TweenProperty(this, "modulate:a", QuietAlpha, BreathSeconds)
                .SetTrans(Tween.TransitionType.Sine);
        }
        catch (Exception ex)
        {
            // Still a perfectly good highlight standing still.
            ModLog.Warn($"Could not animate the shadow glow: {ex.Message}");
        }
    }
}

/// <summary>
/// Marks the card you took from this reward last time.
///
/// RefreshOptions is the one method that populates the row, and it runs again on a reroll, so the
/// marks are rebuilt with the cards rather than going stale.
/// </summary>
[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
public static class ShadowCardRewardPatch
{
    public static void Postfix(NCardRewardSelectionScreen __instance, IReadOnlyList<CardCreationResult> options)
    {
        if (!Shadowing.IsOn || Shadowing.CurrentFloor() is not { } floor || floor.PickedCards.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var option in options)
            {
                var card = option.Card;
                if (card?.Id == null || !floor.PickedCards.Contains(card.Id.ToString(), StringComparer.Ordinal))
                {
                    continue;
                }

                ShadowGlow.Mark(__instance.GetCardHolder(card));
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not mark the card taken last time: {ex.Message}");
        }
    }
}
