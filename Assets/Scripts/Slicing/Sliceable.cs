using UnityEngine;

namespace LoveAtFirstBite.Slicing
{
    /// <summary>
    /// Marks a food object as cuttable by the knife. Put this on each Ingredient that
    /// should be sliceable. The KnifeSlicer only cuts objects that have this component.
    ///
    /// Requirements for the object this is on (or a child):
    ///  - a MeshFilter with a readable mesh (run Tools > Love at First Bite > Make
    ///    Ingredient Meshes Readable once so the slicer can read vertices at runtime),
    ///  - a Collider (so the knife's trigger can detect it).
    /// </summary>
    public class Sliceable : MonoBehaviour
    {
        [Tooltip("How many times this piece (and its children) may be cut before it stops slicing. Prevents endless shards.")]
        public int maxCuts = 2;

        [HideInInspector] public int cutsSoFar;
    }
}
