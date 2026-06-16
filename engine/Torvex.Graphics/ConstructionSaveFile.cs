using System.Collections.Generic;

namespace Torvex.Graphics;

public sealed class ConstructionSaveFile
{
    public int Version { get; set; } = 1;

    public List<ConstructionPieceSaveData> Pieces { get; set; } = [];
}

public sealed class ConstructionPieceSaveData
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public float YawRadians { get; set; }

    public bool IsVertical { get; set; }
}
