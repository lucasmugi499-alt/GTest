using Cascade.Sim;
using Godot;

// Placeholder scene. M5 replaces this with the map, Cascade view, Brief and readouts.
public partial class Main : Control
{
    public override void _Ready()
    {
        AddChild(new Label
        {
            Text = $"CASCADE · {SimInfo.Slice}\n{SimInfo.Milestone}: project skeleton. Nothing to play yet.",
            Position = new Vector2(24, 24),
        });
        GD.Print($"[cascade] Godot loaded {SimInfo.Name} ({SimInfo.Milestone})");
    }
}
