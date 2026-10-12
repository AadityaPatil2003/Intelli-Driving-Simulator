using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IDS.Vehicle
{
    /// <summary>
    /// One working mirror. A rear-facing camera renders into a small RenderTexture
    /// which is shown on the mirror's glass quad.
    ///
    /// The mirrors are not decoration — the whole mirror-awareness measurement
    /// rests on them. A driver asked to check a mirror that shows nothing learns
    /// to stop checking it, and MirrorCheckDetector then records a behaviour
    /// change the simulator caused rather than one the driver brought with them.
    ///
    /// DESIGN NOTES
    ///
    /// - MIRRORED AT DISPLAY TIME, by flipping the glass material's texture U
    ///   coordinate. The obvious alternative — negating the projection matrix's X
    ///   scale — also reverses triangle winding, so every front face is culled and
    ///   the mirror renders inside-out. Flipping the UV has no such side effect.
    /// - THROTTLED BY TOGGLING camera.enabled, not by calling Camera.Render().
    ///   Under a scriptable render pipeline, a manual Render() call on a disabled
    ///   camera is the unsupported path; letting URP render an enabled camera for
    ///   one frame in N is the supported one and costs the same.
    /// - Small render targets. Three mirrors at 320×200 cost far less than one at
    ///   1024, and a mirror is a glance, not a viewport.
    /// - Opaque clear colour, or the mirror shows whatever is behind it.
    /// - The UI layer is culled, so a HUD panel in the cabin is not reflected.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MirrorView : MonoBehaviour
    {
        public enum Facing { RearLeft, RearRight, RearCentre }

        [SerializeField] private Facing facing = Facing.RearCentre;

        [Header("Render target")]
        [SerializeField] private int textureWidth = 320;
        [SerializeField] private int textureHeight = 200;

        [Tooltip("The mirror glass this view is shown on.")]
        [SerializeField] private Renderer glassRenderer;

        [Tooltip("The whole mirror head — housing and glass together. Aiming " +
                 "rotates THIS, not the glass on its own.")]
        [SerializeField] private Transform aimPivot;

        [Header("Performance")]
        [Tooltip("Mirror refresh rate. 30 is indistinguishable from 72 in a " +
                 "glance and costs less than half as much.")]
        [Range(10, 72)]
        [SerializeField] private int refreshHz = 30;

        [Tooltip("Stop refreshing when the driver is not looking anywhere near " +
                 "this mirror. A mirror behind the driver's head is pure cost.")]
        [SerializeField] private bool cullWhenNotLookedAt = true;

        [SerializeField] private float lookAwayAngle = 110f;

        private Camera _camera;
        private RenderTexture _target;
        private Transform _head;
        private float _nextRender;

        public Facing MirrorFacing => facing;

        /// <summary>The rotating head, for anything that must sit flat on the
        /// glass — the priority highlight, for one.</summary>
        public Transform AimPivot => aimPivot;

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            _target = new RenderTexture(textureWidth, textureHeight, 16,
                                        RenderTextureFormat.Default)
            {
                name = $"MirrorRT_{facing}",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            _target.Create();

            _camera.targetTexture = _target;
            _camera.enabled = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.55f, 0.62f, 0.70f);
            _camera.nearClipPlane = 0.08f;
            _camera.farClipPlane = 180f;
            _camera.fieldOfView = facing == Facing.RearCentre ? 42f : 52f;
            // Cull the UI (a HUD panel must not appear in a mirror) AND the car
            // itself. A mirror bolted to the cabin looking rearward sees the
            // rear-seat panel 2 m away and nothing else; the driver reported the
            // centre mirror as "blocked by the same black panel as the front".
            int mask = ~0;
            int ui = LayerMask.NameToLayer("UI");
            int cabin = LayerMask.NameToLayer("CabinInterior");

            if (ui >= 0) mask &= ~(1 << ui);
            if (cabin >= 0) mask &= ~(1 << cabin);

            _camera.cullingMask = mask;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.depth = -10;              // render before the main camera
            _camera.stereoTargetEye = StereoTargetEyeMask.None;

            // URP needs this, and adds it implicitly at render time. Adding it
            // explicitly means post-processing and stacking are off by intent
            // rather than by default.
            var data = GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.allowXRRendering = false;    // a mirror is one flat image, not stereo

            ApplyGlassMaterial();
        }

        /// <summary>
        /// Gives the glass its own material instance pointing at this mirror's
        /// render texture, with U flipped so the image reads as a mirror.
        /// Instancing matters: three mirrors sharing one material all show
        /// whichever texture was assigned last.
        /// </summary>
        private void ApplyGlassMaterial()
        {
            if (glassRenderer == null || glassRenderer.sharedMaterial == null) return;

            var material = new Material(glassRenderer.sharedMaterial)
            {
                name = $"MirrorGlass_{facing}"
            };

            material.mainTexture = _target;
            material.mainTextureScale = new Vector2(-1f, 1f);
            material.mainTextureOffset = new Vector2(1f, 0f);

            // URP's Lit/Unlit shaders expose _BaseMap; mainTexture maps to _MainTex
            // on some shaders and not others, so set both where present.
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", _target);
                material.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));
                material.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));
            }

            glassRenderer.material = material;
        }

        private void Start()
        {
            Camera main = Camera.main;
            if (main != null) _head = main.transform;
        }

        private void LateUpdate()
        {
            bool due = Time.time >= _nextRender;

            if (due && cullWhenNotLookedAt && _head != null)
            {
                Vector3 toMirror = transform.position - _head.position;
                if (toMirror.sqrMagnitude > 0.0001f &&
                    Vector3.Angle(_head.forward, toMirror) > lookAwayAngle)
                    due = false;
            }

            if (due) _nextRender = Time.time + 1f / Mathf.Max(1, refreshHz);

            // One enabled frame per refresh interval. URP picks the camera up
            // after LateUpdate, which is also after the car has been interpolated
            // into place, so the mirror never shows the previous viewpoint.
            if (_camera.enabled != due) _camera.enabled = due;
        }

        private void OnDestroy()
        {
            if (_camera != null) _camera.targetTexture = null;
            if (_target == null) return;
            _target.Release();
            Destroy(_target);
        }

        public void Configure(Facing which, Renderer glass, Transform pivot = null)
        {
            facing = which;
            glassRenderer = glass;
            aimPivot = pivot;
        }

        /// <summary>
        /// Angles the glass so the reflection is visible from the given eye
        /// position.
        ///
        /// This has to be re-applied whenever the driver changes sides, and that
        /// is why it is a method rather than a build-time constant. The cabin is
        /// authored left-hand drive; for Australia and India the seat mirrors to
        /// the other side, about 0.76 m across. A glass angled once at build time
        /// for the left seat is then pointing roughly 27° away from where the
        /// driver's eyes actually are — close enough to look fine in a screenshot
        /// and useless to glance at, which is the worst of both.
        ///
        /// WHAT ROTATES MATTERS. This used to rotate the glass alone, inside a
        /// housing that stayed put. The glass is a 0.23 × 0.16 box and the
        /// housing is 0.26 × 0.19 × 0.05, so swinging the glass 27° inside a
        /// static shell drove one half of it into the housing and pushed the
        /// other half out through the front: on the headset the centre mirror
        /// read as a tilted sliver and the door mirrors, which turn further,
        /// disappeared almost entirely. Rotating the whole head keeps housing
        /// and glass coplanar, so the driver always sees the full face of it.
        /// </summary>
        public void AimGlassAt(Transform eyes)
        {
            if (eyes == null) return;

            Transform head = aimPivot != null
                ? aimPivot
                : glassRenderer != null ? glassRenderer.transform : null;

            if (head == null) return;

            Vector3 toEyes = eyes.position - head.position;
            if (toEyes.sqrMagnitude < 0.0001f) return;

            // The render texture is shown on the glass box's +Z face, and the
            // glass sits at the head's origin facing +Z, so pointing the head's
            // +Z at the driver points the reflection at the driver. The housing
            // is parented behind the glass at local -Z and so stays behind it.
            head.rotation = Quaternion.LookRotation(toEyes.normalized, Vector3.up);
        }
    }
}
