﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Cache;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using Newtonsoft.Json;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace HealthBars;

public class HealthBars : BaseSettingsPlugin<HealthBarsSettings>
{
    private const string ShadedHealthbarTexture = "healthbar.png";
    private const string FlatHealthbarTexture = "chest.png";
    private string OldConfigPath => Path.Combine(DirectoryFullName, "config", "ignored_entities.txt");
    private string NewConfigCustomPath => Path.Join(ConfigDirectory, "entityConfig.json");

    private Camera Camera => GameController.IngameState.Camera;
    private IngameUIElements IngameUi => GameController.IngameState.IngameUi;
    private Size2F WindowRelativeSize => new Size2F(_windowRectangle.Value.Width / 2560, _windowRectangle.Value.Height / 1600);
    private string HealthbarTexture => Settings.UseShadedTexture ? ShadedHealthbarTexture : FlatHealthbarTexture;

    private readonly ConcurrentDictionary<string, EntityTreatmentRule> _pathRuleCache = new();
    private bool _canTick = true;
    private IndividualEntityConfig _entityConfig = new IndividualEntityConfig(new SerializedIndividualEntityConfig());
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
            IngameUi.FullscreenPanels.Any(x => x.IsVisibleLocal) ||
            IngameUi.LargePanels.Any(x => x.IsVisibleLocal), 250);
        LoadConfig();
        Settings.PlayerZOffset.OnValueChanged += (_, _) => _oldPlayerCoord = Vector2.Zero;
        Settings.PlacePlayerBarRelativeToGroundLevel.OnValueChanged += (_, _) => _oldPlayerCoord = Vector2.Zero;
        Settings.EnableAbsolutePlayerBarPositioning.OnValueChanged += (_, _) => _oldPlayerCoord = Vector2.Zero;
        Settings.ExportDefaultConfig.OnPressed += () => { File.WriteAllText(NewConfigCustomPath, GetEmbeddedConfigString()); };
        return true;
    }

    private void LoadConfig()
    {
        _pathRuleCache.Clear();
        if (Settings.UseOldConfigFormat)
        {
            LoadOldEntityConfigFormat();
        }
        else
        {
            if (File.Exists(NewConfigCustomPath))
            {
                try
                {
                    var content = File.ReadAllText(NewConfigCustomPath);
                    _entityConfig = new IndividualEntityConfig(JsonConvert.DeserializeObject<SerializedIndividualEntityConfig>(content));
                    return;
                }
                catch (Exception ex)
                {
                    DebugWindow.LogError($"Unable to load custom config file, falling back to default: {ex}");
                }
            }

            _entityConfig = LoadEmbeddedConfig();
        }
    }

    private static IndividualEntityConfig LoadEmbeddedConfig()
    {
        var content = GetEmbeddedConfigString();
        return new IndividualEntityConfig(JsonConvert.DeserializeObject<SerializedIndividualEntityConfig>(content));
    }

    private static string GetEmbeddedConfigString()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("entityConfig.default.json");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        return content;
    }

    private void LoadOldEntityConfigFormat()
    {
        if (File.Exists(OldConfigPath))
        {
            var ignoredEntities = File.ReadAllLines(OldConfigPath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(line => !line.StartsWith("#"))
                .ToList();
            _entityConfig = new IndividualEntityConfig(new SerializedIndividualEntityConfig
            {
                EntityPathConfig = ignoredEntities.ToDictionary(
                    x => $"^{Regex.Escape(x)}",
                    _ => new EntityTreatmentRule { Ignore = true }),
            });
        }
        else
        {
            _entityConfig = new IndividualEntityConfig(new SerializedIndividualEntityConfig());
            LogError($"Ignored entities file does not exist. Path: {OldConfigPath}");
        }
    }


    public override void AreaChange(AreaInstance area)
    {
        _oldPlayerCoord = Vector2.Zero;
        LoadConfig();
    }

    private bool SkipHealthBar(HealthBar healthBar, bool checkDistance)
    {
        if (healthBar.Settings?.Show != true) return true;
        if (checkDistance && healthBar.Distance > Settings.DrawDistanceLimit) return true;
        if (healthBar.Life == null) return true;
        if (!healthBar.Entity.IsAlive) return true;
        if (healthBar.HpPercent < 0.001f) return true;
        if (healthBar.Type == CreatureType.Minion && healthBar.HpPercent * 100 > Settings.ShowMinionOnlyWhenBelowHp) return true;
        if (healthBar.Entity.League == LeagueType.Legion &&
            healthBar.Entity.IsHidden &&
            (healthBar.Entity.Rarity == MonsterRarity.Unique && !Settings.LegionSettings.ShowHiddenUniqueMonsters ||
             healthBar.Entity.Rarity == MonsterRarity.Rare && !Settings.LegionSettings.ShowHiddenRareMonsters ||
             healthBar.Entity.Rarity == MonsterRarity.Magic && !Settings.LegionSettings.ShowHiddenNormalAndMagicMonsters ||
             healthBar.Entity.Rarity == MonsterRarity.White && !Settings.LegionSettings.ShowHiddenNormalAndMagicMonsters))
            return true;

        return false;
    }

    private void HpBarWork(HealthBar healthBar)
    {
        healthBar.Skip = SkipHealthBar(healthBar, true);
        if (healthBar.Skip && !ShowInBossOverlay(healthBar)) return;

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
        var bossOverlayItems = new List<HealthBar>();
        foreach (var entity in GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                     .Concat(GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]))
        {
            if (entity.GetHudComponent<HealthBar>() is not { } healthBar)
            {
                continue;
            }

            if (!healthBar.Skip)
            {
                try
                {
                    DrawBar(healthBar);
                    if (IsCastBarEnabled(healthBar))
                    {
                        var lifeArea = healthBar.DisplayArea;
                        DrawCastBar(healthBar,
                            lifeArea with
                            {
                                Y = lifeArea.Y + lifeArea.Height * (healthBar.Settings.CastBarSettings.YOffset + 1),
                                Height = healthBar.Settings.CastBarSettings.Height,
                            }, healthBar.Settings.CastBarSettings.ShowStageNames,
                            Settings.CommonCastBarSettings.ShowNextStageName,
                            Settings.CommonCastBarSettings.MaxSkillNameLength);
                    }
                }
                catch (Exception ex)
                {
                    DebugWindow.LogError(ex.ToString());
                }
            }

            if (ShowInBossOverlay(healthBar) && !SkipHealthBar(healthBar, false))
            {
                bossOverlayItems.Add(healthBar);
            }
        }

        bossOverlayItems.Sort((x, y) => x.StableId.CompareTo(y.StableId));
        DrawBossOverlay(bossOverlayItems);
    }

    private void DrawBossOverlay(IEnumerable<HealthBar> items)
    {
        if (!Settings.BossOverlaySettings.Show)
        {
            return;
        }

        var barPosition = Settings.BossOverlaySettings.Location.Value;
        foreach (var healthBar in items.Take(Settings.BossOverlaySettings.MaxEntries))
        {
            try
            {
                var lifeRect = new RectangleF(barPosition.X, barPosition.Y, Settings.BossOverlaySettings.Width, Settings.BossOverlaySettings.BarHeight);
                DrawBar(healthBar, lifeRect, false, false, Settings.BossOverlaySettings.ShowMonsterNames ? healthBar.Entity.RenderName : null);
                barPosition.Y += lifeRect.Height;
                if (IsCastBarEnabled(healthBar))
                {
                    DrawCastBar(healthBar, lifeRect with { Y = lifeRect.Bottom },
                        Settings.BossOverlaySettings.ShowCastBarStageNames,
                        Settings.CommonCastBarSettings.ShowNextStageNameInBossOverlay,
                        Settings.CommonCastBarSettings.MaxSkillNameLengthForBossOverlay);
                    barPosition.Y += lifeRect.Height;
                }
            }
            catch (Exception ex)
            {
                DebugWindow.LogError(ex.ToString());
            }

            barPosition.Y += Settings.BossOverlaySettings.ItemSpacing;
        }
    }

    private void DrawBar(HealthBar bar)
    {
        var enableResizing = Settings.ResizeBarsToFitText;
        var showDps = bar.Settings.ShowDps;
        DrawBar(bar, bar.DisplayArea, enableResizing, showDps, null);
    }

    private void DrawBar(HealthBar bar, RectangleF barArea, bool enableResizing, bool showDps, string textPrefix)
    {
        var barText = $"{textPrefix} {GetTemplatedText(bar)}";
        barText = string.IsNullOrWhiteSpace(barText) ? null : barText.Trim();

        if (barText != null && enableResizing)
        {
            var barTextSize = Graphics.MeasureText(barText);
            barArea.Inflate(Math.Max(0, (barTextSize.X - barArea.Width) / 2), Math.Max(0, (barTextSize.Y - barArea.Height) / 2));
        }

        var alphaMulti = GetAlphaMulti(bar, barArea);
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

        ShowHealthbarText(bar, barText, alphaMulti, barArea);
        if (showDps)
        {
            ShowDps(bar, alphaMulti, barArea);
        }
    }

    private static float GetAlphaMulti(HealthBar bar, RectangleF barArea)
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        var alphaMulti = bar.Settings.HoverOpacity != 1
                         && ImGui.IsMouseHoveringRect(barArea.TopLeft.ToVector2Num(), barArea.BottomRight.ToVector2Num(), false)
            ? bar.Settings.HoverOpacity
            : 1f;
        return alphaMulti;
    }

    private void ShowDps(HealthBar bar, float alphaMulti, RectangleF area)
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
        var textCenter = new Vector2(area.Center.X, area.Bottom + textArea.Y / 2 + margin);
        Graphics.DrawBox(textCenter - textArea / 2, textCenter + textArea / 2, bar.Settings.TextBackground.MultiplyAlpha(alphaMulti));
        Graphics.DrawText(dpsText, textCenter - textArea / 2, damageColor.MultiplyAlpha(alphaMulti));
    }

    private void ShowHealthbarText(HealthBar bar, string text, float alphaMulti, RectangleF area)
    {
        if (text != null)
        {
            var textArea = Graphics.MeasureText(text);
            var barCenter = area.Center.ToVector2Num();
            var textOffset = bar.Settings.TextPosition.Value.Mult(area.Width + textArea.X, area.Height + textArea.Y) / 2;
            var textCenter = barCenter + textOffset;
            var textTopLeft = textCenter - textArea / 2;
            var textRect = new RectangleF(textTopLeft.X, textTopLeft.Y, textArea.X, textArea.Y);
            area.Contains(ref textRect, out var textIsInsideBar);
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

    private static readonly HashSet<string> DangerousStages = new HashSet<string>
    {
        "contact",
        "slam",
        "teleport",
        "small_beam_blast",
        "medium_beam_blast",
        "large_beam_blast",
        "clone_beam_blast",
        "beam_l",
        "beam_r",
        "clap",
        "stab",
        "slash",
        "ice_shard",
        "wind_force",
        "wave",
    };

    private void DrawCastBar(HealthBar bar, RectangleF area, bool drawStageNames, bool showNextStageName, int maxSkillNameLength)
    {
        if (!bar.Entity.TryGetComponent<Actor>(out var actor))
        {
            return;
        }

        if (actor?.AnimationController is not { } ac || actor.Action != ActionFlags.UsingAbility || ac.RawAnimationSpeed == 0)
        {
            return;
        }

        var stages = ac.CurrentAnimation.AllStages.ToList();
        var settings = bar.Settings.CastBarSettings;
        var maxRawProgress = Settings.CommonCastBarSettings.CutOffBackswing
            ? stages.LastOrDefault(x => DangerousStages.Contains(x.StageNameSafe()))?.StageStart ?? ac.MaxRawAnimationProgress
            : ac.MaxRawAnimationProgress;
        if (ac.RawAnimationProgress > maxRawProgress)
        {
            return;
        }

        var alphaMulti = GetAlphaMulti(bar, area);
        if (alphaMulti == 0)
        {
            return;
        }

        var width = area.Width;
        var height = area.Height;
        var maxProgress = ac.TransformProgress(maxRawProgress);
        var topLeft = area.TopLeft.ToVector2Num();
        var bottomRight = topLeft + new Vector2(width, height);
        Graphics.DrawBox(topLeft, bottomRight, settings.BackgroundColor.MultiplyAlpha(alphaMulti));
        Graphics.DrawBox(topLeft, topLeft + new Vector2(width * ac.TransformedRawAnimationProgress / maxProgress, height), settings.FillColor.MultiplyAlpha(alphaMulti));

        var nextDangerousStage = stages.FirstOrDefault(x => x.StageStart > ac.RawAnimationProgress && DangerousStages.Contains(x.StageNameSafe()));
        var stageIn = nextDangerousStage != null
            ? (ac.TransformProgress(nextDangerousStage.StageStart) - ac.TransformedRawAnimationProgress) / ac.AnimationSpeed
            : ac.AnimationCompletesIn.TotalSeconds;
        var mainText = (nextDangerousStage != null && showNextStageName, maxSkillNameLength) switch
        {
            (true, <= 0) => $"{nextDangerousStage?.StageNameSafe()} in {stageIn:F1}",
            (false, <= 0) => $"{stageIn:F1}",
            (true, var v and > 0) => $"{actor.CurrentAction?.Skill?.Name?.Truncate(v)} {nextDangerousStage?.StageNameSafe()} in {stageIn:F1}",
            (false, var v and > 0) => $"{actor.CurrentAction?.Skill?.Name?.Truncate(v)} in {stageIn:F1}",
        };
        var oldTextSize = Graphics.MeasureText(mainText);
        using (Graphics.SetTextScale(Math.Min(height / oldTextSize.Y, width / oldTextSize.X)))
        {
            var color = (nextDangerousStage != null ? settings.DangerTextColor : settings.NoDangerTextColor).MultiplyAlpha(alphaMulti);
            Graphics.DrawText(mainText, topLeft, color);
        }

        var occupiedSlots = new Dictionary<int, float>();
        var textLineHeight = Graphics.MeasureText("A").Y;
        var displayAllSkillStages = Settings.CommonCastBarSettings.DebugShowAllSkillStages;
        foreach (var stage in stages.Where(x => displayAllSkillStages || DangerousStages.Contains(x.StageNameSafe())))
        {
            var normalizedStageStart = ac.TransformProgress(stage.StageStart) / maxProgress;
            if (ReferenceEquals(stage, nextDangerousStage) && Math.Abs(normalizedStageStart - 1) < 1e-3)
            {
                continue;
            }

            var stageX = topLeft.X + normalizedStageStart * width;
            if (drawStageNames)
            {
                var line = Enumerable.Range(0, 100).FirstOrDefault(x => occupiedSlots.GetValueOrDefault(x, float.NegativeInfinity) < stageX);
                var text = displayAllSkillStages ? $"{normalizedStageStart}:{stage.StageNameSafe()}" : $"{stage.StageNameSafe()}";
                var textSize = Graphics.MeasureText(text);
                occupiedSlots[line] = stageX + textSize.X + 20;
                var textStart = new Vector2(stageX, topLeft.Y + height + line * textLineHeight);
                Graphics.DrawBox(textStart, textStart + textSize, settings.BackgroundColor.MultiplyAlpha(alphaMulti));
                Graphics.DrawText(text, textStart, settings.StageTextColor.MultiplyAlpha(alphaMulti));
                Graphics.DrawLine(textStart, topLeft with { X = textStart.X }, 1, Color.Green.MultiplyAlpha(alphaMulti));
            }
            else
            {
                Graphics.DrawLine(topLeft with { X = stageX }, bottomRight with { X = stageX }, 1, Color.Green.MultiplyAlpha(alphaMulti));
            }
        }
    }

    public override void EntityAdded(Entity entity)
    {
        if (entity.Type != EntityType.Monster && entity.Type != EntityType.Player ||
            entity.GetComponent<Life>() != null && !entity.IsAlive ||
            FindRule(entity.Path).Ignore == true)
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

    private EntityTreatmentRule FindRule(string path)
    {
        return _pathRuleCache.GetOrAdd(path, p => _entityConfig.Rules.FirstOrDefault(x => x.Regex.IsMatch(p)).Rule ?? new EntityTreatmentRule());
    }

    private bool ShowInBossOverlay(HealthBar bar)
    {
        return Settings.BossOverlaySettings.Show &&
               (FindRule(bar.Entity.Path).ShowInBossOverlay ?? bar.Settings.IncludeInBossOverlay.Value);
    }

    private bool IsCastBarEnabled(HealthBar bar)
    {
        return FindRule(bar.Entity.Path).ShowCastBar ?? bar.Settings.CastBarSettings.Show.Value;
    }
}