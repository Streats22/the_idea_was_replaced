using Godot;

namespace Delvework.Game.View3D;

public readonly record struct OmniSpot(Vector3 Pos, Color Color, float Energy, float Range, float Phase);

/// <summary>Fixed budget of shadowless omni lights, assigned to the nearest spots each frame.</summary>
public sealed class OmniLightPool
{
    private readonly OmniLight3D[] _lights;

    public OmniLightPool(Node parent, Color color, float energy, float range, int budget = Graphics.MaxOmniLights)
    {
        var holder = new Node3D { Name = "OmniLights" };
        parent.AddChild(holder);
        _lights = new OmniLight3D[budget];
        for (var i = 0; i < budget; i++)
        {
            _lights[i] = Iso.Light(holder, Vector3.Zero, color, energy, range);
            _lights[i].Visible = false;
        }
    }

    public int Budget => _lights.Length;

    public void Hide()
    {
        foreach (var light in _lights) light.Visible = false;
    }

    public void AssignNearest(IReadOnlyList<OmniSpot> spots, Vector3 focus, float brightness, float time)
    {
        if (brightness <= 0.05f || spots.Count == 0)
        {
            Hide();
            return;
        }

        var take = Math.Min(_lights.Length, spots.Count);
        Span<int> order = spots.Count <= 64 ? stackalloc int[spots.Count] : new int[spots.Count];
        for (var i = 0; i < spots.Count; i++) order[i] = i;
        for (var i = 0; i < take; i++)
        {
            var best = i;
            var bestDist = spots[order[i]].Pos.DistanceSquaredTo(focus);
            for (var j = i + 1; j < spots.Count; j++)
            {
                var dist = spots[order[j]].Pos.DistanceSquaredTo(focus);
                if (dist < bestDist)
                {
                    best = j;
                    bestDist = dist;
                }
            }
            (order[i], order[best]) = (order[best], order[i]);
        }

        for (var i = 0; i < _lights.Length; i++)
        {
            var light = _lights[i];
            if (i >= take)
            {
                light.Visible = false;
                continue;
            }
            var spot = spots[order[i]];
            light.Position = spot.Pos;
            light.LightColor = spot.Color;
            light.OmniRange = spot.Range;
            light.LightEnergy = spot.Energy * brightness
                * (0.85f + 0.15f * Mathf.Sin(time * 11f + spot.Phase) + 0.08f * Mathf.Sin(time * 27f + spot.Phase));
            light.Visible = true;
        }
    }
}
