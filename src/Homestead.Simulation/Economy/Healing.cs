using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Hospitals: the healer calls the nearest wounded own idle soldier within
/// <see cref="Military.MilitaryConfig.HospitalCallRadius"/> to the field in front of the door. Whenever a
/// wounded own soldier stands still at the door, the healer comes out and heals it: once he is out, the
/// heal cannot be stopped (only by tearing the hospital down) and the soldier is free to go; unless it
/// dies, it has full health after <see cref="Military.MilitaryConfig.HealTicks"/>, wherever it is by then.
/// The healer goes back in; after a heal it waits
/// <see cref="Military.MilitaryConfig.HospitalCallPauseTicks"/> before calling the next one.
/// </summary>
public sealed partial class Production
{
    private const string HospitalName = "hospital";

    /// <summary>A called soldier that has not arrived by then is no longer waited for.</summary>
    private const int CallTimeoutTicks = 30 * World.TicksPerSecond;

    /// <summary>How often the healer looks out of the door.</summary>
    private const int LookTicks = 5;

    private sealed class HospitalState
    {
        /// <summary>The soldier called in, on its way to the door.</summary>
        public Settler? Called { get; set; }
        public long CalledUntil { get; set; }

        /// <summary>No soldier is called before this tick (pause after a heal).</summary>
        public long NextCall { get; set; }

        /// <summary>The soldier being healed.</summary>
        public Settler? Patient { get; set; }
    }

    /// <summary>The healer inside: heal a wounded soldier at the door, otherwise call one.</summary>
    private void LookForPatients(Workplace w)
    {
        var hospital = w.Hospital!;
        var door = w.Building.Door;
        var healerSpot = HealerSpot(w);
        var waiting = _world.Settlers.Where(s => IsWounded(s, w.Building.Player) && !s.HasTask
                                                 && s.State != SettlerState.Stepping && s.Destination is null
                                                 && (PathFinder.Distance(s.Position, door) <= 1 || PathFinder.Distance(s.Position, healerSpot) <= 1))
            .MinBy(s => s.Id);
        if (waiting is not null)
        {
            StartHealing(w, waiting);
            return;
        }
        w.Waiting = "waiting for wounded soldiers";
        Rest(w, LookTicks);

        if (hospital.Called is { } called && (called.IsDead || !IsWounded(called, w.Building.Player) || _world.Tick >= hospital.CalledUntil))
            hospital.Called = null;
        if (hospital.Called is not null || _world.Tick < hospital.NextCall)
            return;
        var config = _world.Military.Config;
        var nearest = _world.Settlers.Where(s => IsWounded(s, w.Building.Player) && !s.HasTask
                                                 && PathFinder.Distance(s.Position, door) <= config.HospitalCallRadius)
            .MinBy(s => PathFinder.Distance(s.Position, door));
        if (nearest is null)
            return;
        _world.Military.Move([nearest], HealSpot(w));
        hospital.Called = nearest;
        hospital.CalledUntil = _world.Tick + CallTimeoutTicks;
    }

    private bool IsWounded(Settler s, int player) =>
        s.Profession == Profession.Soldier && s.Player == player && !s.IsDead && !s.IsInside && s.SoldierType is { } type
        && s.Health < _world.MaxHealth(s.Player, type, s.Level);

    /// <summary>Where the healer stands to heal: a step down-left of the door (on screen), or the door itself.</summary>
    private GridPoint HealerSpot(Workplace w)
    {
        var below = w.Building.Door.Neighbor(Direction.SouthWest);
        return IsFreeGround(w, below) ? below : w.Building.Door;
    }

    /// <summary>
    /// Where a called soldier waits: down-right of the healer's spot (on screen; the healer turns to it),
    /// otherwise another walkable field next to it, away from the building so it is seen; the healer's
    /// spot itself if there is none.
    /// </summary>
    private GridPoint HealSpot(Workplace w)
    {
        var healer = HealerSpot(w);
        var below = healer.Neighbor(Direction.SouthEast);
        if (IsFreeGround(w, below))
            return below;
        return Enumerable.Range(0, DirectionExtensions.Count)
            .Select(d => healer.Neighbor((Direction)d))
            .Where(p => p != w.Building.Door && IsFreeGround(w, p))
            .Select(p => (GridPoint?)p)
            .MaxBy(p => PathFinder.Distance(p!.Value, w.Building.Position)) ?? healer;
    }

    /// <summary>A walkable field on the map outside the building.</summary>
    private bool IsFreeGround(Workplace w, GridPoint p) =>
        _world.Map!.Contains(p) && _world.Map.IsWalkable(p) && !w.Building.BlockedPoints.Contains(p);

    /// <summary>
    /// The healer comes out of the door and steps to his spot; the soldier stays free to go. From here on
    /// the heal cannot be stopped.
    /// </summary>
    private void StartHealing(Workplace w, Settler patient)
    {
        var hospital = w.Hospital!;
        if (hospital.Called == patient)
            hospital.Called = null;
        hospital.Patient = patient;
        w.Waiting = null;
        _world.LeaveBuilding(w.Worker!);
        var spot = HealerSpot(w);
        if (spot == w.Building.Door || !WalkTo(w, spot, Phase.ToHealSpot))
            BeginHeal(w);
    }

    /// <summary>The healer on his way out: heals once there (or where he got stuck).</summary>
    private void AdvanceToHealSpot(Workplace w)
    {
        if (w.Hospital!.Patient is not { IsDead: false })
        {
            EndHealing(w);
            return;
        }
        if (Arrived(w, out bool stalled) || stalled)
            BeginHeal(w);
    }

    /// <summary>The healer turns to the soldier and heals.</summary>
    private void BeginHeal(Workplace w)
    {
        var healer = w.Worker!;
        if (w.Hospital!.Patient is { } patient)
            healer.Facing = Enumerable.Range(0, DirectionExtensions.Count).Select(d => (Direction)d)
                .MinBy(d => PathFinder.Distance(healer.Position.Neighbor(d), patient.Position));
        healer.IsWorking = true;
        healer.Activity = Activity.Healing;
        healer.ActivityTicks = 0;
        w.Phase = Phase.Healing;
        w.TicksLeft = _world.Military.Config.HealTicks;
    }

    /// <summary>Healing: done after its time whatever the soldier does meanwhile, unless it dies.</summary>
    private void AdvanceHealing(Workplace w)
    {
        var patient = w.Hospital!.Patient;
        if (patient is null || patient.IsDead)
        {
            EndHealing(w);
            return;
        }
        if (--w.TicksLeft > 0)
            return;
        if (patient.SoldierType is { } type)
            patient.Health = _world.MaxHealth(patient.Player, type, patient.Level);
        HealsDone++;
        w.Hospital.NextCall = _world.Tick + _world.Military.Config.HospitalCallPauseTicks;
        EndHealing(w);
    }

    /// <summary>The heal is over (or the hospital gone): the healer goes back in.</summary>
    private void EndHealing(Workplace w)
    {
        if (w.Hospital is not { } hospital)
            return;
        hospital.Patient = null;
        if (w.Phase is not (Phase.Healing or Phase.ToHealSpot) || w.Worker is not { } healer)
            return;
        StopActivity(healer);
        if (healer.Position == w.Building.Door && healer.State != SettlerState.Stepping)
        {
            _world.EnterBuilding(healer);
            Rest(w, LookTicks);
        }
        else if (!WalkTo(w, w.Building.Door, Phase.ToDoorToRest))
        {
            _world.EnterBuilding(healer);
            Rest(w, LookTicks);
        }
    }

    /// <summary>Soldiers healed fully in hospitals.</summary>
    public int HealsDone { get; private set; }
}
