using System;
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

    public bool Equals(Foliage other) {
        return foliageType == other.foliageType && Equals(mesh, other.mesh);
    }

    public override int GetHashCode() {
        return HashCode.Combine((int)foliageType, mesh);
    }
}
