using System.Text.Json;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;

namespace Homestead.Client;

/// <summary>
/// Everything the screens and menus read from <c>data/ui.json</c>, as typed records: GUI file and image indices,
/// rectangles and colors, mirrored key by key. Loaded once (in <see cref="MapGame"/>); the consumers turn the
/// indices into textures themselves, each with the device it draws with. A key the file lacks fails the load with
/// its path (instead of a screen failing to open later); a key nobody reads shows in
/// <see cref="Homestead.Client.Tests"/>' meta-test, which compares <see cref="ReadPaths"/> with the file.
/// </summary>
public sealed class UiLayout
{
    private UiLayout(IReadOnlyList<PanelTabsEntry> panelTabs, SoldiersMenuData soldiersMenu, TextButtonSkin textButton,
        GuiRef statisticsBackground, IReadOnlyDictionary<string, GuiRef> loadingScreen, TitleScreenData titleScreen,
        SettingsScreenData settingsScreen, IReadOnlyDictionary<string, CampaignScreenData> campaignScreens,
        MissionScreenData missionScreen, StartScreenData startScreen, BuildingInfoIcons buildingInfo, MarketIcons market,
        ShipsData ships, DockyardData dockyard, WarMachinesData warMachines, StatusIconsData statusIcons,
        SpellMenuData spellMenu, ProfessionsMenuData professionsMenu, TransportMenuData transportMenu,
        DistributionMenuData distributionMenu, GoodsGridData goodsGrid, PortraitsData portraits,
        SettlersOverviewData settlersOverview, IReadOnlyList<BuildCategory> buildMenu, IReadOnlyList<StatusText> status)
    {
        PanelTabs = panelTabs;
        SoldiersMenu = soldiersMenu;
        TextButton = textButton;
        StatisticsBackground = statisticsBackground;
        LoadingScreen = loadingScreen;
        TitleScreen = titleScreen;
        SettingsScreen = settingsScreen;
        CampaignScreens = campaignScreens;
        MissionScreen = missionScreen;
        StartScreen = startScreen;
        BuildingInfo = buildingInfo;
        Market = market;
        Ships = ships;
        Dockyard = dockyard;
        WarMachines = warMachines;
        StatusIcons = statusIcons;
        SpellMenu = spellMenu;
        ProfessionsMenu = professionsMenu;
        TransportMenu = transportMenu;
        DistributionMenu = distributionMenu;
        GoodsGrid = goodsGrid;
        Portraits = portraits;
        SettlersOverview = settlersOverview;
        BuildMenu = buildMenu;
        Status = status;
    }

    public IReadOnlyList<PanelTabsEntry> PanelTabs { get; }
    public SoldiersMenuData SoldiersMenu { get; }
    public TextButtonSkin TextButton { get; }
    public GuiRef StatisticsBackground { get; }
    public IReadOnlyDictionary<string, GuiRef> LoadingScreen { get; }
    public TitleScreenData TitleScreen { get; }
    public SettingsScreenData SettingsScreen { get; }
    public IReadOnlyDictionary<string, CampaignScreenData> CampaignScreens { get; }
    public MissionScreenData MissionScreen { get; }
    public StartScreenData StartScreen { get; }
    public BuildingInfoIcons BuildingInfo { get; }
    public MarketIcons Market { get; }
    public ShipsData Ships { get; }
    public DockyardData Dockyard { get; }
    public WarMachinesData WarMachines { get; }
    public StatusIconsData StatusIcons { get; }
    public SpellMenuData SpellMenu { get; }
    public ProfessionsMenuData ProfessionsMenu { get; }
    public TransportMenuData TransportMenu { get; }
    public DistributionMenuData DistributionMenu { get; }
    public GoodsGridData GoodsGrid { get; }
    public PortraitsData Portraits { get; }
    public SettlersOverviewData SettlersOverview { get; }
    public IReadOnlyList<BuildCategory> BuildMenu { get; }
    public IReadOnlyList<StatusText> Status { get; }

    /// <summary>The JSON paths the last <see cref="Load"/> resolved (the meta-test's evidence).</summary>
    public static IReadOnlyList<string> ReadPaths { get; private set; } = [];

    public static UiLayout Load(string? dataDirectory = null)
    {
        var read = new List<string>();
        ReadPaths = read;
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory ?? BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var root = new Section(document.RootElement, "", read);
        return new UiLayout(
            panelTabs: root.Get("panelTabs").Items().Select(PanelTabsEntry.From).ToList(),
            soldiersMenu: SoldiersMenuData.From(root.Get("soldiersMenu")),
            textButton: TextButtonSkin.From(root.Get("textButton")),
            statisticsBackground: GuiRef.FromFileAndImage(root.Get("statisticsBackground")),
            loadingScreen: root.Get("loadingScreen").Map(v => v.Gui()),
            titleScreen: TitleScreenData.From(root.Get("titleScreen")),
            settingsScreen: SettingsScreenData.From(root.Get("settingsScreen")),
            campaignScreens: root.Get("campaignScreens").Map(CampaignScreenData.From),
            missionScreen: MissionScreenData.From(root.Get("missionScreen")),
            startScreen: StartScreenData.From(root.Get("startScreen")),
            buildingInfo: BuildingInfoIcons.From(root.Get("buildingInfo")),
            market: MarketIcons.From(root.Get("market")),
            ships: ShipsData.From(root.Get("ships")),
            dockyard: DockyardData.From(root.Get("dockyard")),
            warMachines: WarMachinesData.From(root.Get("warMachines")),
            statusIcons: StatusIconsData.From(root.Get("statusIcons")),
            spellMenu: SpellMenuData.From(root.Get("spellMenu")),
            professionsMenu: ProfessionsMenuData.From(root.Get("professionsMenu")),
            transportMenu: TransportMenuData.From(root.Get("transportMenu")),
            distributionMenu: DistributionMenuData.From(root.Get("distributionMenu")),
            goodsGrid: GoodsGridData.From(root.Get("goodsGrid")),
            portraits: PortraitsData.From(root.Get("portraits")),
            settlersOverview: SettlersOverviewData.From(root.Get("settlersOverview")),
            buildMenu: root.Get("buildMenu").Items().Select(BuildCategory.From).ToList(),
            status: root.Get("status").Items().Select(e => new StatusText(e.Str(0), e.Str(1))).ToList());
    }

    /// <summary>A GUI image: its file and the image's index in it.</summary>
    public sealed record GuiRef(int File, int Index)
    {
        internal static GuiRef FromFileAndImage(Section s) => new(s.Get("file").Int(), s.Get("gui").Int());
    }

    /// <summary>A settler sequence frame: file, sequence and frame in it.</summary>
    public sealed record SettlerRef(int File, int Sequence, int Frame);

    /// <summary>A main tab of the blue panel (build, goods, settlers), with its sub-menus (none for build).</summary>
    public sealed record PanelTabsEntry(string Id, int[] Tab, IReadOnlyList<PanelTabsEntry> Menus)
    {
        internal static PanelTabsEntry From(Section s) =>
            new(s.Get("id").Str(), s.Get("tab").Ints(),
                s.Has("menus") ? s.Get("menus").Items().Select(From).ToList() : []);
    }

    public sealed record SoldiersMenuData(IReadOnlyDictionary<string, int[]> Upgrades, int[] Arrows,
        IReadOnlyDictionary<string, SoldierWeapons> Weapons)
    {
        internal static SoldiersMenuData From(Section s) =>
            new(s.Get("upgrades").Map(v => v.Ints()), s.Get("arrows").Ints(), s.Get("weapons").Map(SoldierWeapons.From));
    }

    /// <summary>The weapon icons of a people's file, by soldier type.</summary>
    public sealed record SoldierWeapons(int File, int Swordsman, int Bowman, int Pikeman)
    {
        internal static SoldierWeapons From(Section s) =>
            new(s.Get("file").Int(), s.Get("SWORDSMAN").Int(), s.Get("BOWMAN").Int(), s.Get("PIKEMAN").Int());
    }

    /// <summary>The stone sheet the wide text buttons are cut from, with their two states and nine-slice border.</summary>
    public sealed record TextButtonSkin(int File, int Gui, Rectangle Normal, Rectangle Pressed, int Border)
    {
        internal static TextButtonSkin From(Section s) =>
            new(s.Get("file").Int(), s.Get("gui").Int(), s.Get("normal").Rect(), s.Get("pressed").Rect(), s.Get("border").Int());
    }

    public sealed record TitleScreenData(IReadOnlyDictionary<string, TitleTheme> Themes, GuiRef Popup)
    {
        internal static TitleScreenData From(Section s) => new(s.Get("themes").Map(TitleTheme.From), s.Get("popup").Gui());
    }

    /// <summary>The look of one theme (gold, classic, amazons): its pictures and engraved buttons' colors.</summary>
    public sealed record TitleTheme(int File, IReadOnlyDictionary<string, int> Background, int Plain, Rectangle PlainStrip,
        float? VersionRight, Color Fill, Color Pressed, Color Dark, Color Light, Color Shadow)
    {
        internal static TitleTheme From(Section s) =>
            new(s.Get("file").Int(), s.Get("background").Map(v => v.Int()), s.Get("plain").Int(), s.Get("plainStrip").Rect(),
                s.Has("versionRight") ? s.Get("versionRight").Float() : null, s.Get("fill").Rgb(), s.Get("pressed").Rgb(),
                s.Get("dark").Rgb(), s.Get("light").Rgb(), s.Get("shadow").Rgb());
    }

    public sealed record SettingsScreenData(int File, int Background, int Marble, SettingsTabs Tabs)
    {
        internal static SettingsScreenData From(Section s) =>
            new(s.Get("file").Int(), s.Get("background").Int(), s.Get("marble").Int(), SettingsTabs.From(s.Get("tabs")));
    }

    /// <summary>The topic tabs' icons, by tab id: bright and dark.</summary>
    public sealed record SettingsTabs(int File, IReadOnlyDictionary<string, int[]> Icons)
    {
        internal static SettingsTabs From(Section s) => new(s.Get("file").Int(),
            s.Props().Where(p => p.Name != "file").ToDictionary(p => p.Name, p => p.Value.Ints()));
    }

    public sealed record CampaignScreenData(int File, int Background, GuiRef Marble, Rectangle Heading, int HeadingSize,
        IReadOnlyDictionary<string, CampaignSideLayout> Sides)
    {
        internal static CampaignScreenData From(Section s)
        {
            var marble = s.Get("marble");
            return new(s.Get("file").Int(), s.Get("background").Int(),
                marble.Kind == JsonValueKind.Array ? marble.Gui() : new GuiRef(s.Get("file").Int(), marble.Int()),
                s.Get("heading").Rect(), s.Has("headingSize") ? s.Get("headingSize").Int() : 15,
                s.Get("sides").Map(CampaignSideLayout.From));
        }
    }

    /// <summary>One side of a campaign: its picture and name field, the brighter picture under the mouse (if any),
    /// the text key for the name field (if not the side's name).</summary>
    public sealed record CampaignSideLayout(Rectangle Picture, Rectangle Name, SettlerRef? Lit, string? Label)
    {
        internal static CampaignSideLayout From(Section s) =>
            new(s.Get("picture").Rect(), s.Get("name").Rect(),
                s.Has("lit") ? SettlerRefFrom(s.Get("lit")) : null, s.Has("label") ? s.Get("label").Str() : null);

        internal static SettlerRef SettlerRefFrom(Section s) => new(s.Int(0), s.Int(1), s.Int(2));
    }

    public sealed record MissionScreenData(int File, int Background, int Marble, Rectangle Heading, Rectangle List)
    {
        internal static MissionScreenData From(Section s) =>
            new(s.Get("file").Int(), s.Get("background").Int(), s.Get("marble").Int(), s.Get("heading").Rect(), s.Get("list").Rect());
    }

    /// <summary>The start screen's background and the pieces drawn over it (the scroll bar's the list screens share).</summary>
    public sealed record StartScreenData(int File, int Background, int Marble, int Sheet, int RowBar, int ScrollTop,
        int ScrollTrack, int ScrollBottom, Rectangle Oval, Rectangle OvalPressed, Rectangle Handle)
    {
        internal static StartScreenData From(Section s) =>
            new(s.Get("file").Int(), s.Get("background").Int(), s.Get("marble").Int(), s.Get("sheet").Int(), s.Get("rowBar").Int(),
                s.Get("scrollTop").Int(), s.Get("scrollTrack").Int(), s.Get("scrollBottom").Int(), s.Get("oval").Rect(),
                s.Get("ovalPressed").Rect(), s.Get("handle").Rect());
    }

    /// <summary>The building panel's icon indices (GUI images of file 03).</summary>
    public sealed record BuildingInfoIcons(int Demolish, int Pause, int Paused, int Priority, int PriorityOn)
    {
        internal static BuildingInfoIcons From(Section s) =>
            new(s.Get("demolish").Int(), s.Get("pause").Int(), s.Get("paused").Int(), s.Get("priority").Int(), s.Get("priorityOn").Int());
    }

    /// <summary>A market's panel: the route button strip, harbor strip, and the all/none icons.</summary>
    public sealed record MarketIcons(int Route, int SeaRoute, int All, int None)
    {
        internal static MarketIcons From(Section s) =>
            new(s.Get("route").Int(), s.Get("seaRoute").Int(), s.Get("all").Int(), s.Get("none").Int());
    }

    public sealed record ShipsData(int File, IReadOnlyDictionary<string, IReadOnlyDictionary<string, ShipLook>> Peoples)
    {
        internal static ShipsData From(Section s) => new(s.Get("file").Int(), s.Get("peoples").Map(p => p.Map(ShipLook.From)));
    }

    /// <summary>A ship's two sequences: the whole ship and its sail.</summary>
    public sealed record ShipLook(int Ship, int Sail)
    {
        internal static ShipLook From(Section s) => new(s.Get("ship").Int(), s.Get("sail").Int());
    }

    public sealed record DockyardData(int Dock, IReadOnlyDictionary<string, GuiRef> Ships, int[] ShipSteps)
    {
        internal static DockyardData From(Section s) =>
            new(s.Get("dock").Int(), s.Get("ships").Map(v => v.Gui()), s.Get("shipSteps").Ints());
    }

    public sealed record WarMachinesData(IReadOnlyDictionary<string, WarMachineLook> Peoples)
    {
        internal static WarMachinesData From(Section s) => new(s.Map(WarMachineLook.From));
    }

    /// <summary>How a people's war machines are drawn: sequences of its soldier file, and the works' icons.</summary>
    public sealed record WarMachineLook(int File, int Body, int? Wheels, int Fire, int? Shot, bool ShotPerDirection,
        bool Walks, GuiRef Icons)
    {
        internal static WarMachineLook From(Section s) =>
            new(s.Get("file").Int(), s.Get("body").Int(), s.Has("wheels") ? s.Get("wheels").Int() : null,
                s.Get("fire").Int(), s.Has("shot") ? s.Get("shot").Int() : null,
                s.Has("shotPerDirection") && s.Get("shotPerDirection").Bool(),
                s.Has("walks") && s.Get("walks").Bool(), s.Get("icons").Gui());
    }

    /// <summary>The spell status symbols: the file, and per spell the settler sequence.</summary>
    public sealed record StatusIconsData(int File, IReadOnlyDictionary<string, int> Sequences)
    {
        internal static StatusIconsData From(Section s) => new(s.Get("file").Int(),
            s.Props().Where(p => p.Name != "file").ToDictionary(p => p.Name, p => p.Value.Int()));
    }

    /// <summary>Per people the large icon of its top spell (each further spell three lower).</summary>
    public sealed record SpellMenuData(IReadOnlyDictionary<string, GuiRef> ByPeople)
    {
        internal static SpellMenuData From(Section s) => new(s.Map(v => v.Gui()));
    }

    /// <summary>Siedler › Berufe: the share spinner, the find buttons and the convert buttons.</summary>
    public sealed record ProfessionsMenuData(int Spinner, int[] Find, int[] Convert)
    {
        internal static ProfessionsMenuData From(Section s) =>
            new(s.Get("spinner").Int(), s.Get("find").Ints(), s.Get("convert").Ints());
    }

    /// <summary>Waren › Warentransport: the buttons below the goods.</summary>
    public sealed record TransportMenuData(int[] Buttons)
    {
        internal static TransportMenuData From(Section s) => new(s.Get("buttons").Ints());
    }

    /// <summary>Waren › Verteilung: the goods with more than one kind of user, in order.</summary>
    public sealed record DistributionMenuData(IReadOnlyList<string> Goods)
    {
        internal static DistributionMenuData From(Section s) => new(s.Get("goods").Items().Select(v => v.Str()).ToList());
    }

    public sealed record GoodsGridData(IReadOnlyList<IReadOnlyList<GoodsEntry>> Goods)
    {
        internal static GoodsGridData From(Section s) => new(s.Get("goods").Items().Select(r => r.Items().Select(GoodsEntry.From).ToList()).ToList());
    }

    /// <summary>A good of the grid; the icon is the people's own weapon when null.</summary>
    public sealed record GoodsEntry(string Good, GuiRef? Icon)
    {
        internal static GoodsEntry From(Section s) =>
            new(s.Get("good").Str(), s.Get("icon").Kind == JsonValueKind.Array ? s.Get("icon").Gui() : null);
    }

    public sealed record PortraitsData(IReadOnlyDictionary<string, PeoplePortraits> Peoples, CommonPortraits Common)
    {
        internal static PortraitsData From(Section s) =>
            new(s.Props().Where(p => p.Name != "common").ToDictionary(p => p.Name, p => PeoplePortraits.From(p.Value)),
                s.Has("common") ? CommonPortraits.From(s.Get("common")) : null!);
    }

    /// <summary>A people's portraits: its file, the kinds' indices (by uppercase key) and the first soldier's.</summary>
    public sealed record PeoplePortraits(int File, IReadOnlyDictionary<string, int> Types, int Soldiers)
    {
        internal static PeoplePortraits From(Section s) =>
            new(s.Get("file").Int(), UpperKeys(s), s.Get("soldiers").Int());

        internal static IReadOnlyDictionary<string, int> UpperKeys(Section s) =>
            s.Props().Where(p => p.Name.ToUpperInvariant() == p.Name).ToDictionary(p => p.Name, p => p.Value.Int());
    }

    /// <summary>Portraits common to every people; entries may name their own file.</summary>
    public sealed record CommonPortraits(int File, IReadOnlyDictionary<string, GuiRef> Entries)
    {
        internal static CommonPortraits From(Section s) => new(s.Get("file").Int(),
            s.Props().Where(p => p.Name.ToUpperInvariant() == p.Name)
                .ToDictionary(p => p.Name, p => p.Value.Kind == JsonValueKind.Array ? p.Value.Gui() : new GuiRef(s.Get("file").Int(), p.Value.Int())));
    }

    public sealed record SettlersOverviewData(OverviewIcons Icons, IReadOnlyList<IReadOnlyList<string>> Rows)
    {
        internal static SettlersOverviewData From(Section s) =>
            new(OverviewIcons.From(s.Get("icons")),
                s.Get("rows").Items().Select(r => r.Items().Select(v => v.Str()).ToList()).ToList());
    }

    /// <summary>The header icons of Siedler › Siedlerstatistik.</summary>
    public sealed record OverviewIcons(int Room, int Needing, int Total)
    {
        internal static OverviewIcons From(Section s) =>
            new(s.Get("room").Int(), s.Get("needing").Int(), s.Get("total").Int());
    }

    /// <summary>A category of the build menu: its button (if it has one) and buildings, in order.</summary>
    public sealed record BuildCategory(string Name, int[]? Tab, IReadOnlyList<string> Buildings)
    {
        internal static BuildCategory From(Section s) =>
            new(s.Get("name").Str(), s.Has("tab") ? s.Get("tab").Ints() : null,
                s.Get("buildings").Items().Select(b => b.Str()).ToList());
    }

    /// <summary>What the simulation's status text contains → the state shown (<c>buildingStatus.*</c>).</summary>
    public sealed record StatusText(string Contains, string State);

    /// <summary>One branch of <c>ui.json</c>, remembering every path it resolves (see <see cref="ReadPaths"/>).</summary>
    internal sealed class Section(JsonElement element, string path, List<string> read)
    {
        public JsonElement Element { get; } = element;
        public string Path { get; } = path;

        public JsonValueKind Kind => Element.ValueKind;

        public Section Get(string name)
        {
            if (!Element.TryGetProperty(name, out var child))
                throw new KeyNotFoundException($"ui.json has no '{(Path.Length == 0 ? name : Path + "." + name)}'");
            var childPath = Path.Length == 0 ? name : Path + "." + name;
            read.Add(childPath);
            return new Section(child, childPath, read);
        }

        public bool Has(string name) => Element.TryGetProperty(name, out _);

        public int Int() => Element.GetInt32();
        public int Int(int i) => Element[i].GetInt32();
        public float Float() => Element.GetSingle();
        public bool Bool() => Element.GetBoolean();
        public string Str() => Element.GetString()!;
        public string Str(int i) => Element[i].GetString()!;

        public int[] Ints() => Element.EnumerateArray().Select(e => e.GetInt32()).ToArray();

        public Rectangle Rect() => new(Int(0), Int(1), Int(2), Int(3));

        public GuiRef Gui() => new(Int(0), Int(1));

        public Color Rgb() => new(Int(0), Int(1), Int(2));

        public IReadOnlyDictionary<string, TValue> Map<TValue>(Func<Section, TValue> parse) =>
            Props().ToDictionary(p => p.Name, p => parse(p.Value));

        /// <summary>The properties of an object, without the <c>_</c>-prefixed comments.</summary>
        public IEnumerable<(string Name, Section Value)> Props()
        {
            foreach (var property in Element.EnumerateObject())
            {
                if (property.Name.StartsWith('_'))
                    continue;
                var childPath = Path.Length == 0 ? property.Name : Path + "." + property.Name;
                read.Add(childPath);
                yield return (property.Name, new Section(property.Value, childPath, read));
            }
        }

        public IEnumerable<Section> Items() => Element.EnumerateArray().Select(e => new Section(e, Path, read));
    }
}
