// Demiurge.Probe — reproducible batch measurement of pains.
// Runs N seeds × T ticks in memory through the real harness path (TickRunner + pain detectors),
// no SQLite, no waiting between ticks, and prints the pains per seed and aggregated.
//
//   dotnet run --project tools/Demiurge.Probe -- --brain rules --seeds 1-10 --ticks 1000 --agents 5
//
// Concept: Pain detectors — docs/CONCEPTS.md#harness-observabilidade-e-detectores-de-dor

using System.Globalization;
using Demiurge.Agents;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Harness;
using Demiurge.Harness.Pains;

var options = ProbeOptions.Parse(args);
Console.WriteLine($"brain={options.Brain} seeds={options.FirstSeed}-{options.LastSeed} ticks={options.Ticks} agents={options.Agents}");
Console.WriteLine();
Console.WriteLine("seed | deaths | talks | pains (code×episodes)");

var perSeed = new List<(ulong Seed, IReadOnlyList<Pain> Pains)>();
for (ulong seed = options.FirstSeed; seed <= options.LastSeed; seed++)
{
    var runner = new TickRunner(
        InMemorySimulation.Start(new WorldConfig { Seed = seed, AgentCount = options.Agents }),
        _ => options.CreateBrain(),
        TickRunnerOptions.FromInterval(TimeSpan.FromSeconds(1)),
        TimeProvider.System,
        PainDetectors.CreateDefault());

    int deaths = 0, talks = 0;
    for (int t = 0; t < options.Ticks; t++)
    {
        var outcome = await runner.RunTickAsync();
        deaths += outcome.Report.Events.Count(e => e is AgentDied);
        talks += outcome.Report.Events.Count(e => e is Talked);
    }

    var pains = runner.Pains.Ranked();
    perSeed.Add((seed, pains));
    Console.WriteLine($"{seed,4} | {deaths,6} | {talks,5} | {string.Join(", ", pains.Select(p => $"{p.Code}×{p.Count}"))}");
}

Console.WriteLine();
Console.WriteLine("code                   | seeds | episodes | earliest tick | severity");
foreach (var group in perSeed
             .SelectMany(s => s.Pains.Select(p => (s.Seed, Pain: p)))
             .GroupBy(x => x.Pain.Code)
             .OrderByDescending(g => g.Max(x => x.Pain.Severity) * g.Sum(x => x.Pain.Count))
             .ThenBy(g => g.Key, StringComparer.Ordinal))
{
    Console.WriteLine(
        $"{group.Key,-22} | {group.Count(),2}/{perSeed.Count,-2} | {group.Sum(x => x.Pain.Count),8} | {group.Min(x => x.Pain.FirstTick),13} | {group.Max(x => x.Pain.Severity),8}");
}

internal sealed record ProbeOptions(string Brain, ulong FirstSeed, ulong LastSeed, int Ticks, int Agents)
{
    public IAgentBrain CreateBrain() => Brain switch
    {
        "rules" => new RuleBasedBrain(),
        _ => throw new ArgumentException($"Unknown brain '{Brain}'. Available: rules."),
    };

    public static ProbeOptions Parse(string[] args)
    {
        string brain = "rules", seeds = "1-10";
        int ticks = 1000, agents = 5;
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--brain": brain = args[i + 1]; break;
                case "--seeds": seeds = args[i + 1]; break;
                case "--ticks": ticks = int.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
                case "--agents": agents = int.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"Unknown option {args[i]}.");
            }
        }

        var range = seeds.Split('-');
        ulong first = ulong.Parse(range[0], CultureInfo.InvariantCulture);
        ulong last = range.Length > 1 ? ulong.Parse(range[1], CultureInfo.InvariantCulture) : first;
        return new ProbeOptions(brain, first, last, ticks, agents);
    }
}
