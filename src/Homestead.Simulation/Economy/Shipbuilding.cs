using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Economy;

/// <summary>
/// The worker of an assembly (<see cref="IAssembly"/>: the shipyard's ships, see <see cref="Shipping"/>; war machines, see
/// <see cref="Military.WarMachines"/>): while there is work he takes one unit of its material from the building's piles,
/// carries it to the work spot (the pier's end, the hull), puts it down and hammers it in; then back inside, and the next one.
/// </summary>
public sealed partial class Production
{
    private string ShipyardName => (_config.Shipping ?? ShippingRules.Default).Dockyard;

    /// <summary>What the workplace puts together: the shipyard its ships, a war machine works its machines.</summary>
    private IAssembly AssemblyOf(Workplace w) => w.Shipyard ? _world.Shipping : _world.WarMachines;

    /// <summary>Inside: off to the next unit the work needs, else he waits.</summary>
    private void Assemble(Workplace w)
    {
        var assembly = AssemblyOf(w);
        if (assembly.NextUnit(w.Building) is not { } pile)
        {
            w.Waiting = assembly.Idle(w.Building);
            Rest(w, RetryTicks);
            return;
        }
        w.Waiting = null;
        w.Inputs.Clear();
        w.Inputs.Enqueue(pile);
        _world.LeaveBuilding(w.Worker!);
        if (!WalkTo(w, pile.Position, Phase.ToShipPile))
            GiveUp(w, "ship material unreachable");
    }

    private void AdvanceAssembly(Workplace w)
    {
        var worker = w.Worker!;
        var assembly = AssemblyOf(w);
        switch (w.Phase)
        {
            case Phase.ToShipPile:
                if (Arrived(w, out bool stalledPile))
                    BeginHandling(w, Activity.PickingUp, Phase.ShipPickingUp);
                else if (stalledPile)
                    GiveUp(w, "no progress to the ship material");
                break;

            case Phase.ShipPickingUp:
                if (--w.TicksLeft > 0)
                    break;
                StopActivity(worker);
                var pile = w.Inputs.Dequeue();
                if (!_world.HasPile(pile) || pile.Count == 0 || pile.Material is not { } material || assembly.WorkSpot(w.Building) is not { } work)
                {
                    GiveUp(w, "ship material gone");
                    break;
                }
                pile.Count--;
                pile.ResetMaterialIfEmpty();
                worker.Carrying = material;
                assembly.Take(w.Building, material);
                w.Facing = work.Facing;
                if (!WalkTo(w, work.Spot, Phase.ToHull))
                    LoseUnit(w, "hull unreachable");
                break;

            case Phase.ToHull:
                if (Arrived(w, out bool stalledHull))
                {
                    worker.Facing = w.Facing;
                    w.Product = worker.Carrying;
                    worker.Carrying = null;
                    BeginHandling(w, Activity.Building, Phase.Hammering);
                    w.TicksLeft = WorkTicks(w, assembly.BuildTicks);
                }
                else if (stalledHull)
                {
                    LoseUnit(w, "no progress to the hull");
                }
                break;

            case Phase.Hammering:
                if (--w.TicksLeft > 0)
                    break;
                StopActivity(worker);
                if (w.Product is { } built)
                    assembly.Build(w.Building, built);
                w.Product = null;
                if (!WalkTo(w, w.Building.Door, Phase.ToDoorToRest))
                {
                    Report("worker could not get back home");
                    ReleaseWorker(w);
                }
                break;
        }
    }

    /// <summary>He cannot get the unit to the work spot: it is dropped, the work needs another.</summary>
    private void LoseUnit(Workplace w, string reason)
    {
        if (w.Worker!.Carrying is { } material)
            AssemblyOf(w).Take(w.Building, material, back: true);
        GiveUp(w, reason);
    }

    private string AssemblyStatus(Workplace w)
    {
        string work = AssemblyOf(w).Work(w.Building);
        return w.Phase switch
        {
            Phase.ToShipPile or Phase.ShipPickingUp => $"fetching {work} material",
            Phase.ToHull => $"bringing {w.Worker?.Carrying} to the {work}",
            Phase.Hammering => $"building the {work}",
            _ => "going home",
        };
    }
}
