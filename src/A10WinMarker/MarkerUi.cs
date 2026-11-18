using System.Reflection;
using A10WinMarker.Core;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace A10WinMarker;

/// <summary>
/// The marker: at the top ascension (10, or 5 in Blitz) the panel's fire icon becomes a "fire
/// trophy": a smaller flame (the icon's own texture and tint shader, so red/blue follow the game)
/// rising from the trophy cup. The game's own level label still draws the number on top.
/// <para>
/// Built in code from game textures only, so the mod stays DLL-only. State is read from the store
/// on every refresh (no cache); a click asks the store to toggle. Marked = full colour, not marked
/// = greyed. Hover uses the game's own tip; the cursor keeps the game's skinned arrow.
/// </para>
/// </summary>
internal static class MarkerUi
{
    private const string NodeName = "A10WinMarker";
    private const string PanelScopeMeta = "a10_win_marker_scope";
    private const string HiddenIconMeta = "a10_win_marker_hid_icon";
    private const string TrophyPath = "res://images/packed/statistics_screen/stats_trophy.png";
    private static readonly string[] IconPaths = { "%AscensionIconStandard", "%AscensionIconBlitz" };

    // Layout of option B, as anchor fractions of the vanilla fire icon's rect (x: left, right;
    // y: top, bottom). Overflow past 0..1 is intended: the trophy is wider than the flame.
    private static readonly Rect2 FlameAnchors = new(0.10f, -0.07f, 0.80f, 0.63f);
    private static readonly Rect2 TrophyAnchors = new(-0.12f, 0.28f, 1.24f, 0.81f);

    private static readonly Color MarkedColour = Colors.White;
    private static readonly Color UnmarkedColour = new(0.35f, 0.35f, 0.35f, 0.85f);

    private static readonly Dictionary<Type, (FieldInfo Lobby, FieldInfo Panel)> Fields = new();
    private static readonly List<WeakReference<Node>> Screens = new();

    // HoverTip has no plain-string constructor (it takes loc keys), so the mod's text is set on a
    // boxed default value through its private setters.
    private static readonly PropertyInfo? TipTitle = AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title));
    private static readonly PropertyInfo? TipDescription = AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description));

    private static Texture2D? _trophy;
    private static bool _trophyTried;

    /// <summary>
    /// Screen hook: resolves the screen's scope from its lobby, remembers it on the panel (the
    /// panel itself does not know the character), and refreshes the panel.
    /// </summary>
    public static void Refresh(object screenObject)
    {
        if (!Patches.MarkerEnabled || screenObject is not Node screen || FieldsFor(screen.GetType()) is not { } fields)
        {
            return;
        }
        Track(screen);
        if (fields.Panel.GetValue(screen) is not NAscensionPanel panel || !GodotObject.IsInstanceValid(panel))
        {
            return;
        }
        var scope = fields.Lobby.GetValue(screen) is StartRunLobby lobby
            ? ScreenScope.Resolve(GameAdapter.FromLobby(lobby))
            : null;
        panel.SetMeta(PanelScopeMeta, scope?.Key ?? "");
        RefreshPanel(panel);
    }

    /// <summary>
    /// Panel hook (level changed) and the end of every screen refresh: shows the fire trophy when
    /// the panel is on its scope's top level, the vanilla fire otherwise.
    /// </summary>
    public static void RefreshPanel(object panelObject)
    {
        if (!Patches.MarkerEnabled || panelObject is not NAscensionPanel panel || !GodotObject.IsInstanceValid(panel))
        {
            return;
        }
        var scope = Scope.FromKey(panel.GetMeta(PanelScopeMeta, "").AsString());
        var icon = ActiveIcon(panel);
        var overlay = icon?.GetParent()?.GetNodeOrNull<Control>(NodeName);

        var show = scope is not null
            && icon is not null
            && panel.Ascension == GameAdapter.Caps.For(scope)
            && Trophy() is not null;
        if (!show)
        {
            if (overlay is not null)
            {
                HideTip(overlay);
                overlay.Visible = false;
            }
            RestoreIcons(panel);
            return;
        }

        overlay ??= Create(icon!);
        Sync(overlay, icon!, panel);
        Show(overlay, scope!, ReadMarked(scope!));
    }

    /// <summary>Re-runs <see cref="Refresh"/> on every open character-select or Blitz screen.</summary>
    public static void RefreshOpenScreens()
    {
        foreach (var screen in LiveScreens())
        {
            if (screen.IsInsideTree())
            {
                Refresh(screen);
            }
        }
    }

    private static TextureRect? ActiveIcon(Control panel) =>
        IconPaths.Select(p => panel.GetNodeOrNull<TextureRect>(p)).FirstOrDefault(i => i is not null && i.Visible);

    private static Control Create(TextureRect icon)
    {
        var overlay = new Control
        {
            Name = NodeName,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None,
            // Default (Arrow) cursor: the game skins Arrow; other shapes fall back to the OS cursor.
        };
        overlay.AddChild(Layer("Flame", FlameAnchors));
        overlay.AddChild(Layer("Trophy", TrophyAnchors));

        // Straight after the icon: above the fire, below the game's level label.
        var parent = icon.GetParent();
        parent.AddChild(overlay);
        parent.MoveChild(overlay, icon.GetIndex() + 1);

        overlay.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(e => OnInput(overlay, e)));
        overlay.Connect(Control.SignalName.MouseEntered, Callable.From(() => ShowTip(overlay)));
        overlay.Connect(Control.SignalName.MouseExited, Callable.From(() => HideTip(overlay)));
        return overlay;
    }

    private static TextureRect Layer(string name, Rect2 anchors) => new()
    {
        Name = name,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        AnchorLeft = anchors.Position.X,
        AnchorTop = anchors.Position.Y,
        AnchorRight = anchors.End.X,
        AnchorBottom = anchors.End.Y,
    };

    /// <summary>
    /// Mirrors the active icon's layout, texture and tint shader, and hides the vanilla fire
    /// (through <c>SelfModulate</c>, which the game never touches, so its own Visible toggles are
    /// left alone).
    /// </summary>
    private static void Sync(Control overlay, TextureRect icon, Control panel)
    {
        overlay.LayoutMode = icon.LayoutMode;
        overlay.AnchorLeft = icon.AnchorLeft;
        overlay.AnchorTop = icon.AnchorTop;
        overlay.AnchorRight = icon.AnchorRight;
        overlay.AnchorBottom = icon.AnchorBottom;
        overlay.OffsetLeft = icon.OffsetLeft;
        overlay.OffsetTop = icon.OffsetTop;
        overlay.OffsetRight = icon.OffsetRight;
        overlay.OffsetBottom = icon.OffsetBottom;
        overlay.CustomMinimumSize = icon.CustomMinimumSize;
        overlay.SizeFlagsHorizontal = icon.SizeFlagsHorizontal;
        overlay.SizeFlagsVertical = icon.SizeFlagsVertical;

        var flame = overlay.GetNode<TextureRect>("Flame");
        flame.Texture = icon.Texture;
        flame.Material = icon.Material; // the game's HSV shader: red, or blue for a host
        overlay.GetNode<TextureRect>("Trophy").Texture = Trophy();

        RestoreIcons(panel, except: icon);
        if (!icon.HasMeta(HiddenIconMeta))
        {
            icon.SetMeta(HiddenIconMeta, icon.SelfModulate);
            icon.SelfModulate = Colors.Transparent;
        }
    }

    private static void RestoreIcons(Control panel, TextureRect? except = null)
    {
        foreach (var path in IconPaths)
        {
            if (panel.GetNodeOrNull<TextureRect>(path) is { } icon && icon != except && icon.HasMeta(HiddenIconMeta))
            {
                icon.SelfModulate = icon.GetMeta(HiddenIconMeta).AsColor();
                icon.RemoveMeta(HiddenIconMeta);
            }
        }
    }

    private static void Show(Control overlay, Scope scope, bool marked)
    {
        overlay.SetMeta(PanelScopeMeta, scope.Key);
        overlay.Modulate = marked ? MarkedColour : UnmarkedColour;
        overlay.Visible = true;
        if (overlay.GetGlobalRect().HasPoint(overlay.GetGlobalMousePosition()))
        {
            ShowTip(overlay); // state changed under the pointer: replace the open tip
        }
    }

    private static void OnInput(Control overlay, InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            return;
        }
        overlay.AcceptEvent();
        try
        {
            if (!GodotObject.IsInstanceValid(overlay) || ScopeOf(overlay) is not { } scope)
            {
                return;
            }
            var marked = GameAdapter.Store.Toggle(scope);
            Log.Info($"{scope.Key} {(marked ? "marked" : "unmarked")} by click");
            Show(overlay, scope, marked);
        }
        catch (Exception ex)
        {
            Log.Error($"Toggling the mark failed: {ex}");
            RefreshOpenScreens(); // show whatever is actually stored
        }
    }

    private static void ShowTip(Control overlay)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(overlay) || !overlay.Visible || ScopeOf(overlay) is not { } scope)
            {
                return;
            }
            var text = MarkerText.Tooltip(scope, ReadMarked(scope), GameAdapter.Caps);
            if (BuildTip(text) is { } tip)
            {
                NHoverTipSet.CreateAndShow(overlay, tip, HoverTip.GetHoverTipAlignment(overlay));
            }
        }
        catch (Exception e)
        {
            Log.Error($"Showing the hover tip failed: {e.Message}");
        }
    }

    private static void HideTip(Control overlay)
    {
        try
        {
            NHoverTipSet.Remove(overlay);
        }
        catch (Exception e)
        {
            Log.Error($"Removing the hover tip failed: {e.Message}");
        }
    }

    private static IHoverTip? BuildTip(TooltipText text)
    {
        if (TipTitle is null || TipDescription is null)
        {
            Log.Error("HoverTip.Title/Description not found; no hover tip.");
            return null;
        }
        object tip = default(HoverTip); // boxed so the setters change this copy
        TipTitle.SetValue(tip, text.Title);
        TipDescription.SetValue(tip, text.Description);
        return (IHoverTip)tip;
    }

    private static Scope? ScopeOf(Control overlay) =>
        Scope.FromKey(overlay.GetMeta(PanelScopeMeta, "").AsString());

    private static bool ReadMarked(Scope scope)
    {
        try
        {
            return GameAdapter.Store.Read().IsMarked(scope);
        }
        catch (Exception e)
        {
            Log.Error($"Reading marks failed; showing {scope.Key} as not marked: {e.Message}");
            return false;
        }
    }

    private static Texture2D? Trophy()
    {
        if (!_trophyTried)
        {
            _trophyTried = true;
            try
            {
                _trophy = ResourceLoader.Load<Texture2D>(TrophyPath);
            }
            catch (Exception e)
            {
                Log.Error($"Loading {TrophyPath} threw: {e.Message}");
            }
            if (_trophy is null)
            {
                Log.Error($"Could not load {TrophyPath}; the vanilla fire stays.");
            }
        }
        return _trophy;
    }

    private static (FieldInfo Lobby, FieldInfo Panel)? FieldsFor(Type type)
    {
        if (Fields.TryGetValue(type, out var cached))
        {
            return cached;
        }
        var screen = Compat.Screens.FirstOrDefault(s => s.IsAssignableFrom(type));
        if (screen is null || Compat.LobbyField(screen) is not { } lobby || Compat.PanelField(screen) is not { } panel)
        {
            return null;
        }
        return Fields[type] = (lobby, panel);
    }

    private static void Track(Node screen)
    {
        if (!LiveScreens().Contains(screen))
        {
            Screens.Add(new WeakReference<Node>(screen));
        }
    }

    /// <summary>Tracked screens that still exist; drops the rest.</summary>
    private static List<Node> LiveScreens()
    {
        var live = new List<Node>();
        Screens.RemoveAll(r => !r.TryGetTarget(out var n) || !GodotObject.IsInstanceValid(n));
        foreach (var r in Screens)
        {
            if (r.TryGetTarget(out var n))
            {
                live.Add(n);
            }
        }
        return live;
    }
}
