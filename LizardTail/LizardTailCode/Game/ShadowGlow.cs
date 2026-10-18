using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// The mark on something you chose last time: green fire burning behind it, larger than it, so what
/// you see is flame licking out around the edges while the art covers the rest.
///
/// Under rather than over, and that is the whole design. An outline on top reads as part of the art
/// - an early attempt drew a rectangle inside the card, where it competed with the frame and left
/// the card indistinguishable from one whose art simply happens to be green. Light coming from
/// behind cannot be mistaken for anything the card itself is doing.
///
/// The flame is a shader rather than a tween on a solid shape, because a shape that only swells and
/// brightens reads as a pulsing rectangle however it is coloured. The motion has to be in the
/// surface, not in the outline.
/// </summary>
public partial class ShadowGlow : ColorRect
{
    public const string NodeName = "LizardTailShadowGlow";

    /// <summary>How far the fire reaches past the edges of what it marks.</summary>
    private const float Spill = 26f;

    /// <summary>Overall strength. Raised from the first version, which was hard to pick out.</summary>
    private const float Intensity = 1.25f;

    /// <summary>Flat colour for when the shader will not compile, so the mark still appears.</summary>
    private static readonly Color Plain = new Color("5fd18f") with { A = 0.6f };

    /// <summary>
    /// Green fire. It lives near the edges of this rect and fades out well before the boundary, so
    /// no straight edge or corner of the rect is ever visible; the middle is covered by whatever is
    /// being marked. Two noise layers drift upward at different rates, which is what keeps it from
    /// looking like one texture being scrolled.
    /// </summary>
    private const string FlameShader = """
        shader_type canvas_item;

        uniform float intensity = 1.25;
        uniform vec3 ember : source_color = vec3(0.04, 0.42, 0.20);
        uniform vec3 flame : source_color = vec3(0.42, 1.0, 0.52);
        uniform vec3 tip : source_color = vec3(0.86, 1.0, 0.82);

        float hash(vec2 p)
        {
            return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
        }

        float vnoise(vec2 p)
        {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
                       mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
        }

        float fbm(vec2 p)
        {
            float v = 0.0;
            float a = 0.55;
            for (int i = 0; i < 4; i++)
            {
                v += a * vnoise(p);
                p *= 2.03;
                a *= 0.5;
            }
            return v;
        }

        void fragment()
        {
            vec2 uv = UV;

            float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
            float falloff = smoothstep(0.0, 0.11, edge);

            float n1 = fbm(vec2(uv.x * 5.0, uv.y * 6.0 - TIME * 1.30));
            float n2 = fbm(vec2(uv.x * 9.0 + 3.7, uv.y * 11.0 - TIME * 2.05));
            float n = n1 * 0.65 + n2 * 0.35;

            float lick = smoothstep(0.20, 0.90, n);
            float a = falloff * (0.45 + 0.95 * lick) * intensity;

            vec3 col = mix(ember, flame, lick);
            col = mix(col, tip, smoothstep(0.76, 1.0, lick) * 0.6);

            COLOR = vec4(col, clamp(a, 0.0, 1.0));
        }
        """;

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
        }
        catch (Exception ex)
        {
            // A missing highlight is a cosmetic loss; it must never take a screen down with it.
            ModLog.Warn($"Could not mark a previously chosen option: {ex.Message}");
        }
    }

    /// <summary>
    /// Takes the mark off, wherever it sits under the given node.
    ///
    /// Card nodes are pooled: one marked on a reward screen comes back later as an unrelated card in
    /// a fight and brings the fire with it, drawn in front of the hand rather than behind it.
    /// Clearing it on the way into and out of the pool is what keeps a mark on its own screen.
    /// </summary>
    public static void Unmark(Node? target)
    {
        if (target == null || !IsInstanceValid(target))
        {
            return;
        }

        try
        {
            foreach (var glow in Descendants(target).OfType<ShadowGlow>().ToList())
            {
                glow.QueueFree();
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not clear a shadow mark: {ex.Message}");
        }
    }

    public override void _Ready()
    {
        Name = NodeName;
        MouseFilter = MouseFilterEnum.Ignore;
        ShowBehindParent = true;
        ZIndex = -1;
        Color = Plain;

        try
        {
            var material = new ShaderMaterial { Shader = new Shader { Code = FlameShader } };
            material.SetShaderParameter("intensity", Intensity);
            Material = material;
        }
        catch (Exception ex)
        {
            // The flat green rect is still a perfectly readable mark.
            ModLog.Warn($"Could not build the flame shader, falling back to a plain glow: {ex.Message}");
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

/// <summary>Clears a mark as a card node goes back to the pool, and again as it comes out.</summary>
[HarmonyPatch(typeof(NCard))]
public static class ShadowCardPoolingPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NCard.OnFreedToPool))]
    public static void AfterFreed(NCard __instance) => ShadowGlow.Unmark(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NCard.OnReturnedFromPool))]
    public static void AfterReturned(NCard __instance) => ShadowGlow.Unmark(__instance);
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

/// <summary>
/// Marks the route the shadowed run took, so you can see which node to click.
///
/// A map node is joined to history by its row, which the game treats as the index into that act's
/// history - it looks its own icons up the same way. There is no column in the record, so where a
/// row holds two nodes of the same kind both are marked: better to show the pair than to pick one
/// and be confidently wrong.
///
/// Only rows you have not reached yet are marked. The game already draws the path behind you, and
/// relighting it would bury the one decision actually in front of you.
/// </summary>
[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetMap))]
public static class ShadowMapPatch
{
    public static void Postfix(NMapScreen __instance)
    {
        if (!Shadowing.IsOn || Shadowing.Run is not { } run)
        {
            return;
        }

        try
        {
            var state = Shadowing.CurrentState();
            if (state == null)
            {
                return;
            }

            var act = state.CurrentActIndex;
            var history = state.MapPointHistory;
            var nextRow = act >= 0 && act < history.Count ? history[act].Count : 0;

            var points = ShadowGlow.Descendants(__instance).OfType<NMapPoint>().ToList();
            var marked = 0;
            foreach (var point in points)
            {
                var coord = point.Point?.coord;
                if (coord is not { } at || at.row < nextRow)
                {
                    continue;
                }

                if (run.FloorAt(act, at.row) is not { } floor || !SameKind(floor.PointType, point.Point!.PointType))
                {
                    continue;
                }

                ShadowGlow.Mark(point);
                marked++;
            }

            ModLog.Info($"Map for act {act}: {points.Count} node(s), marking from row {nextRow} on, marked {marked}.");
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not mark the route taken last time: {ex.Message}");
        }
    }

    /// <summary>
    /// The record stores the kind as the game's own lower-case name ("rest_site", "monster"), which
    /// is the enum with its words split. Comparing with the separators removed avoids depending on
    /// that spelling.
    /// </summary>
    private static bool SameKind(string recorded, MapPointType live) =>
        string.Equals(recorded.Replace("_", ""), live.ToString(), StringComparison.OrdinalIgnoreCase);
}
