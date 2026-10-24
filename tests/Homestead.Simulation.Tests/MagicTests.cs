using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Magic;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Tests;

public class MagicTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);
    private static readonly BuildingCatalog Asians = BuildingCatalog.Load(Civilization.Asian);
    private static readonly BuildingCatalog Amazons = BuildingCatalog.Load(Civilization.Amazon);
    private static readonly BuildingCatalog Egyptians = BuildingCatalog.Load(Civilization.Egyptian);

    [Fact]
    public void The_big_temple_sends_a_priest_at_once_and_then_every_few_minutes()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        world.PlaceBuilding(Asians["big_temple"], 0, new GridPoint(40, 40));

        Run(world, 20);
        var priest = Assert.Single(world.Settlers, s => s.Profession == Profession.Priest);
        Assert.Equal(Civilization.Asian, priest.People);

        Run(world, world.Magic.Config.PriestTicks);
        Assert.Equal(2, world.Settlers.Count(s => s.Profession == Profession.Priest));
    }

    [Fact]
    public void Vines_give_wine_and_the_temple_turns_it_into_mana()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools.WithCropGrowTicks("wine", 300));
        world.PlaceBuilding(Romans["winegrower"], 0, new GridPoint(30, 30));
        Assert.NotNull(world.PlaceBuilding(Romans["temple"], 0, new GridPoint(50, 40)));
        for (int i = 0; i < 3; i++)
            world.Spawn(new GridPoint(40 + i, 50), Profession.Carrier);

        Run(world, 10_000, () => world.Magic.Mana(0) >= 2);

        Assert.True(world.Magic.Mana(0) >= 2, $"mana {world.Magic.Mana(0)}, harvested {world.Production.CropHarvested}");
        // Vines grow again after the harvest.
        Assert.True(Enumerable.Range(0, world.Map!.PointCount).Any(i => world.Map.ObjectCodeAt(world.Map.Point(i)) is >= MapObjectCodes.FirstWine and <= MapObjectCodes.RipeWine));
    }

    [Fact]
    public void Rice_grows_only_on_swampy_ground()
    {
        var map = new GameMap(80);
        for (int y = 20; y < 40; y++)
        for (int x = 40; x < 60; x++)
            map.SetTerrain(new GridPoint(x, y), TerrainType.Mud);
        var world = new World(map, TestConfig.NoTools.WithCropGrowTicks("rice", 300));
        Assert.NotNull(world.PlaceBuilding(Asians["rice_farm"], 0, new GridPoint(30, 30)));
        world.PlaceBuilding(Asians["stock"], 0, new GridPoint(30, 55));
        for (int i = 0; i < 3; i++)
            world.Spawn(new GridPoint(30 + i, 45), Profession.Carrier);

        Run(world, 10_000, () => world.Production.CropHarvested >= 2);

        Assert.True(world.Production.CropHarvested >= 2);
        for (int i = 0; i < map.PointCount; i++)
            if (map.ObjectCodeAt(map.Point(i)) is >= MapObjectCodes.FirstRice and <= MapObjectCodes.RipeRice)
                Assert.Equal(TerrainType.Mud, map.TerrainAt(map.Point(i)));
    }

    [Fact]
    public void A_spell_is_only_cast_where_it_works_on_someone_and_costs_mana()
    {
        var world = TwoPeoples(out var priest, Romans);
        var target = new GridPoint(20, 20);
        Assert.Equal(CastResult.NoMana, world.Magic.Cast([priest], target));
        world.Magic.AddMana(0, 100);
        Assert.Equal(CastResult.NoTarget, world.Magic.Cast([priest], target));
        world.SpawnSoldier(target, SoldierType.Swordsman);
        Assert.Equal(CastResult.NoTarget, world.Magic.Cast([priest], target)); // only own soldiers there
        Assert.Equal(100, world.Magic.Mana(0));

        // An enemy alone is enough, no fight needed.
        var enemy = world.SpawnSoldier(new GridPoint(target.X + 2, target.Y), SoldierType.Swordsman, player: 1)!;
        world.Military.Move([enemy], enemy.Position, forced: true); // it stays put
        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], target));
        Run(world, 1_000, () => world.Magic.SpellsCast > 0);
        Assert.Equal(0, enemy.Player);
    }

    [Theory]
    [InlineData(Civilization.Roman)]
    [InlineData(Civilization.Egyptian)]
    public void Every_people_summons_piles_of_goods_without_a_fight(Civilization people)
    {
        var catalog = BuildingCatalog.Load(people);
        var spell = TwoPeoples(out var priest, catalog).Magic.SpellsOf(0).Single(s => s.Kind == SpellKind.SummonGoods);
        Assert.Equal(10, spell.Cost);
        var stone = new List<int>();
        for (int seed = 0; seed < 12; seed++)
        {
            var world = TwoPeoples(out priest, catalog, seed);
            world.Magic.AddMana(0, 100);
            var target = new GridPoint(priest.Position.X + world.Magic.Range, priest.Position.Y); // within range: no walking
            Assert.Equal(CastResult.Started, world.Magic.Cast([priest], target, spell));
            Run(world, 400, () => world.Magic.SpellsCast > 0);

            Assert.Equal(90, world.Magic.Mana(0));
            var piles = world.Piles.Where(p => p.Role == PileRole.Loose).ToList();
            var regular = piles.Where(p => p.Material != Material.Stone).ToList();
            Assert.InRange(regular.Count, 3, 4);
            Assert.All(piles, p => Assert.InRange(p.Count, 4, 8));
            Assert.All(piles, p => Assert.True(PathFinder.InCircle(target, p.Position, Magic.Magic.SummonSpread)));
            Assert.All(regular, p => Assert.Contains(p.Material!.Value, spell.Goods.SelectMany(g => g)));
            Assert.Equal(piles.Count, world.Magic.Effects.Single().Points.Count);
            stone.Add(piles.Count - regular.Count);
        }
        if (people == Civilization.Egyptian)
            Assert.Contains(1, stone); // half the time
        else
            Assert.All(stone, n => Assert.Equal(0, n));
    }

    [Fact]
    public void Each_cast_makes_the_next_spell_of_its_kind_a_quarter_dearer_rounded_up()
    {
        var world = TwoPeoples(out var priest, Romans);
        var summon = world.Magic.SpellsOf(0).Single(s => s.Kind == SpellKind.SummonGoods);
        var own = world.Magic.SpellOf(0)!;
        Assert.Equal(25, world.Magic.Config.CostIncrease);
        world.Magic.AddMana(0, 200);
        var paid = new List<int>();
        for (int cast = 1; cast <= 3; cast++)
        {
            int before = world.Magic.Mana(0);
            var target = new GridPoint(priest.Position.X + world.Magic.Range - cast * 3, priest.Position.Y);
            Assert.Equal(CastResult.Started, world.Magic.Cast([priest], target, summon));
            Run(world, 400, () => world.Magic.SpellsCast == cast && !world.Magic.IsBusy(priest));
            paid.Add(before - world.Magic.Mana(0));
        }

        Assert.Equal([10, 13, 17], paid);
        Assert.Equal(22, world.Magic.CostOf(0, summon));
        Assert.Equal(22, world.Magic.MenuOf(0).Single(m => m.Spell == summon).Cost);
        Assert.Equal(own.Cost, world.Magic.CostOf(0, own)); // only the kind cast grows
        Assert.Equal(28, world.Magic.CostOf(0, summon, ahead: 1));
    }

    [Fact]
    public void Without_cost_increase_spells_keep_their_price()
    {
        var magic = MagicConfig.Load() with { CostIncrease = 0 };
        var world = new World(new GameMap(90), TestConfig.NoTools, magic: magic) { Peoples = [Civilization.Roman, Civilization.Asian] };
        var summon = world.Magic.SpellsOf(0).Single(s => s.Kind == SpellKind.SummonGoods);
        Assert.Equal(summon.Cost, world.Magic.CostOf(0, summon, ahead: 5));
    }

    [Fact]
    public void Spells_reach_24_fields_like_the_original()
    {
        var world = TwoPeoples(out _, Romans);
        Assert.Equal(24, world.Magic.Range);
    }

    [Fact]
    public void Romans_win_enemy_soldiers_over_who_keep_their_people()
    {
        var world = TwoPeoples(out var priest, Romans);
        var (_, enemies) = Battle(world);
        world.Magic.AddMana(0, 100);
        var cost = world.Magic.SpellOf(priest)!.Cost;

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(41, 40)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        Assert.Equal(1, world.Magic.SpellsCast);
        Assert.Equal(100 - cost, world.Magic.Mana(0));
        var converted = enemies.Where(e => !e.IsDead && e.Player == 0).ToList();
        Assert.InRange(converted.Count, 5, world.Magic.SpellOf(priest)!.Count);
        Assert.All(converted, c => Assert.Equal(Civilization.Asian, c.People));
    }

    [Fact]
    public void Amazons_freeze_enemies_on_the_spot()
    {
        var world = TwoPeoples(out var priest, Amazons);
        var (_, enemies) = Battle(world);
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(41, 40)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);
        var frozen = enemies.Where(e => !e.IsDead && e.FrozenUntil > world.Tick).ToList();
        Assert.NotEmpty(frozen);
        Run(world, World.StepTicks); // a step under way is finished
        var health = enemies.ToDictionary(e => e.Id, e => e.Health);
        var positions = frozen.ToDictionary(e => e.Id, e => e.Position);

        Run(world, 100);
        Assert.All(frozen.Where(e => !e.IsDead), e => Assert.Equal(positions[e.Id], e.Position));
        Assert.Contains(enemies, e => e.IsDead || e.Health < health[e.Id]);
    }

    [Fact]
    public void Egyptians_scatter_enemies_over_the_map()
    {
        var world = TwoPeoples(out var priest, Egyptians);
        var (_, enemies) = Battle(world);
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(41, 40)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        int away = enemies.Count(e => !e.IsDead && PathFinder.Distance(e.Position, new GridPoint(41, 40)) > world.Magic.SpellOf(priest)!.Radius + 2);
        Assert.True(away >= Math.Min(world.Magic.SpellOf(priest)!.Count, 5), $"{away} scattered");
    }

    [Fact]
    public void Asians_make_their_soldiers_strike_faster()
    {
        var world = TwoPeoples(out var priest, Asians);
        var (friends, _) = Battle(world);
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(41, 40)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        var hasted = Assert.Single(friends.Where(f => f.IsCharmed(SpellKind.Haste, world.Tick)).Take(1));
        var control = world.SpawnSoldier(new GridPoint(70, 70), SoldierType.Swordsman, level: 3)!; // far away, not hasted
        Assert.Equal(world.Military.DamageOf(control) * 1.33f, world.Military.DamageOf(hasted), 2);
    }

    [Fact]
    public void Romans_put_fear_into_enemies_on_their_land_who_strike_with_half_the_damage()
    {
        var world = TwoPeoples(out var priest, Romans);
        var map = world.Map!;
        for (int y = 30; y < 50; y++)
            for (int x = 30; x < 50; x++)
                map.SetOwner(new GridPoint(x, y), 0);
        var (_, enemies) = Battle(world);
        var enemy = enemies[4];
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], enemy.Position, Spell(world, SpellKind.Fear)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        Assert.True(enemy.IsCharmed(SpellKind.Fear, world.Tick));
        var control = world.SpawnSoldier(new GridPoint(31, 31), SoldierType.Swordsman, level: 3, player: 1)!; // on the land, no fear
        Assert.Equal(world.Military.DamageOf(control) * 0.5f, world.Military.DamageOf(enemy), 2);
        map.SetOwner(enemy.Position, 1); // off the caster's land: the spell does nothing
        Assert.Equal(world.Military.Config.Damage(SoldierType.Swordsman, 3), world.Military.DamageOf(enemy), 2);
    }

    [Fact]
    public void Egyptians_strengthen_their_soldiers_away_from_home_only()
    {
        var world = TwoPeoples(out var priest, Egyptians);
        var (friends, _) = Battle(world);
        var friend = friends[4];
        float before = world.Military.DamageOf(friend);
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], friend.Position, Spell(world, SpellKind.Strengthen)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        Assert.Equal(before * 2, world.Military.DamageOf(friend), 2);
        world.Map!.SetOwner(friend.Position, 0);
        Assert.Equal(world.Military.Config.Damage(SoldierType.Swordsman, 3), world.Military.DamageOf(friend), 2); // at home: no spell, full strength
    }

    [Fact]
    public void Shields_take_a_third_off_and_cursed_arrows_all_of_the_bowmens_damage()
    {
        var world = TwoPeoples(out var priest, Asians);
        var (friends, enemies) = Battle(world);
        var bowman = world.SpawnSoldier(new GridPoint(60, 60), SoldierType.Bowman, player: 1)!;
        var friend = friends[4];
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], friend.Position, Spell(world, SpellKind.Shield)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);
        Assert.Equal(20 * 0.67f, world.Military.Taken(friend, enemies[4], 20), 2);

        var amazons = TwoPeoples(out var priestess, Amazons);
        var (guarded, _) = Battle(amazons);
        amazons.Magic.AddMana(0, 100);
        Assert.Equal(CastResult.Started, amazons.Magic.Cast([priestess], guarded[4].Position, Spell(amazons, SpellKind.CursedArrows)));
        Run(amazons, 400, () => amazons.Magic.SpellsCast > 0);
        var archer = amazons.SpawnSoldier(new GridPoint(60, 60), SoldierType.Bowman, player: 1)!;
        Assert.Equal(0, amazons.Military.Taken(guarded[4], archer, 20));
        Assert.True(amazons.Military.Taken(guarded[4], guarded[0], 20) > 0); // swords still hurt
        Assert.NotNull(bowman);
    }

    [Fact]
    public void Egyptians_turn_enemy_bowmen_into_pioneers()
    {
        var world = TwoPeoples(out var priest, Egyptians);
        var bowmen = Enumerable.Range(0, 3).Select(i => world.SpawnSoldier(new GridPoint(40 + i, 40), SoldierType.Bowman, player: 1)!).ToList();
        var swordsman = world.SpawnSoldier(new GridPoint(40, 42), SoldierType.Swordsman, player: 1)!;
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(41, 40), Spell(world, SpellKind.BowsToPioneers)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        Assert.All(bowmen, b => Assert.Equal((Profession.Pioneer, (SoldierType?)null, 1), (b.Profession, b.SoldierType, b.Player)));
        Assert.Equal(Profession.Soldier, swordsman.Profession);
    }

    [Fact]
    public void Asian_reinforcements_come_to_the_priest_from_anywhere()
    {
        var world = TwoPeoples(out var priest, Asians);
        var far = Enumerable.Range(0, 3).Select(i => world.SpawnSoldier(new GridPoint(80, 80 + i), SoldierType.Pikeman)!).ToList();
        var geologist = world.Spawn(new GridPoint(82, 80), Profession.Geologist)!;
        world.Magic.AddMana(0, 100);
        var start = priest.Position;

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(81, 81), Spell(world, SpellKind.Reinforce)));
        Run(world, 10, () => world.Magic.SpellsCast > 0);

        Assert.Equal(start, priest.Position); // cast where it stands
        Assert.All(far.Append(geologist), u => Assert.True(PathFinder.Distance(u.Position, priest.Position) <= 3, $"{u.Position}"));
    }

    [Fact]
    public void Conversion_takes_other_units_but_not_soldiers_of_a_tower()
    {
        var world = TwoPeoples(out var priest, Romans);
        var tower = world.PlaceBuilding(Asians["tower"], 1, new GridPoint(45, 45), force: true)!;
        world.SpawnSoldier(new GridPoint(46, 50), SoldierType.Swordsman, player: 1);
        Run(world, 2_000, () => world.Military.IsOccupied(tower));
        Assert.True(world.Military.IsOccupied(tower));
        var geologist = world.Spawn(new GridPoint(41, 40), Profession.Geologist, player: 1)!;
        var enemyPriest = world.Magic.SpawnPriest(new GridPoint(42, 41), 1)!;
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(43, 42)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        Assert.Equal(0, geologist.Player);
        Assert.Equal(0, enemyPriest.Player);
        Assert.All(world.Settlers.Where(s => s.Profession == Profession.Soldier), s => Assert.Equal(1, s.Player));
    }

    [Fact]
    public void Midas_turns_up_to_forty_iron_bars_lying_in_the_area_into_gold()
    {
        var world = TwoPeoples(out var priest, Romans);
        for (int i = 0; i < 6; i++)
            world.AddLooseGoods(new GridPoint(38 + 2 * (i % 3), 38 + 2 * (i / 3)), Material.Iron, 8); // 48 bars
        world.AddLooseGoods(new GridPoint(40, 43), Material.Coal, 8);
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(40, 39), Spell(world, SpellKind.IronToGold)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        int Count(Material m) => world.Piles.Where(p => p.Material == m).Sum(p => p.Count);
        Assert.Equal(40, Count(Material.Gold));
        Assert.Equal(8, Count(Material.Iron));
        Assert.Equal(8, Count(Material.Coal));
    }

    [Fact]
    public void Amazons_turn_enemy_gold_into_stones()
    {
        var world = TwoPeoples(out var priest, Amazons);
        world.Map!.SetOwner(new GridPoint(42, 40), 1);
        var pile = world.AddLooseGoods(new GridPoint(42, 40), Material.Gold, 6)!;
        world.Magic.AddMana(0, 100);
        Assert.Equal(CastResult.NoTarget, world.Magic.Cast([priest], new GridPoint(70, 70), Spell(world, SpellKind.GoldToStone)));

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], pile.Position, Spell(world, SpellKind.GoldToStone)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        Assert.DoesNotContain(world.Piles, p => p.Material == Material.Gold && p.Count > 0);
        Assert.Equal(6, world.Piles.Where(p => p.Material == Material.Stone).Sum(p => p.Count));
    }

    [Fact]
    public void Amazons_call_goods_from_their_land_to_the_priestess_and_send_others_away()
    {
        var world = TwoPeoples(out var priest, Amazons);
        var map = world.Map!;
        for (int y = 60; y < 80; y++)
            for (int x = 60; x < 80; x++)
                map.SetOwner(new GridPoint(x, y), 0);
        world.AddLooseGoods(new GridPoint(70, 70), Material.Plank, 8);
        world.AddLooseGoods(new GridPoint(10, 80), Material.Stone, 8); // nobody's land: not called
        world.Magic.AddMana(0, 100);
        Assert.Equal(CastResult.NoTarget, world.Magic.Cast([priest], new GridPoint(10, 80), Spell(world, SpellKind.CallGoods)));

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(70, 70), Spell(world, SpellKind.CallGoods)));
        Run(world, 10, () => world.Magic.SpellsCast > 0);
        var planks = Assert.Single(world.Piles, p => p.Material == Material.Plank);
        Assert.True(PathFinder.Distance(planks.Position, priest.Position) <= 2, $"{planks.Position}");

        // Sending: the goods around the priestess go to the target, however far.
        Run(world, 100);
        var far = new GridPoint(85, 10);
        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], far, Spell(world, SpellKind.SendGoods)));
        Run(world, 10, () => world.Magic.SpellsCast > 1);
        planks = Assert.Single(world.Piles, p => p.Material == Material.Plank);
        Assert.Equal(8, planks.Count);
        Assert.True(PathFinder.Distance(planks.Position, far) <= 2, $"{planks.Position}");
        Run(world, world.Magic.Config.CastTicks + 1);
        Assert.Equal(CastResult.NoTarget, world.Magic.Cast([priest], far, Spell(world, SpellKind.SendGoods))); // nothing left around her
    }

    [Fact]
    public void The_magic_eye_and_revealing_the_map_let_the_caster_see_for_a_while()
    {
        var world = TwoPeoples(out var priest, Amazons);
        var vision = new Vision(world.Map!.Size, 0);
        var far = new GridPoint(80, 80);
        world.Magic.AddMana(0, 100);
        vision.Update(world);
        Assert.False(vision.IsVisible(far));

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], priest.Position, Spell(world, SpellKind.RevealMap)));
        Run(world, 10, () => world.Magic.SpellsCast > 0);
        vision.Update(world);
        Assert.True(vision.IsVisible(far));
        Assert.True(world.Military.Sees(0, far));

        Run(world, Spell(world, SpellKind.RevealMap).Ticks);
        vision.Update(world);
        Assert.False(vision.IsVisible(far));
        Assert.True(vision.IsExplored(world.Map.Index(far)));

        var romans = TwoPeoples(out var romanPriest, Romans);
        romans.Magic.AddMana(0, 100);
        var spot = new GridPoint(40, 40);
        Assert.Equal(CastResult.Started, romans.Magic.Cast([romanPriest], spot, Spell(romans, SpellKind.MagicEye)));
        Run(romans, 400, () => romans.Magic.SpellsCast > 0);
        Assert.True(romans.Military.Sees(0, new GridPoint(40, 70))); // 30 fields from the eye
        Assert.False(romans.Military.Sees(0, new GridPoint(40, 75)));
    }

    [Fact]
    public void Horus_heat_turns_grass_into_desert_with_borders_and_grassland_turns_it_back()
    {
        var world = TwoPeoples(out var priest, Egyptians);
        var map = world.Map!;
        var target = new GridPoint(45, 40);
        world.Magic.AddMana(0, 200);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], target, Spell(world, SpellKind.Desertify)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        Assert.Equal(TerrainType.Desert, map.TerrainAt(target));
        // Every desert point next to grass is a border, with the outer border on the grass beyond.
        for (int i = 0; i < map.PointCount; i++)
        {
            var p = map.Point(i);
            var t = map.TerrainAt(p);
            var around = Enumerable.Range(0, 6).Select(d => p.Neighbor((Direction)d)).Where(map.Contains).Select(map.TerrainAt).ToList();
            if (t == TerrainType.Desert)
                Assert.All(around, n => Assert.True(n is TerrainType.Desert or TerrainType.DesertBorder, $"{p}: {n} next to desert"));
            if (t == TerrainType.DesertBorder)
                Assert.All(around, n => Assert.True(n is TerrainType.Desert or TerrainType.DesertBorder or TerrainType.DesertBorderOuter, $"{p}: {n}"));
        }
        Assert.Contains(Enumerable.Range(0, map.PointCount), i => map.TerrainAt(i) == TerrainType.DesertBorderOuter);

        var romans = TwoPeoples(out var romanPriest, Romans);
        for (int i = 0; i < map.PointCount; i++)
            romans.Map!.SetTerrain(map.Point(i), map.TerrainAt(i));
        romans.Magic.AddMana(0, 100);
        Assert.Equal(CastResult.Started, romans.Magic.Cast([romanPriest], target, Spell(romans, SpellKind.Grassland)));
        Run(romans, 400, () => romans.Magic.SpellsCast > 0);
        Assert.Equal(TerrainType.Grass, romans.Map!.TerrainAt(target));
    }

    [Fact]
    public void Draining_the_swamp_makes_it_walkable_grass()
    {
        var world = TwoPeoples(out var priest, Egyptians);
        var map = world.Map!;
        var target = new GridPoint(40, 40);
        foreach (var p in PathFinder.Circle(target, 3).Where(map.Contains))
            map.SetTerrain(p, TerrainType.Moor);
        Assert.False(map.IsWalkable(target));
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], target, Spell(world, SpellKind.DrainSwamp)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        Assert.Equal(TerrainType.Grass, map.TerrainAt(target));
        Assert.True(map.IsWalkable(target));
    }

    [Fact]
    public void Forest_fire_burns_the_trees_in_the_area_and_those_right_next_to_them()
    {
        var world = TwoPeoples(out var priest, Egyptians);
        var map = world.Map!;
        var target = new GridPoint(40, 40);
        for (int x = 34; x <= 52; x++)
            map.SetObject(new GridPoint(x, 40), MapObjectKind.Tree); // a row of trees, 6 fields of them in the area
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], target, Spell(world, SpellKind.ForestFire)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);
        Assert.Equal(MapObjectCodes.TreeStump, map.ObjectCodeAt(target));
        Assert.True(MapObjectCodes.IsAdultTree(map.ObjectCodeAt(new GridPoint(47, 40))));

        Run(world, Magic.Magic.FireSpreadTicks + 1);
        Assert.Equal(MapObjectCodes.TreeStump, map.ObjectCodeAt(new GridPoint(47, 40))); // next to the area: caught fire
        Assert.True(MapObjectCodes.IsAdultTree(map.ObjectCodeAt(new GridPoint(48, 40))));
        Assert.True(MapObjectCodes.IsAdultTree(map.ObjectCodeAt(new GridPoint(52, 40))));
    }

    [Fact]
    public void Forests_and_stones_appear_on_free_ground()
    {
        var world = TwoPeoples(out var priest, Amazons);
        var map = world.Map!;
        world.Magic.AddMana(0, 100);
        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(40, 40), Spell(world, SpellKind.GrowForest)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);
        int trees = Enumerable.Range(0, map.PointCount).Count(i => MapObjectCodes.IsAdultTree(map.ObjectCodeAt(map.Point(i))));
        Assert.InRange(trees, 60, 400);

        var asians = TwoPeoples(out var monk, Asians);
        asians.Magic.AddMana(0, 100);
        Assert.Equal(CastResult.Started, asians.Magic.Cast([monk], new GridPoint(40, 40), Spell(asians, SpellKind.StoneCurse)));
        Run(asians, 400, () => asians.Magic.SpellsCast > 0);
        var stones = Enumerable.Range(0, map.PointCount).Select(asians.Map!.Point)
            .Where(p => MapObjectCodes.StoneLeft(asians.Map.ObjectCodeAt(p)) > 0).ToList();
        Assert.InRange(stones.Count, 10, 20);
        Assert.True(stones.Select(p => asians.Map.ObjectCodeAt(p)).Distinct().Count() > 1, "stones of different sizes");
        Assert.All(stones, p => Assert.DoesNotContain(stones, q => q != p && PathFinder.Distance(p, q) <= 2));
    }

    [Fact]
    public void Resources_shrink_and_fish_floods_the_water()
    {
        var world = TwoPeoples(out var priest, Romans);
        var map = world.Map!;
        map.SetResource(new GridPoint(40, 40), ResourceType.Coal, 15);
        map.SetResource(new GridPoint(41, 40), ResourceType.GoldOre, 1);
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(40, 40), Spell(world, SpellKind.ShrinkResources)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);
        Assert.Equal((ResourceType.Coal, 7), map.ResourceAt(new GridPoint(40, 40)));
        Assert.Equal((ResourceType.GoldOre, 0), map.ResourceAt(new GridPoint(41, 40)));

        var asians = TwoPeoples(out var monk, Asians);
        var lake = new GridPoint(40, 40);
        foreach (var p in PathFinder.Circle(lake, 4).Where(asians.Map!.Contains))
            asians.Map.SetTerrain(p, TerrainType.Water);
        asians.Magic.AddMana(0, 100);
        Assert.Equal(CastResult.Started, asians.Magic.Cast([monk], lake, Spell(asians, SpellKind.FishFlood)));
        Run(asians, 400, () => asians.Magic.SpellsCast > 0);
        Assert.InRange(asians.Map.ResourceAmount(lake, ResourceType.Fish), 1, 4);
    }

    [Fact]
    public void Gorgeous_growth_ripens_crops_within_three_minutes()
    {
        var world = new World(new GameMap(90), TestConfig.NoTools) { Peoples = [Civilization.Roman, Civilization.Asian] };
        world.PlaceBuilding(Romans["big_temple"], 0, new GridPoint(20, 40));
        var farm = world.PlaceBuilding(Romans["farm"], 0, new GridPoint(40, 50))!;
        for (int i = 0; i < 3; i++)
            world.Spawn(new GridPoint(30 + i, 60), Profession.Carrier);
        var map = world.Map!;
        bool Sown() => Enumerable.Range(0, map.PointCount).Any(i => map.ObjectCodeAt(map.Point(i)) == MapObjectCodes.FirstGrain);
        Run(world, 20_000, Sown);
        var field = map.Point(Enumerable.Range(0, map.PointCount).First(i => map.ObjectCodeAt(map.Point(i)) == MapObjectCodes.FirstGrain));
        var priest = world.Settlers.Single(s => s.Profession == Profession.Priest);
        world.Magic.AddMana(0, 100);
        var growth = world.Magic.SpellsOf(0).Single(s => s.Kind == SpellKind.Growth);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], field, growth));
        Run(world, 400, () => world.Magic.SpellsCast > 0);
        Run(world, growth.Ticks + 40); // crops grow on every tenth tick: a few ticks late per stage

        Assert.Equal(MapObjectCodes.RipeGrain, map.ObjectCodeAt(field));
        Assert.NotNull(farm);
    }

    [Fact]
    public void Snow_melts_into_mountain()
    {
        var world = TwoPeoples(out var priest, Asians);
        var map = world.Map!;
        var peak = new GridPoint(40, 40);
        foreach (var p in PathFinder.Circle(peak, 8).Where(map.Contains))
            map.SetTerrain(p, PathFinder.Distance(peak, p) <= 3 ? TerrainType.Snow : TerrainType.Mountain);
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], peak, Spell(world, SpellKind.MeltSnow)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        Assert.Equal(TerrainType.Mountain, map.TerrainAt(peak));
        Assert.True(map.IsWalkable(peak));
    }

    private static Spell Spell(World world, SpellKind kind) => world.Magic.SpellsOf(0).Single(s => s.Kind == kind);

    [Fact]
    public void Soldiers_attack_enemy_priests()
    {
        var world = TwoPeoples(out var priest, Romans);
        var enemy = world.SpawnSoldier(priest.Position + new GridPoint(4, 0), SoldierType.Swordsman, level: 3, player: 1)!;

        Run(world, 2_000, () => priest.IsDead);

        Assert.True(priest.IsDead);
        Assert.False(enemy.IsDead);
    }

    [Fact]
    public void Soldier_types_are_upgraded_by_the_mana_earned_in_all_at_a_small_temple_all_to_level_2_first()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        var magic = world.Magic;
        var needed = magic.Config.UpgradeThresholds;
        magic.AddMana(0, needed[3]);
        Assert.Equal(UpgradeResult.NoTemple, magic.Upgrade(0, SoldierType.Swordsman));
        world.PlaceBuilding(Romans["temple"], 0, new GridPoint(40, 40));

        Assert.Equal(UpgradeResult.Done, magic.Upgrade(0, SoldierType.Bowman));
        Assert.Equal(2, magic.SoldierLevel(0, SoldierType.Bowman));
        Assert.Equal(UpgradeResult.OthersFirst, magic.Upgrade(0, SoldierType.Bowman)); // swordsmen and pikemen first
        Assert.Equal(UpgradeResult.Done, magic.Upgrade(0, SoldierType.Pikeman));
        Assert.Equal(UpgradeResult.Done, magic.Upgrade(0, SoldierType.Swordsman));
        Assert.Equal(UpgradeResult.Done, magic.Upgrade(0, SoldierType.Bowman));
        Assert.Equal(3, magic.SoldierLevel(0, SoldierType.Bowman));
        Assert.Equal(UpgradeResult.Highest, magic.Upgrade(0, SoldierType.Bowman));
        Assert.Equal(needed[3], magic.Mana(0)); // upgrades spend nothing
        Assert.Equal(1, magic.SoldierLevel(1, SoldierType.Bowman)); // per player

        // Mana spent on spells still counts as earned; the next upgrade waits for more earned mana.
        magic.AddMana(0, -magic.Mana(0));
        Assert.Equal(needed[3], magic.ManaEarned(0));
        magic.AddMana(0, needed[4] - needed[3] - 1);
        Assert.Equal(UpgradeResult.NoMana, magic.Upgrade(0, SoldierType.Swordsman));
        magic.AddMana(0, 1);
        Assert.Equal(UpgradeResult.Done, magic.Upgrade(0, SoldierType.Swordsman));
    }

    [Fact]
    public void Only_soldiers_trained_after_an_upgrade_get_the_new_level()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        world.PlaceBuilding(Romans["temple"], 0, new GridPoint(20, 20));
        world.PlaceBuilding(Romans["barrack"], 0, new GridPoint(40, 40));
        var veteran = world.SpawnSoldier(new GridPoint(60, 60), SoldierType.Swordsman)!;
        world.Magic.AddMana(0, 100);
        Assert.Equal(UpgradeResult.Done, world.Magic.Upgrade(0, SoldierType.Swordsman));
        world.AddLooseGoods(new GridPoint(50, 55), Material.Sword, 1);
        world.Spawn(new GridPoint(45, 55), Profession.Carrier);

        Run(world, 10_000, () => world.Military.SoldiersTrained == 1);

        var recruit = Assert.Single(world.Settlers, s => s.Profession == Profession.Soldier && s != veteran);
        Assert.Equal(2, recruit.Level);
        Assert.Equal(world.Military.Config.MaxHealth(SoldierType.Swordsman, 2), recruit.Health);
        Assert.Equal(1, veteran.Level);
    }

    /// <summary>Player 0 has the given people (and a priest of it, from a big temple), player 1 is Asian.</summary>
    private static World TwoPeoples(out Settler priest, BuildingCatalog people, int seed = 0)
    {
        var world = new World(new GameMap(90), TestConfig.NoTools, seed: seed) { Peoples = [people.Civilization, Civilization.Asian] };
        world.PlaceBuilding(people["big_temple"], 0, new GridPoint(20, 40));
        Run(world, 20);
        priest = world.Settlers.Single(s => s.Profession == Profession.Priest);
        return world;
    }

    /// <summary>Two rows of strong soldiers fighting each other around x 40-41.</summary>
    private static (List<Settler> Friends, List<Settler> Enemies) Battle(World world)
    {
        var friends = new List<Settler>();
        var enemies = new List<Settler>();
        for (int y = 36; y <= 44; y++)
        {
            friends.Add(world.SpawnSoldier(new GridPoint(40, y), SoldierType.Swordsman, level: 3)!);
            enemies.Add(world.SpawnSoldier(new GridPoint(42, y), SoldierType.Swordsman, level: 3, player: 1)!);
        }
        Run(world, 40);
        Assert.Contains(friends, f => f.Activity == Activity.Fighting);
        return (friends, enemies);
    }

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }
}
