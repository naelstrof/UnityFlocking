using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct Foliage {
    public enum FoliageType {
        Filler,
        Spiller,
        Thriller,
    }

    public FoliageType foliageType;
    public Mesh mesh;
}
