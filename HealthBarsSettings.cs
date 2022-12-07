using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace HealthBars;

public class HealthBarsSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(true);

    public ToggleNode IgnoreUiElementVisibility { get; set; } = new(false);
    public ToggleNode ShowInTown { get; set; } = new(false);
    public ToggleNode ShowInHideout { get; set; } = new(true);
    public ToggleNode EnableAbsolutePlayerBarPositioning { get; set; } = new(false);

    [ConditionalDisplay(nameof(EnableAbsolutePlayerBarPositioning))]
    public RangeNode<Vector2> PlayerBarPosition { get; set; } = new(new Vector2(1000, 1000), Vector2.Zero, Vector2.One * 4000);

    public RangeNode<float> SmoothingFactor { get; set; } = new(0, 0, 0.99f);

    [ConditionalDisplay(nameof(EnableAbsolutePlayerBarPositioning), false)]
    public RangeNode<float> PlayerSmoothingFactor { get; set; } = new(1, 0, 1);

    public RangeNode<int> GlobalZOffset { get; set; } = new(0, -300, 300);

    [ConditionalDisplay(nameof(EnableAbsolutePlayerBarPositioning), false)]
    public RangeNode<int> PlayerZOffset { get; set; } = new(0, -300, 300);

    [Menu(null, "By default, bar is placed relative to the model top")]
    public ToggleNode PlaceBarRelativeToGroundLevel { get; set; } = new(false);

    [Menu(null, "By default, bar is placed relative to the model top")]
    [ConditionalDisplay(nameof(EnableAbsolutePlayerBarPositioning), false)]
    public ToggleNode PlacePlayerBarRelativeToGroundLevel { get; set; } = new(false);

    public RangeNode<int> DrawDistanceLimit { get; set; } = new(133, 0, 1000);
    public ToggleNode MultiThreading { get; set; } = new(false);
    public RangeNode<int> MinimumEntitiesForMultithreading { get; set; } = new(10, 1, 200);
    public RangeNode<int> ShowMinionOnlyWhenBelowHp { get; set; } = new(50, 1, 100);
    public RangeNode<int> DpsEstimateDuration { get; set; } = new(2000, 500, 10000);
    public RangeNode<int> CullPercent { get; set; } = new(10, 0, 100);

    [Menu(null, "Combines Life and ES into a single bar that's filled proportionally to the total EHP")]
    public ToggleNode CombineLifeAndEs { get; set; } = new(true);

    [ConditionalDisplay(nameof(CombineLifeAndEs), false)]
    public RangeNode<float> EsBarHeight { get; set; } = new(1 / 3f, 0, 1);

    public ColorNode CombatDamageColor { get; set; } = Color.Red;
    public ColorNode CombatHealColor { get; set; } = Color.Green;
    public ToggleNode ResizeBarsToFitText { get; set; } = new(true);
    public ToggleNode UseShadedTexture { get; set; } = new(true);

    public UnitSettings Self { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0x008000),
        TextFormat = { Value = "{percent}%" },
        ShowDps = { Value = false },
    };

    public UnitSettings UniqueEnemy { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0xffa500),
        OutlineColor = Extensions.FromHex(0xffa500),
        TextColor = Extensions.FromHex(0x66ff99),
        Width = { Value = 200 },
        Height = { Value = 25 },
    };

    public UnitSettings RareEnemy { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0xf4ff19),
        OutlineColor = Extensions.FromHex(0xf4ff19),
        TextColor = Extensions.FromHex(0x66ff99),
        Width = { Value = 125 },
        Height = { Value = 20 },
    };

    public UnitSettings MagicEnemy { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0x8888ff),
        OutlineColor = Extensions.FromHex(0x8888ff),
        TextColor = Extensions.FromHex(0x66ff99),
        Width = { Value = 100 },
        Height = { Value = 15 },
        TextFormat = { Value = "" },
        ShowDps = { Value = false },
    };

    public UnitSettings NormalEnemy { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0xff0000),
        TextColor = Extensions.FromHex(0x66ff66),
        Width = { Value = 75 },
        Height = { Value = 10 },
        TextFormat = { Value = "" },
        ShowDps = { Value = false },
    };

    public UnitSettings Minions { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0x90ee90),
        TextFormat = { Value = "" },
        ShowDps = { Value = false },
    };

    public UnitSettings Players { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0x008000),
        TextFormat = { Value = "{percent}%" },
        ShowDps = { Value = false },
    };
}

[Submenu(CollapsedByDefault = true)]
public class UnitSettings
{
    public ToggleNode Show { get; set; } = new(true);
    public RangeNode<float> Width { get; set; } = new(100, 20, 250);
    public RangeNode<float> Height { get; set; } = new(20, 5, 150);
    public RangeNode<int> OutlineThickness { get; set; } = new(1, 0, 20);
    public RangeNode<int> HealthSegments { get; set; } = new(1, 1, 10);
    public RangeNode<float> HealthSegmentHeight { get; set; } = new(0.5f, 0, 1);
    public ColorNode LifeColor { get; set; } = Color.White;
    public ColorNode EsColor { get; set; } = Color.Aqua;
    public ColorNode CullableColor { get; set; } = Color.White;
    public ColorNode BackgroundColor { get; set; } = Color.Black;
    public ColorNode OutlineColor { get; set; } = Color.Transparent;
    public ColorNode TextColor { get; set; } = Color.White;
    public ColorNode TextBackground { get; set; } = Color.Black;
    public ColorNode HealthSegmentColor { get; set; } = Color.Black;
    public RangeNode<Vector2> TextPosition { get; set; } = new(new Vector2(0, -1), new Vector2(-1, -1), new Vector2(1, 1));
    public ToggleNode ShowDps { get; set; } = new(true);
    public RangeNode<float> HoverOpacity { get; set; } = new RangeNode<float>(1, 0, 1);

    [Menu(null,
        "Text template to show next to the bar. Available variables are:\n" +
        "{percent} - current percent of total HP (Life + ES)\n" +
        "{current} - current HP (Life + ES) value\n" +
        "{total} - total HP (Life + ES)\n" +
        "{currentes} - current ES\n" +
        "{currentlife} - current Life\n\n" +
        "Example:\n" +
        "{percent}% {current}/{total} -> 50% 5.00K/10.0K")]
    public TextNode TextFormat { get; set; } = "{percent}% {current}/{total}";
}