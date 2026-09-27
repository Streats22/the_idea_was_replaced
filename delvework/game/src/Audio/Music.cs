using Godot;

namespace Delvework.Game.Audio;

public enum Mood
{
    Silent,
    Title,
    Town,
    Dungeon,
}

public enum Sfx
{
    Click,
    Coin,
    Hit,
    Chest,
    Spell,
    Unlock,
    Fail,
    Break,
    Page,
    Descend,
}

/// <summary>The original score, composed in code. All town layers share one length so they stay in sync.</summary>
internal static class Compositions
{
    private const double TownBar = 3.0;
    private const int TownBars = 8;
    private const double Beat = TownBar / 4;

    // D, Bm, G, A, D, F#m, G, A as MIDI root + chord tones.
    private static readonly int[][] TownChords =
    [
        [50, 57, 62, 66], [47, 54, 59, 62], [43, 55, 59, 62], [45, 57, 61, 64],
        [50, 57, 62, 66], [42, 54, 57, 61], [43, 55, 59, 62], [45, 57, 61, 64],
    ];

    public static AudioStreamWav TownPad()
    {
        var s = new Synth(TownBar * TownBars);
        for (var bar = 0; bar < TownBars; bar++)
        {
            var c = TownChords[bar];
            for (var i = 1; i < c.Length; i++) s.Tone(bar * TownBar, TownBar * 0.95, Synth.Midi(c[i]), 0.1f, attack: 0.6, release: 1.2, bright: 0.12f, detune: 0.003f);
            s.Tone(bar * TownBar, TownBar * 0.9, Synth.Midi(c[0] - 12), 0.12f, attack: 0.2, release: 0.8, bright: 0.05f);
        }
        s.Echo(0.37, 0.3f, 0.25f);
        return s.ToStream(true);
    }

    public static AudioStreamWav TownArp()
    {
        var s = new Synth(TownBar * TownBars);
        int[] pattern = [1, 2, 3, 2, 1, 2, 3, 2];
        for (var bar = 0; bar < TownBars; bar++)
        {
            var c = TownChords[bar];
            for (var i = 0; i < 8; i++)
            {
                var note = c[pattern[i]] + (i >= 4 && bar % 2 == 1 ? 12 : 0);
                s.Pluck(bar * TownBar + i * Beat / 2, Synth.Midi(note), 0.22f, 1.4);
            }
        }
        s.Echo(0.375, 0.35f, 0.3f);
        return s.ToStream(true);
    }

    public static AudioStreamWav TownMelody()
    {
        var s = new Synth(TownBar * TownBars);
        // (beat, length in beats, midi) over 32 beats: a gentle folk tune in D major.
        (double At, double Len, int Note)[] tune =
        [
            (0, 1.5, 74), (1.5, 0.5, 76), (2, 1, 78), (3, 1, 76),
            (4, 2, 74), (6, 1, 71), (7, 1, 74),
            (8, 1.5, 71), (9.5, 0.5, 69), (10, 2, 67),
            (12, 1, 69), (13, 1, 71), (14, 2, 73),
            (16, 1.5, 74), (17.5, 0.5, 76), (18, 1, 78), (19, 1, 81),
            (20, 2, 78), (22, 1, 76), (23, 1, 73),
            (24, 1.5, 74), (25.5, 0.5, 71), (26, 2, 67),
            (28, 1, 69), (29, 1, 73), (30, 2, 74),
        ];
        foreach (var (at, len, note) in tune)
        {
            s.Tone(at * Beat, len * Beat * 0.92, Synth.Midi(note), 0.14f, attack: 0.04, release: 0.35, bright: 0.45f, vibrato: 0.004f);
        }
        s.Echo(0.375, 0.35f, 0.35f);
        return s.ToStream(true);
    }

    public static AudioStreamWav TownRhythm()
    {
        var s = new Synth(TownBar * TownBars);
        for (var bar = 0; bar < TownBars; bar++)
        {
            var root = TownChords[bar][0];
            for (var b = 0; b < 4; b++)
            {
                var t = bar * TownBar + b * Beat;
                if (b % 2 == 0) s.Drum(t, 0.35f, 60, 0.25, 0.1f);
                else s.Noise(t, 0.08, 0.12f, 0.8f);
                s.Pluck(t + Beat / 2, Synth.Midi(root - 12 + (b == 3 ? 7 : 0)), 0.3f, 0.8, 0.99f);
            }
        }
        return s.ToStream(true);
    }

    private const double DungeonLength = 32.0;

    public static AudioStreamWav DungeonDrone()
    {
        var s = new Synth(DungeonLength);
        // Two slow chords (A minor, F) under a low A, with bells dripping like water.
        s.Tone(0, DungeonLength, Synth.Midi(33), 0.22f, attack: 4, release: 4, bright: 0.02f, detune: 0.002f);
        s.Tone(0, 16, Synth.Midi(45), 0.08f, attack: 5, release: 5, bright: 0.02f, detune: 0.004f);
        s.Tone(0, 16, Synth.Midi(52), 0.06f, attack: 5, release: 5, bright: 0.02f, detune: 0.004f);
        s.Tone(16, 16, Synth.Midi(41), 0.08f, attack: 5, release: 5, bright: 0.02f, detune: 0.004f);
        s.Tone(16, 16, Synth.Midi(48), 0.06f, attack: 5, release: 5, bright: 0.02f, detune: 0.004f);
        (double At, int Note)[] bells = [(1.5, 81), (5.2, 76), (9.1, 84), (12.4, 79), (17.3, 77), (20.8, 72), (24.9, 81), (28.6, 76), (30.1, 88)];
        foreach (var (at, note) in bells) s.Bell(at, Synth.Midi(note), 0.07f, 1.8);
        for (var i = 0; i < 4; i++) s.Wind(i * 8 + 1, 7, 0.08f, 0.01f);
        s.Echo(0.61, 0.45f, 0.35f);
        return s.ToStream(true);
    }

    public static AudioStreamWav DungeonCombat()
    {
        var s = new Synth(DungeonLength);
        var beat = 60.0 / 120;
        int[] bass = [33, 33, 36, 33, 31, 31, 29, 31];
        for (var b = 0; b < (int)(DungeonLength / beat); b++)
        {
            var t = b * beat;
            if (b % 4 == 0 || b % 8 == 3) s.Drum(t, 0.55f, 55, 0.3, 0.2f);
            if (b % 4 == 2) s.Drum(t, 0.35f, 90, 0.15, 0.4f);
            s.Noise(t + beat / 2, 0.05, 0.06f, 0.9f);
            var note = bass[(b / 8) % bass.Length];
            if (b % 2 == 0) s.Tone(t, beat * 0.8, Synth.Midi(note), 0.18f, attack: 0.01, release: 0.1, bright: 0.6f);
        }
        return s.ToStream(true);
    }

    public static AudioStreamWav Effect(Sfx sfx)
    {
        switch (sfx)
        {
            case Sfx.Click:
            {
                var s = new Synth(0.06);
                s.Sweep(0, 0.05, 1400, 900, 0.3f);
                return s.ToStream(false);
            }
            case Sfx.Page:
            {
                var s = new Synth(0.25);
                s.Wind(0, 0.22, 0.4f, 0.25f);
                return s.ToStream(false);
            }
            case Sfx.Coin:
            {
                var s = new Synth(0.5);
                s.Bell(0, Synth.Midi(83), 0.4f, 0.12);
                s.Bell(0.07, Synth.Midi(88), 0.4f, 0.2);
                return s.ToStream(false);
            }
            case Sfx.Hit:
            {
                var s = new Synth(0.3);
                s.Drum(0, 0.6f, 110, 0.06, 0.8f);
                s.Noise(0, 0.12, 0.35f, 0.5f);
                return s.ToStream(false);
            }
            case Sfx.Chest:
            {
                var s = new Synth(0.9);
                s.Noise(0, 0.1, 0.25f, 0.2f);
                int[] notes = [76, 79, 83, 88];
                for (var i = 0; i < notes.Length; i++) s.Bell(0.08 + i * 0.06, Synth.Midi(notes[i]), 0.25f, 0.3);
                return s.ToStream(false);
            }
            case Sfx.Spell:
            {
                var s = new Synth(0.8);
                s.Sweep(0, 0.5, 400, 1400, 0.25f);
                s.Sweep(0.05, 0.5, 600, 2100, 0.12f);
                s.Wind(0, 0.7, 0.25f, 0.3f);
                return s.ToStream(false);
            }
            case Sfx.Unlock:
            {
                var s = new Synth(1.8);
                int[] notes = [62, 66, 69, 74];
                for (var i = 0; i < notes.Length; i++)
                {
                    s.Tone(i * 0.12, 0.9 - i * 0.12, Synth.Midi(notes[i]), 0.18f, attack: 0.01, release: 0.6, bright: 0.5f);
                    s.Bell(i * 0.12, Synth.Midi(notes[i] + 12), 0.12f, 0.6);
                }
                return s.ToStream(false);
            }
            case Sfx.Fail:
            {
                var s = new Synth(1.0);
                s.Tone(0, 0.25, Synth.Midi(64), 0.2f, attack: 0.01, release: 0.2, bright: 0.4f);
                s.Tone(0.28, 0.45, Synth.Midi(60), 0.2f, attack: 0.01, release: 0.3, bright: 0.4f);
                return s.ToStream(false);
            }
            case Sfx.Break:
            {
                var s = new Synth(0.8);
                s.Drum(0, 0.7f, 45, 0.3, 0.6f);
                s.Noise(0.02, 0.5, 0.4f, 0.15f);
                return s.ToStream(false);
            }
            default:
            {
                var s = new Synth(1.2);
                s.Sweep(0, 1.0, 900, 200, 0.2f);
                s.Bell(0, Synth.Midi(69), 0.2f, 0.5);
                return s.ToStream(false);
            }
        }
    }
}

/// <summary>
/// Plays the score: town layers fade in as the village grows, the dungeon drone gets a combat
/// layer when monsters are close. Sound effects go through their own bus.
/// </summary>
public partial class AudioDirector : Node
{
    private readonly List<AudioStreamPlayer> _town = [];
    private AudioStreamPlayer _drone = null!, _combat = null!;
    private readonly Dictionary<Sfx, AudioStreamWav> _sfx = [];
    private readonly List<AudioStreamPlayer> _voices = [];
    private Mood _mood = Mood.Silent;
    private int _townLevel;
    private float _combatTarget;
    private readonly Dictionary<Sfx, ulong> _lastPlayed = [];

    public static int Bus(string name)
    {
        var i = AudioServer.GetBusIndex(name);
        if (i >= 0) return i;
        AudioServer.AddBus();
        i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, name);
        AudioServer.SetBusSend(i, "Master");
        return i;
    }

    public override void _Ready()
    {
        Bus("Music");
        Bus("Sfx");
        AudioStreamPlayer Player(AudioStream s, string bus) => AddAndReturn(new AudioStreamPlayer { Stream = s, Bus = bus, VolumeDb = -80 });
        foreach (var s in new[] { Compositions.TownPad(), Compositions.TownArp(), Compositions.TownMelody(), Compositions.TownRhythm() })
        {
            _town.Add(Player(s, "Music"));
        }
        _drone = Player(Compositions.DungeonDrone(), "Music");
        _combat = Player(Compositions.DungeonCombat(), "Music");
        foreach (var sfx in Enum.GetValues<Sfx>()) _sfx[sfx] = Compositions.Effect(sfx);
        for (var i = 0; i < 8; i++) _voices.Add(AddAndReturn(new AudioStreamPlayer { Bus = "Sfx" }));
    }

    private T AddAndReturn<T>(T node)
        where T : Node
    {
        AddChild(node);
        return node;
    }

    public static void SetVolumes(double music, double sfx)
    {
        AudioServer.SetBusVolumeDb(Bus("Music"), Db(music));
        AudioServer.SetBusVolumeDb(Bus("Sfx"), Db(sfx));
    }

    private static float Db(double linear) => linear <= 0.001 ? -80 : (float)(20 * Math.Log10(linear));

    public void SetMood(Mood mood)
    {
        if (mood == _mood) return;
        _mood = mood;
        if (mood is Mood.Title or Mood.Town && !_town[0].Playing)
        {
            foreach (var p in _town) p.Play();
        }
        if (mood == Mood.Dungeon && !_drone.Playing)
        {
            _drone.Play();
            _combat.Play();
        }
    }

    /// <summary>Number of village buildings; more buildings, fuller town music.</summary>
    public void SetTownLevel(int buildings) => _townLevel = buildings;

    /// <summary>0 = calm, 1 = monsters right next to the party.</summary>
    public void SetCombat(float amount) => _combatTarget = Math.Clamp(amount, 0, 1);

    public void Play(Sfx sfx, float pitch = 1f)
    {
        var now = Time.GetTicksMsec();
        if (_lastPlayed.TryGetValue(sfx, out var last) && now - last < 45) return;
        _lastPlayed[sfx] = now;
        var voice = _voices.FirstOrDefault(v => !v.Playing) ?? _voices[0];
        voice.Stream = _sfx[sfx];
        voice.PitchScale = pitch;
        voice.Play();
    }

    public override void _Process(double delta)
    {
        float[] townTargets = _mood switch
        {
            Mood.Title => [1f, 0f, 0.8f, 0f],
            Mood.Town => [1f, _townLevel >= 1 ? 0.8f : 0.35f, _townLevel >= 3 ? 0.75f : 0f, _townLevel >= 6 ? 0.6f : 0f],
            _ => [0f, 0f, 0f, 0f],
        };
        for (var i = 0; i < _town.Count; i++) Fade(_town[i], townTargets[i], delta, 0.8);
        var dungeon = _mood == Mood.Dungeon;
        Fade(_drone, dungeon ? 1f : 0f, delta, 0.6);
        Fade(_combat, dungeon ? _combatTarget * 0.8f : 0f, delta, _combatTarget > 0.1f ? 2.0 : 0.4);
    }

    private static void Fade(AudioStreamPlayer p, float target, double delta, double speed)
    {
        var current = p.VolumeDb <= -79 ? 0 : Mathf.DbToLinear(p.VolumeDb);
        var next = Mathf.MoveToward(current, target, (float)(delta * speed));
        p.VolumeDb = next <= 0.001f ? -80 : Mathf.LinearToDb(next);
    }
}
