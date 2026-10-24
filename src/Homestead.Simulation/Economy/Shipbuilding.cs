using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Economy;

/// <summary>
/// The shipyard's worker (see <see cref="Shipping"/>): while a ship is on the stocks he takes one unit of its material
/// from the shipyard's piles, carries it along the pier to the hull, puts it down and hammers it in; then back inside,
/// and the next one.
/// </summary>
public sealed partial class Production
{
    private string ShipyardName => (_config.Shipping ?? ShippingRules.Default).Dockyard;

    /// <summary>Inside: off to the next unit the ship needs, else he waits.</summary>
    private void BuildShip(Workplace w)
    {
        var shipping = _world.Shipping;
        if (shipping.NextUnit(w.Building) is not { } pile)
        {
            w.Waiting = shipping.Building(w.Building) is null ? "no ship ordered" : "waiting for ship material";
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

    private void AdvanceShipyard(Workplace w)
    {
        var worker = w.Worker!;
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
                if (!_world.HasPile(pile) || pile.Count == 0 || pile.Material is not { } material || _world.Shipping.WorkSpot(w.Building) is not { } work)
                {
                    GiveUp(w, "ship material gone");
                    break;
                }
                pile.Count--;
                pile.ResetMaterialIfEmpty();
                worker.Carrying = material;
                _world.Shipping.Take(w.Building, material);
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
                    w.TicksLeft = WorkTicks(w, _world.Shipping.Rules.BuildTicks);
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
                    _world.Shipping.Build(w.Building, built);
                w.Product = null;
                if (!WalkTo(w, w.Building.Door, Phase.ToDoorToRest))
                {
                    Report("worker could not get back home");
                    ReleaseWorker(w);
                }
                break;
        }
    }

    /// <summary>He cannot get the unit to the hull: it is dropped, the ship needs another.</summary>
    private void LoseUnit(Workplace w, string reason)
    {
        if (w.Worker!.Carrying is { } material)
            _world.Shipping.Take(w.Building, material, back: true);
        GiveUp(w, reason);
    }

    private string ShipyardStatus(Workplace w) => w.Phase switch
    {
        Phase.ToShipPile or Phase.ShipPickingUp => "fetching ship material",
        Phase.ToHull => $"bringing {w.Worker?.Carrying} to the ship",
        Phase.Hammering => "building the ship",
        _ => "going home",
    };
}
