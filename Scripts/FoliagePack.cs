using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Foliage Pack", menuName = "Data/Foliage Pack", order = 0)]
public class FoliagePack : ScriptableObject {
    public List<Foliage> foliages;
    public Material material;
}
