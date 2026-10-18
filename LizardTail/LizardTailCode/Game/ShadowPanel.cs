using Godot;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// The picker: every run of the seed you are playing, so you can shadow whichever one you meant.
///
/// The default opens on the run that got furthest, but which run you want is a matter of taste on
/// the day — the earliest attempt, the latest, or one somewhere in the middle — so all of them are
/// listed and the choice can change mid-run.
///
/// Its own CanvasLayer rather than a panel bolted to a screen: it is wanted on the map, in a shop
/// and mid-fight alike, and the game has no one screen that covers all three.
/// </summary>
public partial class ShadowPanel : CanvasLayer
{
    /// <summary>Opens and closes the picker. Rarely claimed by the game or by other mods.</summary>
    public const Key ToggleKey = Key.F6;

    private const string Gold = "e0c070";
    private const string Ink = "e8e2d0";
    private const string Muted = "9a9a9a";
    private const string Good = "5fd18f";

    private static ShadowPanel? _instance;

    private VBoxContainer _column = null!;
    private PanelContainer _panel = null!;

    /// <summary>Adds the picker to the scene tree. Safe to call from mod initialization.</summary>
    public static void Install()
    {
        if (_instance != null || Engine.GetMainLoop() is not SceneTree tree)
        {
            return;
        }

        // Mod initialization does not reliably run on the main thread, and building a Node off it
        // can take the engine down. Deferring puts construction and insertion on the main thread.
        Callable.From(() =>
        {
            try
            {
                _instance = new ShadowPanel();
                tree.Root.AddChild(_instance);
            }
            catch (Exception ex)
            {
                ModLog.Error($"Shadow picker could not be added to the scene tree: {ex}");
            }
        }).CallDeferred();
    }

    public override void _Ready()
    {
        Name = "LizardTailShadowPanel";
        Layer = 101;
        ProcessMode = ProcessModeEnum.Always;

        _panel = new PanelContainer
        {
            Name = "Panel",
            MouseFilter = Control.MouseFilterEnum.Stop,
            Position = new Vector2(40, 120),
            CustomMinimumSize = new Vector2(430, 0),
        };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.07f, 0.93f),
            BorderColor = new Color(Gold),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });

        _column = new VBoxContainer { Name = "Column" };
        _column.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(_column);
        AddChild(_panel);

        Visible = false;
        Shadowing.Changed += Redraw;
        Redraw();
    }

    public override void _ExitTree() => Shadowing.Changed -= Redraw;

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.Keycode == ToggleKey)
        {
            Visible = !Visible;
            if (Visible)
            {
                Redraw();
            }
            GetViewport().SetInputAsHandled();
        }
        else if (Visible && key.Keycode == Key.Escape)
        {
            Visible = false;
            GetViewport().SetInputAsHandled();
        }
    }

    private void Redraw()
    {
        if (_column == null || !IsInstanceValid(_column))
        {
            return;
        }

        foreach (var child in _column.GetChildren())
        {
            child.QueueFree();
        }

        var library = Shadowing.Library;
        if (library == null)
        {
            Add(Label("Shadowing — no run in progress.", 18, Muted));
            return;
        }

        Add(Label(ShadowText.Title(library), 20, Gold, bold: true));

        if (!library.HasAny)
        {
            Add(Label(ShadowText.Nothing(library.Seed), 15, Muted, wrap: true));
            Add(Label($"{ToggleKey} closes this.", 13, Muted));
            return;
        }

        Add(Label(ShadowText.Position(library), 14, Muted));

        for (var i = 0; i < library.Runs.Count; i++)
        {
            var index = i;
            var chosen = i == library.Index;
            var button = new Button
            {
                Text = (chosen ? "● " : "   ") + ShadowText.Row(library.Runs[i]),
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
            };
            button.AddThemeFontSizeOverride("font_size", 15);
            button.AddThemeColorOverride("font_color", new Color(chosen ? Good : Ink));
            button.Pressed += () => Shadowing.Select(index);
            Add(button);
        }

        var glow = new Button
        {
            Text = Shadowing.Suppressed ? "Show the glow" : "Hide the glow",
            FocusMode = Control.FocusModeEnum.None,
        };
        glow.AddThemeFontSizeOverride("font_size", 14);
        glow.Pressed += () => Shadowing.Suppressed = !Shadowing.Suppressed;
        Add(glow);

        Add(Label($"{ToggleKey} closes this. Marks appear as you reach a floor the shadowed run also reached.",
            13, Muted, wrap: true));
    }

    private void Add(Control control) => _column.AddChild(control);

    private static Label Label(string text, int size, string colour, bool bold = false, bool wrap = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color(colour));
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(400, 0);
        }
        if (bold)
        {
            label.AddThemeConstantOverride("outline_size", 0);
        }
        return label;
    }
}
