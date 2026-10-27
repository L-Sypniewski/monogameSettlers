using Homestead.Simulation.Map;
using Homestead.Simulation.Persistence;

namespace Homestead.Simulation.Tests;

/// <summary>The global chat (<see cref="World.Chat"/>): every player may write, the lines are kept with the game.</summary>
public class ChatTests
{
    [Fact]
    public void Players_say_lines_in_order_at_the_current_tick()
    {
        var world = new World(new GameMap(10), TestConfig.NoTools);
        world.Say(0, "  hello  ");
        world.Step();
        world.Say(2, "hi");
        Assert.Equal([new ChatMessage(0, 0, "hello"), new ChatMessage(1, 2, "hi")], world.Chat.Messages);
    }

    [Fact]
    public void Blank_lines_say_nothing_and_long_ones_are_cut()
    {
        var world = new World(new GameMap(10), TestConfig.NoTools);
        Assert.Null(world.Say(0, "   "));
        Assert.Equal(ChatLog.MaxLength, world.Say(0, new string('x', ChatLog.MaxLength + 50))!.Value.Text.Length);
        Assert.Single(world.Chat.Messages);
    }

    [Fact]
    public void The_chat_is_kept_in_a_saved_game()
    {
        var world = new World(new GameMap(10), TestConfig.NoTools);
        world.Say(1, "gg");
        using var stream = new MemoryStream();
        Snapshot.Write(stream, world);
        stream.Position = 0;
        var loaded = Snapshot.Read<World>(stream);
        Assert.Equal(world.Chat.Messages, loaded.Chat.Messages);
    }
}
