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

    /// <summary>Overall strength. Raised twice from the first version, which was hard to pick out.</summary>
    private const float Intensity = 1.5f;

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

                // An option button is measured by the container holding it, which has not laid out
                // when its screen finishes building: every button is still zero by zero here. The
                // fit is redone once the layout has run, and reported, because a mark that silently
                // draws at the wrong size looks exactly like one that was never drawn at all.
                Callable.From(() => Fit(glow, target)).CallDeferred();
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

        Clear(target);
    }

    /// <summary>
    /// Removes every mark under a node and says how many there were.
    ///
    /// Each one is renamed before being freed, because QueueFree only takes effect at the end of
    /// the frame: a screen that clears and re-marks in one pass would otherwise still see the old
    /// mark by name and decline to draw the new one.
    /// </summary>
    public static int Clear(Node? target)
    {
        if (target == null || !IsInstanceValid(target))
        {
            return 0;
        }

        try
        {
            var found = Descendants(target).Where(n => n.Name == NodeName).ToList();
            foreach (var glow in found)
            {
                glow.Name = "_lizardTailSpentGlow";
                glow.QueueFree();
            }
            return found.Count;
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not clear a shadow mark: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Re-fits a mark to its target after the layout has run, and says what it ended up as.
    /// </summary>
    private static void Fit(ShadowGlow glow, Control target)
    {
        if (!IsInstanceValid(glow) || !IsInstanceValid(target))
        {
            return;
        }

        try
        {
            var spill = new Vector2(Spill, Spill);
            glow.Size = target.Size + spill * 2;
            glow.Position = -spill;
            ModLog.Info($"Mark on {target.Name}: target {target.Size}, mark {glow.Size} at {glow.Position}, "
                + $"visible {glow.IsVisibleInTree()}.");
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not fit a shadow mark: {ex.Message}");
        }
    }

    public override void _Ready()
    {
        Name = NodeName;
        MouseFilter = MouseFilterEnum.Ignore;
        // Behind the parent and its art, but NOT pushed down the canvas layer. A negative ZIndex
        // puts the mark behind the screen's own background panel, where it is invisible: rendering
        // the real node structure in the engine showed the mark vanish at -1 and appear at 0.
        // Cards survived it only because nothing opaque sits behind them.
        ShowBehindParent = true;
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

    /// <summary>
    /// Marks something drawn as a picture with transparency around it - a map node - by burning
    /// along the edges of the picture itself.
    ///
    /// A rectangle behind a map icon fills the icon's whole transparent margin and reads as a green
    /// block, not as a highlight. Here the icon's own alpha is dilated to make the light, so it
    /// follows the shape. Falls back to the rectangle when no picture can be found, which is still
    /// better than no mark at all.
    /// </summary>
    public static void MarkSilhouette(Control? target)
    {
        if (target == null || !IsInstanceValid(target) || target.GetNodeOrNull<Node>(NodeName) != null)
        {
            return;
        }

        var icon = IconOf(target);
        if (icon == null)
        {
            Mark(target);
            return;
        }

        try
        {
            var glow = new ShadowSilhouette();
            glow.SetTexture(icon.Texture);
            icon.AddChild(glow);
            icon.MoveChild(glow, 0);
            glow.SetAnchorsPreset(LayoutPreset.FullRect);
            Callable.From(() => FitTo(glow, icon)).CallDeferred();
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not mark a previously chosen map node: {ex.Message}");
        }
    }

    /// <summary>
    /// The picture to take the shape from. The game names a map node's own art "Icon"; anything
    /// else falls back to the biggest picture under the node, which is the art in practice.
    /// </summary>
    private static TextureRect? IconOf(Control target)
    {
        var pictures = Descendants(target).OfType<TextureRect>()
            .Where(t => t.Texture != null)
            .ToList();

        return pictures.FirstOrDefault(t => t.Name.ToString().Contains("Icon", StringComparison.OrdinalIgnoreCase))
            ?? pictures.OrderByDescending(t => t.Size.X * t.Size.Y).FirstOrDefault();
    }

    private static void FitTo(Control glow, Control target)
    {
        if (!IsInstanceValid(glow) || !IsInstanceValid(target))
        {
            return;
        }

        try
        {
            glow.Size = target.Size;
            glow.Position = Vector2.Zero;
            ModLog.Info($"Silhouette mark on {target.Name}: {glow.Size}, visible {glow.IsVisibleInTree()}.");
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not fit a silhouette mark: {ex.Message}");
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
/// Fire that follows the outline of a picture rather than its bounding box, for map nodes and
/// anything else drawn with transparency around it.
/// </summary>
public partial class ShadowSilhouette : TextureRect
{
    private const float Intensity = 1.6f;

    /// <summary>How far the light reaches past the edge of the shape, in the texture's own pixels.</summary>
    private const float Radius = 9.0f;

    private Texture2D? _source;

    /// <summary>
    /// Dilates the picture's alpha to make a halo outside its edges, then burns that halo. The
    /// subtraction is what keeps the light outside the shape instead of washing over the art.
    /// </summary>
    private const string OutlineShader = """
        shader_type canvas_item;

        uniform float intensity = 1.6;
        uniform float radius = 9.0;
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
            vec2 px = TEXTURE_PIXEL_SIZE;
            float here = texture(TEXTURE, UV).a;

            float reach = 0.0;
            for (int i = 0; i < 16; i++)
            {
                float ang = float(i) * 0.39269908;
                vec2 dir = vec2(cos(ang), sin(ang));
                for (int r = 1; r <= 4; r++)
                {
                    float f = float(r) / 4.0;
                    reach = max(reach, texture(TEXTURE, UV + dir * px * radius * f).a * (1.0 - f * 0.55));
                }
            }

            float halo = clamp(reach - here, 0.0, 1.0);

            float n = fbm(vec2(UV.x * 7.0, UV.y * 8.0 - TIME * 1.5));
            float lick = smoothstep(0.18, 0.92, n);

            vec3 col = mix(ember, flame, lick);
            col = mix(col, tip, smoothstep(0.76, 1.0, lick) * 0.6);

            COLOR = vec4(col, clamp(halo * (0.45 + 0.95 * lick) * intensity, 0.0, 1.0));
        }
        """;

    /// <summary>Set before the node enters the tree, since _Ready is what applies it.</summary>
    public void SetTexture(Texture2D? texture) => _source = texture;

    public override void _Ready()
    {
        Name = ShadowGlow.NodeName;
        MouseFilter = MouseFilterEnum.Ignore;
        ShowBehindParent = true;
        Texture = _source;
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.Scale;

        try
        {
            var material = new ShaderMaterial { Shader = new Shader { Code = OutlineShader } };
            material.SetShaderParameter("intensity", Intensity);
            material.SetShaderParameter("radius", Radius);
            Material = material;
        }
        catch (Exception ex)
        {
            // Without the shader this would just redraw the icon, which would be worse than
            // nothing, so the mark removes itself instead.
            ModLog.Warn($"Could not build the outline shader, so this mark is dropped: {ex.Message}");
            QueueFree();
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
            // Every mark on this screen is redrawn from scratch. Card nodes are pooled and reused,
            // so one can arrive already carrying fire from the screen it was last used on, which
            // showed up in testing as a second card glowing that this code never marked.
            var stale = ShadowGlow.Clear(__instance);

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
                + $"took last time [{string.Join(", ", wanted)}], {holders.Count} holder(s), marked {marked}"
                + (stale > 0 ? $", cleared {stale} stale mark(s) first." : "."));
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
            var stale = ShadowGlow.Clear(__instance);

            var floor = Shadowing.CurrentFloor();

            // How many times each option was taken, not merely whether it was. Two options on one
            // screen can share a key - a screen offering two potions reads as POTION twice - and
            // matching on presence alone lit both of them for a single choice.
            var budget = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var key in floor?.ChosenOptions ?? Enumerable.Empty<string>())
            {
                budget[key] = budget.TryGetValue(key, out var n) ? n + 1 : 1;
            }

            var wanted = budget.Keys.ToList();
            var buttons = __instance.OptionButtons.ToList();
            var seen = new List<string>();
            var marked = 0;

            foreach (var button in buttons)
            {
                var key = KeyOf(button.Option);
                seen.Add(key);
                if (key.Length == 0 || !budget.TryGetValue(key, out var left) || left <= 0)
                {
                    continue;
                }

                budget[key] = left - 1;
                ShadowGlow.Mark(button);
                marked++;
            }

            ModLog.Info($"Event options {Shadowing.Where()}: on screen [{string.Join(", ", seen)}], "
                + $"chose last time [{string.Join(", ", wanted)}], marked {marked}"
                + (stale > 0 ? $", cleared {stale} stale mark(s) first." : "."));
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
/// Only the next row is marked. Lighting the whole route ahead put fire on 26 of 67 nodes in
/// testing, which tells you nothing: the question is which node to click now, and every row after
/// this one depends on what you click.
/// </summary>
[HarmonyPatch(typeof(NMapScreen))]
public static class ShadowMapPatch
{
    /// <summary>The map is built once per act.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMapScreen.SetMap))]
    public static void AfterSetMap(NMapScreen __instance) => Apply(__instance);

    /// <summary>
    /// And redrawn every time it is shown. Marking only on SetMap left the marks frozen on the row
    /// that was next when the act began, so after the first floor they pointed at a decision
    /// already made.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMapScreen.RefreshAllPointVisuals))]
    public static void AfterRefresh(NMapScreen __instance) => Apply(__instance);

    private static void Apply(NMapScreen __instance)
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

            var stale = ShadowGlow.Clear(__instance);
            var points = ShadowGlow.Descendants(__instance).OfType<NMapPoint>().ToList();
            var marked = 0;

            if (run.FloorAt(act, nextRow) is { } next)
            {
                foreach (var point in points)
                {
                    if (point.Point is not { } p || p.coord.row != nextRow || !SameKind(next.PointType, p.PointType))
                    {
                        continue;
                    }

                    ShadowGlow.MarkSilhouette(point);
                    marked++;
                }
            }

            ModLog.Info($"Map for act {act}: {points.Count} node(s), next is row {nextRow}, marked {marked}"
                + (stale > 0 ? $", cleared {stale} stale mark(s) first." : "."));
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
