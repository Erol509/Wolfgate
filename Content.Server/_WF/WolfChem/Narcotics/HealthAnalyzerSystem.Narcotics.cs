using Content.Server._WF.WolfChem.Narcotics;

namespace Content.Server.Medical;

/// <summary>Gives a scan the Narcotics conditions readout.</summary>
public sealed partial class HealthAnalyzerSystem
{
    [Dependency] private WFAnalyzerConditionsSystem _wfConditions = default!;
}
