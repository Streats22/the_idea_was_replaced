using System.Globalization;
using Delvework.Core.Content;
using Delvework.Core.Progress;
using Delvework.Game.Screens;
using Godot;

namespace Delvework.Game;

/// <summary>
/// Headless checks and screenshots, run with <c>-- --smoke</c> or
/// <c>-- --screenshot=out.png --screen=town|title|codex|skills|lesson|delve|workshop|almanac|commissions|commission
/// [--progress=N] [--gold=N] [--stock=N] [--lesson=id] [--page=N] [--site=id] [--tree=Village] [--workshop=id]
/// [--commission=id] [--script=file] [--tick=N] [--solution] [--result] [--time=0..1] [--found] [--category=name]</c>.
/// Command-line runs use a throwaway profile and never touch the player's save.
/// </summary>
public static class CommandLine
{
    public static async Task Run(App app, string[] args)
    {
        var tree = app.GetTree();
        try
        {
            var opts = args.Select(a => a.TrimStart('-').Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "", StringComparer.Ordinal);
            int Int(string key, int fallback) => opts.TryGetValue(key, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

            if (opts.ContainsKey("smoke"))
            {
                await Smoke(app);
                tree.Quit(0);
                return;
            }
            if (!opts.TryGetValue("screenshot", out var path)) throw new ArgumentException("Expected --smoke or --screenshot=path");

            Progress(app, Int("progress", 0), Int("gold", 137), Int("stock", 14));
            if (opts.ContainsKey("found")) FindMost(app);
            var chosen = opts.TryGetValue("lesson", out var lid) ? app.Content.Lessons.First(l => l.Id == lid) : null;
            var lesson = chosen ?? app.Progress.NextChallenge ?? app.Content.Lessons[0];
            switch (opts.GetValueOrDefault("screen", "town"))
            {
                case "title":
                    app.GoTitle();
                    break;
                case "codex":
                    app.GoCodex(chosen, Int("page", 0));
                    break;
                case "reference":
                    app.GoCodex(chosen).ShowReference();
                    break;
                case "skills":
                    app.GoSkills(Enum.Parse<SkillTree>(opts.GetValueOrDefault("tree", "Village")));
                    break;
                case "lesson":
                case "delve":
                {
                    if (opts.ContainsKey("solution")) app.Profile.Programs[Profile.LessonSlot(lesson.Id, lesson.Challenge.Party[0])] = lesson.Challenge.Solution;
                    var site = opts.TryGetValue("site", out var sid) ? app.Content.Sites.First(s => s.Id == sid) : app.Progress.OpenSites.LastOrDefault();
                    var screen = opts["screen"] == "lesson" || site is null ? app.GoLesson(lesson) : app.GoSite(site);
                    await Frames(app, 2);
                    screen.Run(apply: opts.ContainsKey("result"));
                    var last = screen.Timeline!.LastTick;
                    screen.SetPlayhead(Math.Min(Int("tick", last), last));
                    await Frames(app, 4);
                    screen.SetPlayhead(Math.Min(Int("tick", last), last) + 0.4);
                    break;
                }
                case "workshop":
                {
                    var def = app.Content.Workshops.First(w => w.Id == opts.GetValueOrDefault("workshop", "smelter"));
                    if (opts.TryGetValue("script", out var script)) app.Profile.Programs[Profile.WorkshopSlot(def.Id)] = File.ReadAllText(script);
                    var screen = app.GoWorkshop(def);
                    await Frames(app, 3);
                    var last = screen.Result!.LastTick;
                    screen.SetPlayhead(Math.Min(Int("tick", last), last));
                    break;
                }
                case "almanac":
                    app.GoAlmanac(opts.GetValueOrDefault("category"));
                    break;
                case "commissions":
                    app.GoCommissions();
                    break;
                case "commission":
                {
                    var c = app.Content.Commissions.First(x => x.Id == opts.GetValueOrDefault("commission", app.Content.Commissions[0].Id));
                    var w = app.Content.Workshops.First(x => x.Id == c.Workshop);
                    if (opts.ContainsKey("solution")) app.Profile.Programs[Commissions.Slot(c)] = c.Solution;
                    var screen = app.Show(new WorkshopScreen { App = app, Workshop = w, Commission = c });
                    await Frames(app, 3);
                    var last = screen.Result!.LastTick;
                    screen.SetPlayhead(Math.Min(Int("tick", last), last));
                    break;
                }
                default:
                {
                    var town = app.GoTown();
                    if (opts.TryGetValue("time", out var time))
                    {
                        town.Town.TimeOfDay = float.Parse(time, CultureInfo.InvariantCulture);
                        town.Town.Frozen = true;
                    }
                    break;
                }
            }
            await Frames(app, 2);
            foreach (var w in opts.GetValueOrDefault("windows", "").Split(',', StringSplitOptions.RemoveEmptyEntries)) OpenWindow(app, w);
            await Frames(app, 20);
            await app.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            app.GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"screenshot: {path}");
            tree.Quit(0);
        }
#pragma warning disable CA1031 // Any failure must fail the command-line run.
        catch (Exception e)
#pragma warning restore CA1031
        {
            GD.PushError(e.ToString());
            tree.Quit(1);
        }
    }

    private static async Task Frames(App app, int n)
    {
        for (var i = 0; i < n; i++) await app.ToSignal(app.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>A help window by name: a lesson id, a function name, "functions" or "howto".</summary>
    private static void OpenWindow(App app, string name)
    {
        if (name == "functions") Help.Functions(app);
        else if (name == "howto") Help.HowToPlay(app);
        else if (app.Content.Lessons.FirstOrDefault(l => l.Id == name) is { } l) Help.Topic(app, l);
        else if (app.Content.Workshops.FirstOrDefault(w => w.Id == name) is { } w) Help.Workshop(app, w);
        else if (!Help.Word(app, name)) throw new ArgumentException($"No help window called '{name}'");
    }

    /// <summary>A profile that has learned (and passed the challenges of) <paramref name="lessons"/> features and bought everything it could along the way.</summary>
    private static void Progress(App app, int lessons, int gold, int stock)
    {
        var p = app.Progress;
        foreach (var l in p.Lessons.Take(lessons))
        {
            app.Profile.Gold = l.Cost;
            if (!p.IsLearned(l) && !p.Learn(l)) throw new InvalidOperationException($"Could not learn {l.Id}");
            p.Complete(l);
        }
        app.Profile.Gold = 100_000;
        Fill(app, 100_000);
        bool bought;
        do
        {
            bought = false;
            foreach (var n in p.Content.Skills)
            {
                if (lessons > 0 && p.Status(n) == NodeStatus.Available && p.Buy(n)) bought = true;
            }
        }
        while (bought);
        app.Profile.Gold = gold;
        Fill(app, stock);
    }

    /// <summary>Find most of the Almanac: two of every three entries, and monsters at every stage of knowledge.</summary>
    private static void FindMost(App app)
    {
        var p = app.Progress;
        var i = 0;
        foreach (var e in p.AlmanacEntries.Where(e => e.Category != Almanac.MonsterCategory))
        {
            if (i++ % 3 != 2) app.Profile.Found[e.Key] = Math.Max(app.Profile.Found.GetValueOrDefault(e.Key), e.Count);
        }
        var kills = new[] { 14, 5, 1, 12, 0, 3, 7, 0, 2, 11 };
        var k = 0;
        foreach (var m in app.Content.Monsters.Values.OrderBy(m => m.Hp).ThenBy(m => m.Id, StringComparer.Ordinal))
        {
            var n = kills[k++ % kills.Length];
            if (n == 0 && k % 2 == 0) continue;
            app.Profile.Found["seen:" + m.Id] = 1;
            if (n > 0) app.Profile.Found["killed:" + m.Id] = n;
        }
        p.Discover(null);
        p.TakeFresh();
    }

    private static void Fill(App app, int amount)
    {
        app.Profile.Stock.Clear();
        foreach (var id in Resources.All) app.Profile.Add(id, amount);
    }

    private static async Task Smoke(App app)
    {
        app.GoTitle();
        await Frames(app, 3);
        app.GoTown();
        await Frames(app, 3);
        var p = app.Progress;

        var first = app.GoSite(p.OpenSites.First());
        await Frames(app, 1);
        first.Run(apply: true);
        first.ShowResultNow();
        await Frames(app, 1);
        if (first.Timeline!.Outcome.Loot <= 0) throw new InvalidOperationException("The first program brought no gold home.");
        if (app.Profile.Amount(Resources.Ore) <= 0) throw new InvalidOperationException("The first program mined no ore.");
        GD.Print($"smoke: first delve with the starter program: {first.Timeline.Outcome.Summary}, {app.Profile.Gold} gold, {Resources.Format(app.Profile.Stock)}");

        Help.HowToPlay(app);
        Help.Functions(app);
        foreach (var b in Delvework.Core.Sim.GolemApi.Environment.Builtins.Where(b => b.Signature.Length > 0)) Help.Word(app, b.Name);
        foreach (var word in new[] { "while", "if", "def", "for", "on" }) Help.Word(app, word);
        await Frames(app, 2);
        GD.Print($"smoke: {app.WindowCount} help windows opened");

        foreach (var lesson in app.Content.Lessons)
        {
            if (!p.IsLearned(lesson))
            {
                app.GoCodex(lesson);
                await Frames(app, 1);
                app.Profile.Gold = Math.Max(app.Profile.Gold, lesson.Cost);
                if (!app.Learn(lesson)) throw new InvalidOperationException($"Could not learn {lesson.Id}.");
                await Frames(app, 1);
            }
            for (var page = 0; page <= lesson.Pages.Count; page++)
            {
                app.GoCodex(lesson, page);
                await Frames(app, 1);
            }
            foreach (var id in lesson.Challenge.Party) app.Profile.Programs[Profile.LessonSlot(lesson.Id, id)] = lesson.Challenge.Solution;
            var screen = app.GoLesson(lesson);
            await Frames(app, 1);
            screen.Run(apply: true);
            for (var t = 0; t <= screen.Timeline!.LastTick; t += 9) screen.SetPlayhead(t + 0.5);
            await Frames(app, 1);
            screen.ShowResultNow();
            await Frames(app, 1);
            if (!p.IsCompleted(lesson)) throw new InvalidOperationException($"Lesson {lesson.Id}: the solution did not pass in the game.");
            GD.Print($"smoke: lesson {lesson.Tier} {lesson.Title}: passed, {app.Profile.Gold} gold");
        }
        app.Profile.Gold = 100_000;
        Fill(app, 100_000);
        foreach (var tree in app.Content.Trees)
        {
            var skills = app.GoSkills(tree.Id);
            await Frames(app, 1);
            foreach (var n in app.Content.Skills.Where(n => n.Tree == tree.Id).OrderBy(n => n.Col)) p.Buy(n);
            skills.Refresh();
            await Frames(app, 1);
        }
        foreach (var n in app.Content.Skills) p.Buy(n);
        app.GoTown();
        await Frames(app, 3);
        Fill(app, 20);
        foreach (var w in app.Content.Workshops)
        {
            if (!p.IsBuilt(w)) throw new InvalidOperationException($"The {w.Name} was not built.");
            var screen = app.GoWorkshop(w);
            await Frames(app, 2);
            Help.Workshop(app, w);
            for (var t = 0; t <= screen.Result!.LastTick; t += 5) screen.SetPlayhead(t + 0.5);
            await Frames(app, 1);
            var before = app.Profile.Amount(Core.Village.Workshops.KindOf(w).Outputs[0]);
            var result = p.Shift(w);
            if (result.Error is not null || result.Made.Count == 0) throw new InvalidOperationException($"{w.Name} starter made nothing: {result.Summary}");
            if (app.Profile.Amount(Core.Village.Workshops.KindOf(w).Outputs[0]) <= before) throw new InvalidOperationException($"{w.Name} shift did not reach the stock.");
            GD.Print($"smoke: {result.Summary}");
        }
        var board = app.GoCommissions();
        await Frames(app, 2);
        foreach (var c in app.Content.Commissions)
        {
            var w = app.Content.Workshops.First(x => x.Id == c.Workshop);
            app.Profile.Programs[Commissions.Slot(c)] = c.Solution;
            var screen = app.Show(new WorkshopScreen { App = app, Workshop = w, Commission = c });
            await Frames(app, 2);
            for (var t = 0; t <= screen.Result!.LastTick; t += 11) screen.SetPlayhead(t + 0.5);
            var (score, paid) = p.SubmitCommission(c, c.Solution);
            if (!score.Passed || score.TicksMedal != Medal.Gold || score.LinesMedal != Medal.Gold) throw new InvalidOperationException($"Commission {c.Id}: the solution did not earn both golds in the game: {score.Summary}");
            GD.Print($"smoke: commission {score.Summary} paid {Resources.Format(paid)}");
        }
        _ = board;
        app.ToastFinds();
        foreach (var m in app.Content.Monsters.Keys) app.Profile.Found["killed:" + m] = Almanac.MasterKills;
        p.Discover(null);
        app.ToastFinds();
        var almanac = app.GoAlmanac();
        await Frames(app, 2);
        foreach (var e in p.AlmanacEntries.Select(e => e.Category).Distinct())
        {
            almanac.Show(e);
            await Frames(app, 1);
        }
        GD.Print($"smoke: almanac {p.FoundCount} of {p.AlmanacEntries.Count} found");
        var town = app.GoTown();
        for (var t = 0f; t < 1f; t += 0.125f)
        {
            town.Town.TimeOfDay = t;
            await Frames(app, 1);
        }
        foreach (var site in app.Content.Sites)
        {
            var screen = app.GoSite(site);
            await Frames(app, 1);
            screen.Run(apply: true);
            for (var t = 0; t <= screen.Timeline!.LastTick; t += 13) screen.SetPlayhead(t + 0.5);
            screen.ShowResultNow();
            await Frames(app, 1);
            GD.Print($"smoke: delve {site.Name}: {screen.Timeline.Outcome.Summary}");
        }
        GD.Print($"smoke: done, {p.OwnedNodes.Count()} skills, {app.Profile.Gold} gold, {Resources.Format(app.Profile.Stock)}, {app.Profile.Shifts} workshop shifts");
    }
}
