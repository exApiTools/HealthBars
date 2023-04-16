using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HealthBars;

public class IndividualEntityConfig
{
    public IndividualEntityConfig(SerializedIndividualEntityConfig source)
    {
        Rules = source.EntityPathConfig.OrderByDescending(x => x.Value.RulePriority ?? 0).Select(x => (new Regex(x.Key, RegexOptions.Compiled), x.Value)).ToList();
    }

    public List<(Regex Regex, EntityTreatmentRule Rule)> Rules { get; }
}

public class SerializedIndividualEntityConfig
{
    public Dictionary<string, EntityTreatmentRule> EntityPathConfig { get; set; } = new();
}

public class EntityTreatmentRule
{
    public bool? ShowCastBar { get; set; }
    public bool? ShowInBossOverlay { get; set; }
    public bool? Ignore { get; set; }
    public int? RulePriority { get; set; }
}