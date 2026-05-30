using System.Collections;
using UnityEngine;

namespace ChemicalPuzzle
{
    /// <summary>
    /// Управляет жидкостью в колбе.
    ///
    /// Dual-plane подход:
    ///   _LocalFillY  — порог в локальном (mesh) пространстве.
    ///                  Clip по локальному Y сохраняет РЕАЛЬНЫЙ 3D-объём при любом наклоне,
    ///                  т.к. для произвольной формы меша локальный Y-срез — это всегда
    ///                  та же доля геометрии, независимо от поворота объекта в мире.
    ///   _FillWorldY  — мировой Y для foam-полосы.
    ///                  Даёт визуально горизонтальную линию поверхности.
    ///                  Рассчитывается через TransformPoint центральной точки заполнения.
    /// </summary>
    [ExecuteAlways]
    public class LiquidWobble : MonoBehaviour
    {
        [Header("Fill")]
        [Tooltip("Доля заполнения: 0 = пусто, 1 = полная.")]
        [Range(0f, 1f)]
        public float fillFraction = 0f;

        [Tooltip("Скорость плавного следования foam-линии за целевым мировым Y.")]
        [SerializeField] private float correctionSpeed = 15f;

        [Header("Wobble")]
        [Tooltip("Максимальная амплитуда покачивания.")]
        [SerializeField] private float maxWobble   = 0.03f;
        [SerializeField] private float wobbleSpeed = 4f;
        [SerializeField] private float recovery    = 1.5f;

        private float      _fillWorldY;
        private bool       _initialized;
        private Renderer   _renderer;
        private MeshFilter _meshFilter;
        private Vector3    _lastPos;
        private Quaternion _lastRot;
        private float      _wobbleAddX, _wobbleAddZ, _wobbleX, _wobbleZ;
        private float      _time;

        [Header("Color")]
        [Tooltip("Основной цвет жидкости (_LiquidColor).")]
        [SerializeField] private Color _liquidColor = Color.white;
        [Tooltip("Цвет поверхности/пены (_SurfaceColor).")]
        [SerializeField] private Color _surfaceColor = Color.white;
        [Tooltip("Цвет свечения жидкости (_EmissionColor).")]
        [SerializeField] private Color _emissionColor = Color.black;
        [Tooltip("Интенсивность свечения (_EmissionPower).")]
        [SerializeField] private float _emissionPower = 0f;

        private Coroutine _fillCoroutine;

        // Локальные границы меша — кэшируются один раз, не зависят от трансформа
        private float _localMeshMin;
        private float _localMeshHeight;

        private static readonly int FillAmountId    = Shader.PropertyToID("_FillAmount");
        private static readonly int LocalMeshMinId  = Shader.PropertyToID("_LocalMeshMin");
        private static readonly int LocalMeshMaxId  = Shader.PropertyToID("_LocalMeshMax");
        private static readonly int PivotWSId       = Shader.PropertyToID("_PivotWS");
        private static readonly int WobbleXId       = Shader.PropertyToID("_WobbleX");
        private static readonly int WobbleZId       = Shader.PropertyToID("_WobbleZ");
        private static readonly int LiquidColorId   = Shader.PropertyToID("_LiquidColor");
        private static readonly int SurfaceColorId  = Shader.PropertyToID("_SurfaceColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionPowerId = Shader.PropertyToID("_EmissionPower");

        private MaterialPropertyBlock _propBlock;

        private void OnEnable()
        {
            _renderer   = GetComponent<Renderer>();
            _meshFilter = GetComponent<MeshFilter>();
            _propBlock  = new MaterialPropertyBlock();
            _lastPos    = transform.position;
            _lastRot    = transform.rotation;
            _initialized = false;

            CacheLocalMeshBounds();
        }

        private void CacheLocalMeshBounds()
        {
            if (_meshFilter != null && _meshFilter.sharedMesh != null)
            {
                Bounds b         = _meshFilter.sharedMesh.bounds;
                _localMeshMin    = b.min.y;
                _localMeshHeight = b.size.y;
            }
            else
            {
                _localMeshMin    = -0.5f;
                _localMeshHeight = 1.0f;
            }
        }

        private void Update()
        {
            if (_renderer == null) return;

            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
            if (dt <= 0) dt = 0.016f;

            if (Application.isPlaying)
            {
                _time += dt;
                
                _wobbleAddX = Mathf.Lerp(_wobbleAddX, 0f, dt * recovery);
                _wobbleAddZ = Mathf.Lerp(_wobbleAddZ, 0f, dt * recovery);

                float pulse = 2f * Mathf.PI * wobbleSpeed;
                _wobbleX = _wobbleAddX * Mathf.Sin(pulse * _time);
                _wobbleZ = _wobbleAddZ * Mathf.Sin(pulse * _time);

                Vector3 velocity = (transform.position - _lastPos) / dt;
                Quaternion deltaRot = transform.rotation * Quaternion.Inverse(_lastRot);
                deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180) angle -= 360;
                Vector3 angularVelocity = axis * (angle * Mathf.Deg2Rad / dt);

                _wobbleAddX += Mathf.Clamp((velocity.x + angularVelocity.z * 0.2f) * maxWobble, -maxWobble, maxWobble) * dt * 5f;
                _wobbleAddZ += Mathf.Clamp((velocity.z + angularVelocity.x * 0.2f) * maxWobble, -maxWobble, maxWobble) * dt * 5f;

                _lastPos = transform.position;
                _lastRot = transform.rotation;
            }

            // Sync with shader properties using PropertyBlock
            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(FillAmountId,    fillFraction);
            _propBlock.SetColor(LiquidColorId,   _liquidColor);
            _propBlock.SetColor(SurfaceColorId,  _surfaceColor);
            _propBlock.SetColor(EmissionColorId, _emissionColor);
            _propBlock.SetFloat(EmissionPowerId, _emissionPower);
            _propBlock.SetFloat(LocalMeshMinId,  _localMeshMin);
            _propBlock.SetFloat(LocalMeshMaxId,  _localMeshMin + _localMeshHeight);
            _propBlock.SetVector(PivotWSId,      transform.position);
            _propBlock.SetFloat(WobbleXId,       _wobbleX);
            _propBlock.SetFloat(WobbleZId,       _wobbleZ);
            _renderer.SetPropertyBlock(_propBlock);

        #if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.SceneView.RepaintAll();
        #endif
        }

        private void OnDisable()
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(WobbleXId, 0f);
            _propBlock.SetFloat(WobbleZId, 0f);
            _renderer.SetPropertyBlock(_propBlock);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Overrides all liquid colors at runtime (e.g. from MixerController).</summary>
        public void SetLiquidColor(Color liquid, Color surface, Color emission, float emissionPower)
        {
            _liquidColor   = liquid;
            _surfaceColor  = surface;
            _emissionColor = emission;
            _emissionPower = emissionPower;
        }

        /// <summary>Shorthand: sets liquid and surface to the same color, no emission.</summary>
        public void SetLiquidColor(Color color)
        {
            _liquidColor  = color;
            _surfaceColor = color;
        }

        /// <summary>Returns the current liquid color set on this component.</summary>
        public Color LiquidColor => _liquidColor;

        /// <summary>Smoothly animates fillFraction to <paramref name="target"/> over <paramref name="duration"/> seconds.</summary>
        public void AnimateFillTo(float target, float duration)
        {
            if (_fillCoroutine != null) StopCoroutine(_fillCoroutine);
            _fillCoroutine = StartCoroutine(FillRoutine(target, duration));
        }

        private IEnumerator FillRoutine(float target, float duration)
        {
            float start   = fillFraction;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed      += Time.deltaTime;
                fillFraction  = Mathf.Lerp(start, target, elapsed / duration);
                yield return null;
            }
            fillFraction   = target;
            _fillCoroutine = null;
        }
    }
}
