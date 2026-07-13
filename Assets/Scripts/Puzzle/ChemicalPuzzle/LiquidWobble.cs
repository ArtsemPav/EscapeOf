using System.Collections;
using UnityEngine;

namespace ChemicalPuzzle
{
    /// <summary>
    /// Управляет жидкостью в колбе.
    ///
    /// Использует material instance вместо MaterialPropertyBlock,
    /// т.к. SRP Batcher в URP игнорирует PropertyBlock для свойств
    /// внутри CBUFFER_START(UnityPerMaterial).
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

        private Renderer   _renderer;
        private MeshFilter _meshFilter;
        private Material   _materialInstance;
        private bool       _ownsMaterialInstance;
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

        [Header("Transparency & Refraction")]
        [Tooltip("Непрозрачность жидкости: 1 = полностью непрозрачная, 0 = невидима.")]
        [Range(0f, 1f)]
        [SerializeField] private float _opacity = 0.82f;
        [Tooltip("Сила преломления фона сквозь жидкость. Требует Opaque Texture в URP Asset (уже включено).")]
        [Range(0f, 0.08f)]
        [SerializeField] private float _refractionStrength = 0.03f;
        [Tooltip("Хроматическая аберрация при преломлении — лёгкое радужное расщепление краёв.")]
        [Range(0f, 0.02f)]
        [SerializeField] private float _chromaticAberration = 0.004f;
        [Tooltip("Затемнение к низу колбы: 0 = равномерно, 1 = дно полностью чёрное.")]
        [Range(0f, 1f)]
        [SerializeField] private float _depthDarken = 0.5f;

        private Coroutine _fillCoroutine;

        // Локальные границы меша — кэшируются один раз, не зависят от трансформа
        private float _localMeshMin;
        private float _localMeshHeight;

        private static readonly int FillAmountId          = Shader.PropertyToID("_FillAmount");
        private static readonly int LocalMeshMinId        = Shader.PropertyToID("_LocalMeshMin");
        private static readonly int LocalMeshMaxId        = Shader.PropertyToID("_LocalMeshMax");
        private static readonly int PivotWSId             = Shader.PropertyToID("_PivotWS");
        private static readonly int WobbleXId             = Shader.PropertyToID("_WobbleX");
        private static readonly int WobbleZId             = Shader.PropertyToID("_WobbleZ");
        private static readonly int LiquidColorId         = Shader.PropertyToID("_LiquidColor");
        private static readonly int SurfaceColorId        = Shader.PropertyToID("_SurfaceColor");
        private static readonly int EmissionColorId       = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionPowerId       = Shader.PropertyToID("_EmissionPower");
        private static readonly int OpacityId             = Shader.PropertyToID("_Opacity");
        private static readonly int RefractionStrengthId  = Shader.PropertyToID("_RefractionStrength");
        private static readonly int ChromaticAberrationId = Shader.PropertyToID("_ChromaticAberration");
        private static readonly int DepthDarkenId         = Shader.PropertyToID("_DepthDarken");

        private void OnEnable()
        {
            _renderer   = GetComponent<Renderer>();
            _meshFilter = GetComponent<MeshFilter>();
            _lastPos    = transform.position;
            _lastRot    = transform.rotation;

            // Сбрасываем renderingLayerMask к Default (1).
            // Меши бутылок могут иметь нестандартные Light Layers (например, 516),
            // но для жидкости с шейдером LiquidFlask нужен Default, чтобы источники
            // света комнаты корректно освещали жидкость через URP per-object light culling.
            if (_renderer != null)
                _renderer.renderingLayerMask = 1;

            CacheLocalMeshBounds();
            EnsureMaterialInstance();
        }

        /// <summary>
        /// Создаёт уникальный экземпляр материала для этого рендерера.
        /// В Edit Mode используем sharedMaterial напрямую (без инстанса),
        /// чтобы не загрязнять ассет.
        /// </summary>
        private void EnsureMaterialInstance()
        {
            if (_renderer == null) return;

            if (Application.isPlaying)
            {
                // renderer.material автоматически создаёт instance, которым владеем мы
                _materialInstance = _renderer.material;
                _ownsMaterialInstance = true;
            }
            else
            {
                // В Edit Mode работаем с shared (без инстансирования)
                _materialInstance = _renderer.sharedMaterial;
                _ownsMaterialInstance = false;
            }
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
            if (_renderer == null || _materialInstance == null) return;

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

            // Передаём свойства напрямую в material instance —
            // SRP Batcher корректно обрабатывает per-material свойства.
            _materialInstance.SetFloat(FillAmountId,           fillFraction);
            _materialInstance.SetColor(LiquidColorId,          _liquidColor);
            _materialInstance.SetColor(SurfaceColorId,         _surfaceColor);
            _materialInstance.SetColor(EmissionColorId,        _emissionColor);
            _materialInstance.SetFloat(EmissionPowerId,        _emissionPower);
            _materialInstance.SetFloat(LocalMeshMinId,         _localMeshMin);
            _materialInstance.SetFloat(LocalMeshMaxId,         _localMeshMin + _localMeshHeight);
            _materialInstance.SetVector(PivotWSId,             transform.position);
            _materialInstance.SetFloat(WobbleXId,              _wobbleX);
            _materialInstance.SetFloat(WobbleZId,              _wobbleZ);
            _materialInstance.SetFloat(OpacityId,              _opacity);
            _materialInstance.SetFloat(RefractionStrengthId,   _refractionStrength);
            _materialInstance.SetFloat(ChromaticAberrationId,  _chromaticAberration);
            _materialInstance.SetFloat(DepthDarkenId,          _depthDarken);

        #if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.SceneView.RepaintAll();
        #endif
        }

        private void OnDisable()
        {
            if (_materialInstance == null) return;
            _materialInstance.SetFloat(WobbleXId, 0f);
            _materialInstance.SetFloat(WobbleZId, 0f);
        }

        private void OnDestroy()
        {
            // Уничтожаем только тот инстанс, который создали сами, чтобы никогда не
            // пытаться удалить шаренный ассет (например, при рекомпиляции в Play Mode).
            if (_ownsMaterialInstance && _materialInstance != null)
            {
                Destroy(_materialInstance);
                _materialInstance = null;
                _ownsMaterialInstance = false;
            }
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

        /// <summary>Sets opacity and refraction at runtime (e.g. per liquid type).</summary>
        public void SetTransparency(float opacity, float refractionStrength, float chromaticAberration = 0.004f, float depthDarken = 0.35f)
        {
            _opacity             = Mathf.Clamp01(opacity);
            _refractionStrength  = Mathf.Clamp(refractionStrength, 0f, 0.08f);
            _chromaticAberration = Mathf.Clamp(chromaticAberration, 0f, 0.02f);
            _depthDarken         = Mathf.Clamp01(depthDarken);
        }

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
