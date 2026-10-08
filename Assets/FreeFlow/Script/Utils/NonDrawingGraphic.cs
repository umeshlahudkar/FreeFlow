using UnityEngine;
using UnityEngine.UI;

namespace FreeFlow.Util
{
    /// <summary>
    /// A Graphic that occupies a rect and can be hit by a pointer, but emits no geometry.
    ///
    /// The use case is an invisible hit area: an Image with alpha 0 still builds a full quad,
    /// still sits in the canvas batch and still gets rasterised and alpha-blended, so a board of
    /// 81 cells pays for 81 screen-area quads that draw nothing at all. Emitting no mesh removes
    /// all of that while leaving the RectTransform -- and therefore the hit test, which works off
    /// the rect and not the mesh -- untouched.
    ///
    /// WHETHER THIS ACTUALLY KEEPS RAYCASTING is version-dependent and must be verified, not
    /// assumed: GraphicRaycaster skips any graphic whose `depth` is -1, and depth is -1 exactly
    /// when the canvas did not draw it. See the comment on GamePlayController.BlockFromHit, which
    /// records the same hazard for the alpha-0 Image this replaces.
    /// </summary>
    public class NonDrawingGraphic : MaskableGraphic
    {
        protected NonDrawingGraphic() { useLegacyMeshGeneration = false; }

        /// <summary>Nothing to rebuild: there is no material to assign when nothing is drawn.</summary>
        public override void SetMaterialDirty() { }

        /// <summary>Nothing to rebuild: the mesh is always empty, so a vertex rebuild is wasted
        /// work -- and this is the point of the class, since it is what keeps this graphic out of
        /// the canvas's per-frame batch rebuild.</summary>
        public override void SetVerticesDirty() { }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }
    }
}
