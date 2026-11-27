using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Runs;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// The panel on the pause menu that takes a checkpoint of the run: a copy of the game's save as it was
/// last written (entering the current room, or finishing it), with a label. Going back to one happens
/// on the menus, after Save &amp; Quit, because the game holds a run in memory while it is being played.
/// </summary>
public partial class CheckpointPanel : PanelContainer
{
    public const string NodeName = "LizardTailCheckpoints";

    private const float Width = 460;
    private const float Margin = 40;
    private const int RecentShown = 6;

    private static readonly Color Gold = new("e0c070");
    private static readonly Color Ink = new("e8e2d0");
    private static readonly Color Muted = new("9a9a9a");
    private static readonly Color Warn = new("e0a070");
    private static readonly Color Good = new("8fd18f");

    private static readonly AccessTools.FieldRef<NSubmenu, Control?> LastFocused =
        AccessTools.FieldRefAccess<NSubmenu, Control?>("_lastFocusedControl");

    private NPauseMenu _menu = null!;
    private Label _where = null!;
    private LineEdit _label = null!;
    private SlotButton _save = null!;
    private Label _status = null!;
    private VBoxContainer _recent = null!;
    private Label _howBack = null!;

    /// <summary>
    /// Which save this run writes, when this PC can take a checkpoint of it: the solo save in a solo run,
    /// the multiplayer save when hosting. Null for a joined player (only the host keeps the save) and for
    /// runs the game does not save.
    /// </summary>
    public static SaveKind? KindOfRun()
    {
        if (RunManager.Instance is not { IsInProgress: true, ShouldSave: true } run)
        {
            return null;
        }
        return run.NetService.Type switch
        {
            NetGameType.Singleplayer => SaveKind.Solo,
            NetGameType.Host => SaveKind.Multiplayer,
            _ => null,
        };
    }

    public static void AttachTo(NPauseMenu menu)
    {
        if (menu.GetNodeOrNull<CheckpointPanel>(NodeName) != null)
        {
            return;
        }
        var panel = new CheckpointPanel { Name = NodeName };
        panel.Build(menu);
        menu.AddChild(panel);
        panel.WireFocus();
        panel.Refresh();
    }

    private void Build(NPauseMenu menu)
    {
        _menu = menu;
        MouseFilter = MouseFilterEnum.Stop;
        AnchorLeft = 1;
        AnchorRight = 1;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -Width - Margin;
        OffsetRight = -Margin;
        OffsetTop = -280;
        OffsetBottom = 280;

        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.07f, 0.9f),
            BorderColor = new Color(0.85f, 0.75f, 0.45f, 0.9f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14,
        });

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 10);
        AddChild(column);

        column.AddChild(MakeLabel("Checkpoint", 26, Gold));
        _where = MakeLabel("", 15, Muted);
        column.AddChild(_where);

        // Focused by clicking only. A controller cannot type, and a text box that is editing swallows the
        // game's keys (Escape included), so directional navigation goes to Save checkpoint instead.
        _label = new LineEdit
        {
            MaxLength = CheckpointInfo.MaxLabelLength,
            CustomMinimumSize = new Vector2(Width - 40, 40),
            FocusMode = FocusModeEnum.Click,
            ClearButtonEnabled = true,
        };
        _label.AddThemeFontSizeOverride("font_size", 17);
        _label.TextSubmitted += _ => Take();
        column.AddChild(_label);

        _save = new SlotButton { Text = "Save checkpoint", Key = "checkpoint", FocusMode = FocusModeEnum.All };
        _save.AddThemeFontSizeOverride("font_size", 17);
        _save.Pressed += Take;
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddChild(_save);
        column.AddChild(line);

        _status = MakeLabel("", 15, Muted);
        column.AddChild(_status);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        _recent = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _recent.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_recent);

        _howBack = MakeLabel("", 13, Muted);
        column.AddChild(_howBack);
    }

    /// <summary>The pause menu is built once per run and shown and hidden; redraw each time it appears.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree())
        {
            // The menu refocuses whatever had focus when it last closed; never the text box, which would
            // reopen editing and take the first Escape.
            if (LastFocused(_menu) == _label)
            {
                LastFocused(_menu) = null;
            }
            Refresh();
        }
    }

    private void Refresh()
    {
        var kind = KindOfRun();
        Visible = kind != null;
        if (kind is not { } k)
        {
            return;
        }

        try
        {
            var run = RunSummary.TryParse(SlotStore.ReadActive(k));
            _where.Text = run == null
                ? "The game has not saved this run yet. It saves on entering each room."
                : $"Saves the moment the game last saved: {CheckpointText.RestorePoint(run)}.";
            _label.PlaceholderText = run == null ? "Label" : CheckpointText.DefaultLabel(run);
            _save.Disabled = run == null;
            _howBack.Text = k == SaveKind.Solo
                ? "To go back to one: Save & Quit, then open Solo runs on the main menu."
                : "To go back to one: Save & Quit, then use Multiplayer runs on the multiplayer menu.";

            foreach (var child in _recent.GetChildren())
            {
                _recent.RemoveChild(child);
                child.QueueFree();
            }
            var mine = run == null ? [] : CheckpointStore.OfRun(k, run.StartTime);
            _recent.AddChild(MakeLabel(mine.Count == 0 ? "THIS RUN'S CHECKPOINTS" : $"THIS RUN'S CHECKPOINTS ({mine.Count})", 14, Gold));
            if (mine.Count == 0)
            {
                _recent.AddChild(MakeLabel("None yet.", 15, Muted));
            }
            foreach (var checkpoint in mine.Take(RecentShown))
            {
                var row = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                row.AddThemeConstantOverride("separation", 0);
                row.AddChild(MakeLabel(checkpoint.Info.Label, 16, Ink));
                row.AddChild(MakeLabel($"{CheckpointText.RestorePoint(checkpoint.Summary!)}  ·  {SlotText.Saved(checkpoint.Summary!.SaveTime)}", 14, Muted));
                _recent.AddChild(row);
            }
            if (mine.Count > RecentShown)
            {
                _recent.AddChild(MakeLabel($"and {mine.Count - RecentShown} more on the menu.", 14, Muted));
            }
        }
        catch (Exception ex)
        {
            ModLog.Error($"Could not show checkpoints: {ex}");
            SetStatus($"Could not read checkpoints: {ex.Message}", Warn);
        }
    }

    private void Take()
    {
        if (KindOfRun() is not { } kind)
        {
            return;
        }
        try
        {
            var checkpoint = CheckpointStore.Take(kind, _label.Text);
            SetStatus($"Saved “{checkpoint.Info.Label}”.", Good);
            _label.Text = "";
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not take a checkpoint: {ex}");
            SetStatus(ex.Message, Warn);
        }
        Refresh();
    }

    /// <summary>
    /// The pause menu points its buttons' left and right at themselves; right now leads to Save
    /// checkpoint, and left from it back to Resume.
    /// </summary>
    private void WireFocus()
    {
        var container = _menu.GetNodeOrNull<Control>("%ButtonContainer");
        var buttons = container?.GetChildren().OfType<Control>().Where(b => b.FocusMode != FocusModeEnum.None).ToList() ?? [];
        foreach (var button in buttons)
        {
            button.FocusNeighborRight = _save.GetPath();
        }
        var self = _save.GetPath();
        _save.FocusNeighborLeft = buttons.FirstOrDefault()?.GetPath() ?? self;
        _save.FocusNeighborRight = self;
        _save.FocusNeighborTop = self;
        _save.FocusNeighborBottom = self;
    }

    private void SetStatus(string text, Color colour)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", colour);
    }

    private static Label MakeLabel(string text, int size, Color colour)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(Width - 40, 0),
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }
}

[HarmonyPatch(typeof(NPauseMenu), nameof(NPauseMenu._Ready))]
public static class AttachCheckpointPanelPatch
{
    private static void Postfix(NPauseMenu __instance)
    {
        try
        {
            CheckpointPanel.AttachTo(__instance);
        }
        catch (Exception ex)
        {
            ModLog.Error($"Could not add the checkpoint panel to the pause menu: {ex}");
        }
    }
}
