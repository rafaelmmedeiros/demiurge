using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Engine.Tests;

/// <summary>
/// Concept: Tick-based simulation — docs/CONCEPTS.md#simulação-baseada-em-ticks
/// </summary>
public class TickTests
{
    [Fact]
    public void Each_step_advances_one_tick_and_applies_needs()
    {
        var (world, _) = World.Create(new WorldConfig { Seed = 42 });
        var before = world.Agents.Select(a => (a.Hunger, a.Energy)).ToList();

        var report = world.Step();

        Assert.Equal(1, world.Tick);
        Assert.Equal(1, report.Tick);
        Assert.IsType<TickStarted>(report.Events[0]);
        for (int i = 0; i < world.Agents.Count; i++)
        {
            Assert.Equal(Math.Min(100, before[i].Hunger + WorldRules.HungerPerTick), world.Agents[i].Hunger);
            Assert.Equal(Math.Max(0, before[i].Energy - WorldRules.EnergyPerTick), world.Agents[i].Energy);
        }
    }

    [Fact]
    public void Needs_are_clamped_to_0_and_100()
    {
        var (world, _) = World.Create(new WorldConfig { Seed = 42 });

        for (int i = 0; i < 60; i++) world.Step();

        Assert.All(world.Agents, a =>
        {
            Assert.InRange(a.Hunger, 0, 100);
            Assert.InRange(a.Energy, 0, 100);
        });
    }

    [Fact]
    public void Agent_starves_after_12_consecutive_ticks_at_max_hunger()
    {
        // Without actions nobody eats yet, so every agent must die exactly on schedule:
        // first tick at 100 = ceil((100 - h0) / 4); death 11 ticks later (12th consecutive tick).
        var (world, genesis) = World.Create(new WorldConfig { Seed = 42 });
        var expectedDeathTick = genesis.Events.OfType<AgentSpawned>().ToDictionary(
            s => s.AgentId,
            s => (long)Math.Ceiling((100 - s.Hunger) / (double)WorldRules.HungerPerTick) + WorldRules.StarvationTicks - 1);

        var deaths = new List<AgentDied>();
        for (int i = 0; i < 100; i++)
            deaths.AddRange(world.Step().Events.OfType<AgentDied>());

        Assert.Equal(world.Agents.Count, deaths.Count);
        Assert.All(deaths, d =>
        {
            Assert.Equal(DeathCause.Starvation, d.Cause);
            Assert.Equal(expectedDeathTick[d.AgentId], d.Tick);
        });
        Assert.All(world.Agents, a => Assert.False(a.IsAlive));
    }

    [Fact]
    public void Dead_agents_stop_changing()
    {
        var (world, _) = World.Create(new WorldConfig { Seed = 42, AgentCount = 1 });
        while (world.Agents[0].IsAlive) world.Step();
        int energyAtDeath = world.Agents[0].Energy;

        var report = world.Step();

        Assert.Equal(energyAtDeath, world.Agents[0].Energy);
        Assert.DoesNotContain(report.Events, e => e is NeedsUpdated);
    }
}
