using System;
using System.Collections.Generic;
using System.Diagnostics;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Cache;
using ExileCore.Shared.Enums;
using SharpDX;

namespace HealthBars;

public class HealthBar
{
    private readonly Stopwatch _dpsStopwatch = Stopwatch.StartNew();
    private bool _isHostile;
    private readonly CachedValue<float> _distanceCache;

    public HealthBar(Entity entity, HealthBarsSettings settings)
    {
        Entity = entity;
        AllSettings = settings;
        _distanceCache = new TimeCache<float>(() => entity.DistancePlayer, 200);
        Update();
    }

    public void CheckUpdate()
    {
        var entityIsHostile = Entity.IsHostile;

        if (_isHostile != entityIsHostile)
        {
            _isHostile = entityIsHostile;
            Update();
        }

        if (Settings.ShowDps)
        {
            DpsRefresh();
        }
    }

    public bool Skip { get; set; } = false;
    public Vector2 LastPosition { get; set; }
    private HealthBarsSettings AllSettings { get; }

    public UnitSettings Settings => Type switch
    {
        CreatureType.Player when _distanceCache.Value == 0 => AllSettings.Self,
        CreatureType.Player => AllSettings.Players,
        CreatureType.Minion => AllSettings.Minions,
        CreatureType.Normal => AllSettings.NormalEnemy,
        CreatureType.Magic => AllSettings.MagicEnemy,
        CreatureType.Rare => AllSettings.RareEnemy,
        CreatureType.Unique => AllSettings.UniqueEnemy,
    };

    public RectangleF DisplayArea { get; set; }
    public float Distance => _distanceCache.Value;
    public Entity Entity { get; }
    public CreatureType Type { get; private set; }
    public Life Life => Entity.GetComponent<Life>();
    public float HpPercent => Life.HPPercentage;
    public float EsPercent => Life.ESPercentage;
    public float EhpPercent => CurrentEhp / (float)MaxEhp;
    public int CurrentEhp => Life.CurHP + Life.CurES;
    public int MaxEhp => Life.MaxHP + Life.MaxES;
    public readonly Queue<(DateTime Time, int Value)> EhpHistory = new Queue<(DateTime, int)>();

    public Color Color
    {
        get
        {
            if (IsHidden(Entity))
                return Color.LightGray;

            if (HpPercent * 100 <= AllSettings.CullPercent)
                return Settings.CullableColor;

            return Settings.LifeColor;
        }
    }

    private static bool IsHidden(Entity entity)
    {
        try
        {
            return entity.IsHidden;
        }
        catch
        {
            return false;
        }
    }

    private void Update()
    {
        Type = GetEntityType();
    }

    private CreatureType GetEntityType()
    {
        if (Entity.HasComponent<Player>())
        {
            return CreatureType.Player;
        }

        if (Entity.HasComponent<Monster>())
        {
            var objectMagicProperties = Entity.GetComponent<ObjectMagicProperties>();
            if (Entity.IsHostile)
            {
                return objectMagicProperties?.Rarity switch
                {
                    MonsterRarity.White => CreatureType.Normal,
                    MonsterRarity.Magic => CreatureType.Magic,
                    MonsterRarity.Rare => CreatureType.Rare,
                    MonsterRarity.Unique => CreatureType.Unique,
                    _ => CreatureType.Minion
                };
            }

            return CreatureType.Minion;
        }

        return CreatureType.Minion;
    }

    private void DpsRefresh()
    {
        if (_dpsStopwatch.ElapsedMilliseconds >= 200)
        {
            var hp = CurrentEhp;
            if (hp == MaxEhp && EhpHistory.TryPeek(out var entry) && hp == entry.Value)
            {
                EhpHistory.Clear();
            }
            else
            {
                while (EhpHistory.TryPeek(out entry) &&
                       DateTime.UtcNow - entry.Time > TimeSpan.FromMilliseconds(AllSettings.DpsEstimateDuration))
                {
                    EhpHistory.Dequeue();
                }
            }

            EhpHistory.Enqueue((DateTime.UtcNow, hp));
        }
    }
}