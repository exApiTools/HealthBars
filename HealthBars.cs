using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Cache;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace HealthBars;

public class HealthBars : BaseSettingsPlugin<HealthBarsSettings>
{
    private const string ShadedHealthbarTexture = "healthbar.png";
    private const string FlatHealthbarTexture = "chest.png";
    private static readonly string IgnoreFile = Path.Combine("config", "ignored_entities.txt");
    private List<string> IgnoredEntities { get; set; } = new();

    private bool _canTick = true;
    private Camera Camera => GameController.IngameState.Camera;
    private IngameUIElements IngameUi => GameController.IngameState.IngameUi;
    private Size2F WindowRelativeSize => new Size2F(_windowRectangle.Value.Width / 2560, _windowRectangle.Value.Height / 1600);
    private string HealthbarTexture => Settings.UseShadedTexture ? ShadedHealthbarTexture : FlatHealthbarTexture;

    private Vector2 _oldPlayerCoord;
    private HealthBar _playerBar;
    private CachedValue<bool> _ingameUiCheckVisible;
    private CachedValue<RectangleF> _windowRectangle;

    public override void OnLoad()
    {
        CanUseMultiThreading = true;
        Graphics.InitImage(HealthbarTexture);
        Graphics.InitImage(FlatHealthbarTexture);
    }

    public override bool Initialise()
    {
        _windowRectangle = new TimeCache<RectangleF>(() =>
            GameController.Window.GetWindowRectangleReal() with { Location = SharpDX.Vector2.Zero }, 250);
        _ingameUiCheckVisible = new TimeCache<bool>(() =>
            IngameUi.SyndicatePanel.IsVisibleLocal ||
            IngameUi.SellWindow.IsVisibleLocal ||
            IngameUi.DelveWindow.IsVisibleLocal ||
            IngameUi.IncursionWindow.IsVisibleLocal ||
            IngameUi.UnveilWindow.IsVisibleLocal ||
            IngameUi.TreePanel.IsVisibleLocal ||
            IngameUi.Atlas.IsVisibleLocal ||
            IngameUi.CraftBench.IsVisibleLocal, 250);
        ReadIgnoreFile();
        Settings.PlayerZOffset.OnValueChanged += (_, _) => _oldPlayerCoord = Vector2.Zero;
        Settings.PlacePlayerBarRelativeToGroundLevel.OnValueChanged += (_, _) => _oldPlayerCoord = Vector2.Zero;
        Settings.EnableAbsolutePlayerBarPositioning.OnValueChanged += (_, _) => _oldPlayerCoord = Vector2.Zero;
        return true;
    }

    private void ReadIgnoreFile()
    {
        var path = Path.Combine(DirectoryFullName, IgnoreFile);
        if (File.Exists(path))
        {
            IgnoredEntities = File.ReadAllLines(path)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(line => !line.StartsWith("#"))
                .ToList();
        }
        else
        {
            IgnoredEntities = new List<string>();
            LogError($"Ignored entities file does not exist. Path: {path}");
        }
    }

    public override void AreaChange(AreaInstance area)
    {
        _oldPlayerCoord = Vector2.Zero;
        ReadIgnoreFile();
    }

    private bool SkipHealthBar(HealthBar healthBar)
    {
        if (healthBar.Settings?.Show != true) return true;
        if (healthBar.Distance > Settings.DrawDistanceLimit) return true;
        if (healthBar.Life == null) return true;
        if (!healthBar.Entity.IsAlive) return true;
        if (healthBar.HpPercent < 0.001f) return true;
        if (healthBar.Type == CreatureType.Minion && healthBar.HpPercent * 100 > Settings.ShowMinionOnlyWhenBelowHp) return true;
/*            if (healthBar.Entity.League == LeagueType.Legion && healthBar.Entity.IsHidden 
                && healthBar.Entity.Rarity != MonsterRarity.Unique 
                && healthBar.Entity.Rarity != MonsterRarity.Rare) return true;*/

        return false;
    }

    private void HpBarWork(HealthBar healthBar)
    {
        healthBar.Skip = SkipHealthBar(healthBar);
        if (healthBar.Skip) return;

        healthBar.CheckUpdate();

        var worldCoords = healthBar.Entity.PosNum;
        if (!Settings.PlaceBarRelativeToGroundLevel)
        {
            if (healthBar.Entity.GetComponent<Render>()?.BoundsNum is { } boundsNum)
            {
                worldCoords.Z -= 2 * boundsNum.Z;
            }
        }

        worldCoords.Z += Settings.GlobalZOffset;
        var mobScreenCoords = Camera.WorldToScreen(worldCoords);
        if (mobScreenCoords == Vector2.Zero) return;
        mobScreenCoords = Vector2.Lerp(mobScreenCoords, healthBar.LastPosition, healthBar.LastPosition == Vector2.Zero ? 0 : Math.Clamp(Settings.SmoothingFactor, 0, 1));
        healthBar.LastPosition = mobScreenCoords;
        var scaledWidth = healthBar.Settings.Width * WindowRelativeSize.Width;
        var scaledHeight = healthBar.Settings.Height * WindowRelativeSize.Height;

        healthBar.DisplayArea = new RectangleF(mobScreenCoords.X - scaledWidth / 2f, mobScreenCoords.Y - scaledHeight / 2f, scaledWidth,
            scaledHeight);

        if (healthBar.Distance > 80 && !_windowRectangle.Value.Intersects(healthBar.DisplayArea))
        {
            healthBar.Skip = true;
        }
    }

    public override Job Tick()
    {
        _canTick = true;

        if (!Settings.IgnoreUiElementVisibility && _ingameUiCheckVisible?.Value == true ||
            Camera == null ||
            !Settings.ShowInTown && GameController.Area.CurrentArea.IsTown ||
            !Settings.ShowInHideout && GameController.Area.CurrentArea.IsHideout)
        {
            _canTick = false;
            return null;
        }

        if (Settings.MultiThreading && GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster].Count >=
            Settings.MinimumEntitiesForMultithreading)
        {
            return new Job(nameof(HealthBars), TickLogic);
        }

        TickLogic();
        return null;
    }

    private void TickLogic()
    {
        foreach (var validEntity in GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                     .Concat(GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]))
        {
            var healthBar = validEntity.GetHudComponent<HealthBar>();
            if (healthBar == null) continue;

            try
            {
                HpBarWork(healthBar);
            }
            catch (Exception e)
            {
                DebugWindow.LogError(e.Message);
            }
        }

        PositionPlayerBar();
    }

    private void PositionPlayerBar()
    {
        if (!Settings.Self.Show || _playerBar is not { } playerBar)
        {
            return;
        }

        var worldCoords = playerBar.Entity.PosNum;
        if (!Settings.PlacePlayerBarRelativeToGroundLevel)
        {
            if (playerBar.Entity.GetComponent<Render>()?.BoundsNum is { } boundsNum)
            {
                worldCoords.Z -= 2 * boundsNum.Z;
            }
        }

        worldCoords.Z += Settings.PlayerZOffset;
        var result = Camera.WorldToScreen(worldCoords);

        if (Settings.EnableAbsolutePlayerBarPositioning)
        {
            _oldPlayerCoord = result = Settings.PlayerBarPosition;
        }
        else
        {
            if (_oldPlayerCoord == Vector2.Zero)
            {
                _oldPlayerCoord = result;
            }
            else if (Settings.PlayerSmoothingFactor >= 1)
            {
                if ((_oldPlayerCoord - result).LengthSquared() < 40 * 40)
                    result = _oldPlayerCoord;
                else
                    _oldPlayerCoord = result;
            }
            else
            {
                result = Vector2.Lerp(result, _oldPlayerCoord, _oldPlayerCoord == Vector2.Zero ? 0 : Math.Max(0, Settings.PlayerSmoothingFactor));
                _oldPlayerCoord = result;
            }
        }

        var scaledWidth = playerBar.Settings.Width * WindowRelativeSize.Width;
        var scaledHeight = playerBar.Settings.Height * WindowRelativeSize.Height;

        var background = new RectangleF(result.X, result.Y, 0, 0);
        background.Inflate(scaledWidth / 2f, scaledHeight / 2f);
        playerBar.DisplayArea = background;
    }

    public override void Render()
    {
        if (!_canTick) return;

        foreach (var entity in GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                     .Concat(GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]))
        {
            if (entity.GetHudComponent<HealthBar>() is { Skip: false } healthBar)
            {
                DrawBar(healthBar);
            }
        }
    }

    private void DrawBar(HealthBar bar)
    {
        var barText = GetTemplatedText(bar);
        var barArea = bar.DisplayArea;

        if (barText != null && Settings.ResizeBarsToFitText)
        {
            var barTextSize = Graphics.MeasureText(barText);
            barArea.Inflate(Math.Max(0, (barTextSize.X - barArea.Width) / 2), Math.Max(0, (barTextSize.Y - barArea.Height) / 2));
        }

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        var alphaMulti = bar.Settings.HoverOpacity != 1
                         && ImGui.IsMouseHoveringRect(barArea.TopLeft.ToVector2Num(), barArea.BottomRight.ToVector2Num(), false)
            ? bar.Settings.HoverOpacity
            : 1f;
        if (alphaMulti == 0)
        {
            return;
        }

        Graphics.DrawImage(HealthbarTexture, barArea, bar.Settings.BackgroundColor.MultiplyAlpha(alphaMulti));
        if (!Settings.CombineLifeAndEs)
        {
            var hpWidth = barArea.Width * bar.HpPercent;
            var esWidth = barArea.Width * bar.EsPercent;
            Graphics.DrawImage(HealthbarTexture, barArea with { Width = hpWidth }, bar.Color.MultiplyAlpha(alphaMulti));
            Graphics.DrawImage(HealthbarTexture, new RectangleF(barArea.X, barArea.Y, esWidth, barArea.Height * Settings.EsBarHeight),
                bar.Settings.EsColor.MultiplyAlpha(alphaMulti));
        }
        else
        {
            var totalLifePool = (float)(bar.Life.MaxHP + bar.Life.MaxES);
            var fullHpWidthFraction = bar.Life.MaxHP / totalLifePool;
            var fullEsWidthFraction = bar.Life.MaxES / totalLifePool;
            var hpWidthFraction = fullHpWidthFraction * bar.HpPercent;
            var esWidthFraction = fullEsWidthFraction * bar.EsPercent;
            var hpWidth = hpWidthFraction * barArea.Width;
            var esWidth = esWidthFraction * barArea.Width;
            Graphics.DrawImage(HealthbarTexture, barArea with { Width = hpWidth }, bar.Color.MultiplyAlpha(alphaMulti));
            Graphics.DrawImage(HealthbarTexture, new RectangleF(barArea.X + hpWidth, barArea.Y, esWidth, barArea.Height), bar.Settings.EsColor.MultiplyAlpha(alphaMulti));
        }

        var segmentCount = bar.Settings.HealthSegments.Value;
        for (int i = 1; i < segmentCount; i++)
        {
            var x = i / (float)segmentCount * barArea.Width;
            var notchRect = new RectangleF(
                barArea.X + x,
                barArea.Bottom - barArea.Height * bar.Settings.HealthSegmentHeight,
                1,
                barArea.Height * bar.Settings.HealthSegmentHeight);
            Graphics.DrawImage(FlatHealthbarTexture, notchRect, bar.Settings.HealthSegmentColor.MultiplyAlpha(alphaMulti));
        }

        if (bar.Settings.OutlineThickness > 0 && bar.Settings.OutlineColor.Value.A > 0)
        {
            var outlineRect = barArea;
            outlineRect.Inflate(1, 1);
            Graphics.DrawFrame(outlineRect, bar.Settings.OutlineColor.MultiplyAlpha(alphaMulti), bar.Settings.OutlineThickness.Value);
        }

        ShowNumbersInHealthbar(bar, barText, alphaMulti);
        if (bar.Settings.ShowDps)
        {
            ShowDps(bar, alphaMulti);
        }
    }

    private void ShowDps(HealthBar bar, float alphaMulti)
    {
        const int margin = 2;
        if (bar.EhpHistory.Count < 2) return;
        var hpFirst = bar.EhpHistory.First();
        var hpLast = bar.EhpHistory.Last();

        var timeDiff = hpLast.Time - hpFirst.Time;
        var hpDiff = hpFirst.Value - hpLast.Value;

        var dps = hpDiff / timeDiff.TotalSeconds;
        if (dps == 0)
        {
            return;
        }

        var damageColor = dps < 0
            ? Settings.CombatHealColor
            : Settings.CombatDamageColor;

        var dpsText = dps.FormatHp();
        var textArea = Graphics.MeasureText(dpsText);
        var textCenter = new Vector2(bar.DisplayArea.Center.X, bar.DisplayArea.Bottom + textArea.Y / 2 + margin);
        Graphics.DrawBox(textCenter - textArea / 2, textCenter + textArea / 2, bar.Settings.TextBackground.MultiplyAlpha(alphaMulti));
        Graphics.DrawText(dpsText, textCenter - textArea / 2, damageColor.MultiplyAlpha(alphaMulti));
    }

    private void ShowNumbersInHealthbar(HealthBar bar, string text, float alphaMulti)
    {
        if (text != null)
        {
            var textArea = Graphics.MeasureText(text);
            var barCenter = bar.DisplayArea.Center.ToVector2Num();
            var textOffset = bar.Settings.TextPosition.Value.Mult(bar.DisplayArea.Width + textArea.X, bar.DisplayArea.Height + textArea.Y) / 2;
            var textCenter = barCenter + textOffset;
            var textTopLeft = textCenter - textArea / 2;
            var textRect = new RectangleF(textTopLeft.X, textTopLeft.Y, textArea.X, textArea.Y);
            bar.DisplayArea.Contains(ref textRect, out var textIsInsideBar);
            if (!textIsInsideBar)
            {
                Graphics.DrawBox(textTopLeft, textTopLeft + textArea, bar.Settings.TextBackground.MultiplyAlpha(alphaMulti));
            }

            Graphics.DrawText(text, textTopLeft, bar.Settings.TextColor.MultiplyAlpha(alphaMulti));
        }
    }

    private static string GetTemplatedText(HealthBar bar)
    {
        var textFormat = bar.Settings.TextFormat.Value;
        if (string.IsNullOrWhiteSpace(textFormat))
        {
            return null;
        }

        return textFormat
            .Replace("{percent}", Math.Floor(bar.EhpPercent * 100).ToString(CultureInfo.InvariantCulture))
            .Replace("{current}", bar.CurrentEhp.FormatHp())
            .Replace("{total}", bar.MaxEhp.FormatHp())
            .Replace("{currentes}", bar.Life.CurES.FormatHp())
            .Replace("{currentlife}", bar.Life.CurHP.FormatHp());
    }

    public override void EntityAdded(Entity entity)
    {
        if (entity.Type != EntityType.Monster && entity.Type != EntityType.Player ||
            entity.GetComponent<Life>() != null && !entity.IsAlive ||
            IgnoredEntities.Any(x => entity.Path.StartsWith(x)))
        {
            return;
        }

        var healthBar = new HealthBar(entity, Settings);
        entity.SetHudComponent(healthBar);
        if (entity.Address == GameController.Player.Address)
        {
            _playerBar = healthBar;
        }
    }
}