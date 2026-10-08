using UnityEngine;
using UnityEngine.UI;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Draws a rounded-rect border split into up to 4 angular slices, one per named pair
    /// colour, solid where that colour may pass and dashed where it may not. Backs
    /// <see cref="FreeFlow.Enums.BlockType.ForbiddenForPair"/> and
    /// <see cref="FreeFlow.Enums.BlockType.AllowedForPairs"/> in place of the old ring sprite
    /// plus a hand-cut half-arc, which topped out at two colours because the arc was a single
    /// fixed sprite.
    ///
    /// Needs its own material instance because a CanvasRenderer graphic ignores
    /// MaterialPropertyBlock -- acceptable here since permission cells are rare per board.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class PermissionBorderView : MonoBehaviour
    {
        public const int MaxSegments = 4;

        private static readonly int SegmentCountId = Shader.PropertyToID("_SegmentCount");
        private static readonly int AllowedId = Shader.PropertyToID("_Allowed");
        private static readonly int[] ColorIds =
        {
            Shader.PropertyToID("_Color0"),
            Shader.PropertyToID("_Color1"),
            Shader.PropertyToID("_Color2"),
            Shader.PropertyToID("_Color3"),
        };

        private Image image;
        private Material materialInstance;

        // Lazy rather than Awake-driven: this component sits on a GameObject that starts
        // inactive, and SetSegments activates it and uses the material in the same call --
        // relying on Awake to have already run by then is exactly the kind of activation-order
        // assumption that breaks under editor tooling and isn't worth risking at runtime either.
        private void EnsureMaterialInstance()
        {
            if (materialInstance != null) { return; }

            if (image == null) { image = GetComponent<Image>(); }

            // A null sprite gives the mesh a clean 0..1 UV across the whole rect, which is
            // what the shader's rounded-rect math assumes -- a packed/trimmed sprite would
            // offset or pad that space.
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;

            materialInstance = Instantiate(image.material);
            image.material = materialInstance;
        }

        /// <summary>
        /// Sets the border to <paramref name="colors"/>.Length slices (clamped to
        /// <see cref="MaxSegments"/>), each solid when its matching <paramref name="allowed"/>
        /// entry is true and dashed otherwise, and activates the GameObject.
        /// </summary>
        public void SetSegments(Color[] colors, bool[] allowed)
        {
            EnsureMaterialInstance();
            gameObject.SetActive(true);

            int count = Mathf.Clamp(colors.Length, 1, MaxSegments);
            Vector4 allowedFlags = Vector4.one;

            for (int i = 0; i < MaxSegments; i++)
            {
                Color slice = i < count ? colors[i] : Color.clear;
                materialInstance.SetColor(ColorIds[i], slice);
                if (i < count) { allowedFlags[i] = allowed[i] ? 1f : 0f; }
            }

            materialInstance.SetFloat(SegmentCountId, count);
            materialInstance.SetVector(AllowedId, allowedFlags);
        }
    }
}
