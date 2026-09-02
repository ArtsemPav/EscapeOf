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
        private const string DefaultShaderName = "Custom/LiquidFlaskLit";

        [Header("Shader")]
        [Tooltip("The LiquidFlask shader asset. Assign to prevent shader stripping in builds. " +
                 "If null, falls back to Shader.Find.")]
        [SerializeField] private Shader _shader;

        [Header("Fill")]
        [Tooltip("Доля заполнения: 0 = пусто, 1 = полная.")]
        [Range(0f, 1f)]
        public float fillFraction = 0f;

        [Tooltip("If true, the script pushes all visual properties (color, opacity, blur, etc.) to the material every frame. " +
                 "If false, only runtime properties (fill, wobble, pivot, mesh bounds) are pushed — visual settings are controlled directly in the material.")]
        [SerializeField] private bool _overrideVisualProps = true;

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
        private Material   _sharedMaterialAsset;
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

        [Tooltip("Множитель авто-эмиссии: когда > 0, shorthand SetLiquidColor(Color) " +
                 "автоматически выводит HDR emission из цвета жидкости " +
                 "(_EmissionColor = color * multiplier). Используется для подсветки " +
                 "уровня заполнения сквозь стекло.")]
        [SerializeField] private float _autoEmissionMultiplier = 0f;

        [Header("Turbidity & Noise")]
        [Tooltip("Мутность жидкости: 0 = прозрачная, 1 = полностью мутная. " +
                 "Влияет на размытие предметов под водой и поглощение цвета.")]
        [Range(0f, 1f)]
        [SerializeField] private float _turbidity = 0.6f;
        [Tooltip("Масштаб шума для анимации поверхности жидкости.")]
        [Range(0.1f, 10f)]
        [SerializeField] private float _noiseScale = 5f;
        [Tooltip("Скорость анимации шума поверхности.")]
        [Range(0f, 5f)]
        [SerializeField] private float _noiseSpeed = 0.5f;

        [Header("Transparency & Refraction")]
        [Tooltip("Непрозрачность жидкости: 1 = полностью непрозрачная, 0 = невидима.")]
        [Range(0f, 1f)]
        [SerializeField] private float _opacity = 0.82f;
        [Tooltip("Сила преломления фона сквозь жидкость. Требует Opaque Texture в URP Asset (уже включено).")]
        [Range(0f, 0.2f)]
        [SerializeField] private float _refractionStrength = 0.03f;
        [Tooltip("Хроматическая аберрация при преломлении — лёгкое радужное расщепление краёв.")]
        [Range(0f, 0.02f)]
        [SerializeField] private float _chromaticAberration = 0.004f;

        [Header("Distortion & Lens")]
        [Tooltip("Сила искажения фона мульти-октавным шумом (центр жидкости).")]
        [Range(0f, 0.3f)]
        [SerializeField] private float _distortionStrength = 0.08f;
        [Tooltip("Скорость анимации искажения.")]
        [Range(0f, 5f)]
        [SerializeField] private float _distortionSpeed = 1f;
        [Tooltip("Сила эффекта линзы — увеличение фона сквозь толщу жидкости.")]
        [Range(0f, 1f)]
        [SerializeField] private float _lensStrength = 0.15f;
        [Tooltip("Степень усиления линзы с глубиной (1 = линейно, 2 = квадратично).")]
        [Range(0f, 3f)]
        [SerializeField] private float _lensPower = 1f;
        [Tooltip("Затемнение к низу колбы: 0 = равномерно, 1 = дно полностью чёрное.")]
        [Range(0f, 1f)]
        [SerializeField] private float _depthDarken = 0.5f;

        [Header("Underwater Blur")]
        [Tooltip("Сила размытия предметов под жидкостью. Больше = предмет менее различим.")]
        [Range(0f, 0.05f)]
        [SerializeField] private float _blurStrength = 0.03f;

        [Header("Cap (surface above water)")]
        [Tooltip("Непрозрачность «крышки» — поверхности выше уровня воды. " +
                 "Больше = лучше спрятан предмет, меньше = сильнее видно размытые очертания.")]
        [Range(0f, 1f)]
        [SerializeField] private float _capOpacity = 0.85f;
        [Tooltip("Усиление искажения для крышки (зарезервировано, сейчас не используется).")]
        [Range(1f, 5f)]
        [SerializeField] private float _capDistortion = 1f;

        [Header("Lighting")]
        [Tooltip("Минимальная яркость в темноте. 0.15 = жидкость слабо видна в полной темноте (колбы). " +
                 "0 = жидкость полностью чёрная без света (раковины, ванны).")]
        [Range(0f, 1f)]
        [SerializeField] private float _minLightFloor = 0.15f;

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
        private static readonly int TurbidityId           = Shader.PropertyToID("_Turbidity");
        private static readonly int NoiseScaleId          = Shader.PropertyToID("_NoiseScale");
        private static readonly int NoiseSpeedId          = Shader.PropertyToID("_NoiseSpeed");
        private static readonly int OpacityId             = Shader.PropertyToID("_Opacity");
        private static readonly int RefractionStrengthId  = Shader.PropertyToID("_RefractionStrength");
        private static readonly int ChromaticAberrationId = Shader.PropertyToID("_ChromaticAberration");
        private static readonly int DistortionStrengthId  = Shader.PropertyToID("_DistortionStrength");
        private static readonly int DistortionSpeedId     = Shader.PropertyToID("_DistortionSpeed");
        private static readonly int LensStrengthId        = Shader.PropertyToID("_LensStrength");
        private static readonly int LensPowerId           = Shader.PropertyToID("_LensPower");
        private static readonly int DepthDarkenId         = Shader.PropertyToID("_DepthDarken");
        private static readonly int MinLightFloorId       = Shader.PropertyToID("_MinLightFloor");
        private static readonly int BlurStrengthId        = Shader.PropertyToID("_BlurStrength");
        private static readonly int CapOpacityId          = Shader.PropertyToID("_CapOpacity");
        private static readonly int CapDistortionId       = Shader.PropertyToID("_CapDistortion");

        private void OnEnable()
        {
            _renderer   = GetComponent<Renderer>();
            _meshFilter = GetComponent<MeshFilter>();
            _lastPos    = transform.position;
            _lastRot    = transform.rotation;

            // Rendering layer mask is controlled directly on the MeshRenderer in the Inspector.
            // Do NOT override it here — the renderer's mask must match the room's light layers.

            CacheLocalMeshBounds();

            // Если уже владеем инстансом (повторный OnEnable), сначала очистим.
            if (_ownsMaterialInstance && _materialInstance != null)
            {
                if (!Application.isPlaying && _renderer != null && _sharedMaterialAsset != null)
                    _renderer.sharedMaterial = _sharedMaterialAsset;
                DestroyImmediate(_materialInstance);
                _materialInstance = null;
                _ownsMaterialInstance = false;
            }

            EnsureMaterialInstance();
            ApplyMaterialProperties();
        }

        /// <summary>
        /// Создаёт уникальный экземпляр материала для этого рендерера.
        /// В Play Mode используем renderer.material (автоматически создаёт instance).
        /// В Edit Mode тоже создаём временный instance, чтобы не загрязнять ассет.
        /// Если sharedMaterial null (часто у FBX-prefab children), создаёт материал
        /// напрямую с LiquidFlask шейдером.
        /// </summary>
        private void EnsureMaterialInstance()
        {
            if (_renderer == null) return;

            Material shared = _renderer.sharedMaterial;

            // If sharedMaterial is null, create one from the LiquidFlask shader.
            // This happens when FBX-prefab children lose their material override.
            if (shared == null)
            {
                Shader shader = _shader != null ? _shader : Shader.Find(DefaultShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[LiquidWobble] Shader '{DefaultShaderName}' not found. " +
                                   "Assign it to the _shader field or add to Always Included Shaders.", this);
                    return;
                }

                _materialInstance = new Material(shader);
                _materialInstance.hideFlags = HideFlags.HideAndDontSave;
                _renderer.sharedMaterial = _materialInstance;
                _ownsMaterialInstance = true;
                return;
            }

            if (Application.isPlaying)
            {
                // renderer.material автоматически создаёт instance, которым владеем мы
                _materialInstance = _renderer.material;
                _ownsMaterialInstance = true;
            }
            else
            {
                // В Edit Mode создаём временный instance от sharedMaterial,
                // чтобы запись свойств в Update() не меняла сам ассет.
                _sharedMaterialAsset = shared;
                _materialInstance = new Material(shared);
                _materialInstance.hideFlags = HideFlags.HideAndDontSave;
                _renderer.sharedMaterial = _materialInstance;
                _ownsMaterialInstance = true;
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

            ApplyMaterialProperties();

        #if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.SceneView.RepaintAll();
        #endif
        }

        /// <summary>
        /// Pushes properties to the material instance.
        /// Runtime properties (fill, wobble, pivot, mesh bounds) are always pushed.
        /// Visual properties (color, opacity, blur, etc.) are only pushed when
        /// _overrideVisualProps is true — otherwise they stay in the material.
        /// </summary>
        private void ApplyMaterialProperties()
        {
            if (_materialInstance == null) return;

            // Runtime properties — always pushed.
            _materialInstance.SetFloat(FillAmountId,           fillFraction);
            _materialInstance.SetFloat(LocalMeshMinId,         _localMeshMin);
            _materialInstance.SetFloat(LocalMeshMaxId,         _localMeshMin + _localMeshHeight);
            _materialInstance.SetVector(PivotWSId,             transform.position);
            _materialInstance.SetFloat(WobbleXId,              _wobbleX);
            _materialInstance.SetFloat(WobbleZId,              _wobbleZ);

            // Visual properties — only pushed when overriding is enabled.
            if (!_overrideVisualProps) return;

            _materialInstance.SetColor(LiquidColorId,          _liquidColor);
            _materialInstance.SetColor(SurfaceColorId,         _surfaceColor);
            _materialInstance.SetColor(EmissionColorId,        _emissionColor);
            _materialInstance.SetFloat(EmissionPowerId,        _emissionPower);
            _materialInstance.SetFloat(TurbidityId,            _turbidity);
            _materialInstance.SetFloat(NoiseScaleId,           _noiseScale);
            _materialInstance.SetFloat(NoiseSpeedId,           _noiseSpeed);
            _materialInstance.SetFloat(OpacityId,              _opacity);
            _materialInstance.SetFloat(RefractionStrengthId,   _refractionStrength);
            _materialInstance.SetFloat(ChromaticAberrationId,  _chromaticAberration);
            _materialInstance.SetFloat(DistortionStrengthId,   _distortionStrength);
            _materialInstance.SetFloat(DistortionSpeedId,      _distortionSpeed);
            _materialInstance.SetFloat(LensStrengthId,         _lensStrength);
            _materialInstance.SetFloat(LensPowerId,            _lensPower);
            _materialInstance.SetFloat(DepthDarkenId,          _depthDarken);
            _materialInstance.SetFloat(MinLightFloorId,        _minLightFloor);
            _materialInstance.SetFloat(BlurStrengthId,         _blurStrength);
            _materialInstance.SetFloat(CapOpacityId,           _capOpacity);
            _materialInstance.SetFloat(CapDistortionId,        _capDistortion);
        }

        private void OnDisable()
        {
            if (_materialInstance == null) return;
            _materialInstance.SetFloat(WobbleXId, 0f);
            _materialInstance.SetFloat(WobbleZId, 0f);

            // В Edit Mode возвращаем оригинальный sharedMaterial, чтобы
            // временный instance не сохранялся в сцену/префаб.
            if (!Application.isPlaying && _renderer != null && _sharedMaterialAsset != null)
            {
                _renderer.sharedMaterial = _sharedMaterialAsset;
            }
        }

        private void OnDestroy()
        {
            // В Edit Mode сначала возвращаем оригинальный материал.
            if (!Application.isPlaying && _renderer != null && _sharedMaterialAsset != null)
            {
                _renderer.sharedMaterial = _sharedMaterialAsset;
            }

            // Уничтожаем только тот инстанс, который создали сами, чтобы никогда не
            // пытаться удалить шаренный ассет (например, при рекомпиляции в Play Mode).
            if (_ownsMaterialInstance && _materialInstance != null)
            {
                DestroyImmediate(_materialInstance);
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

        /// <summary>
        /// Shorthand: sets liquid and surface to the same color.
        /// When _autoEmissionMultiplier > 0, also derives HDR emission from the color
        /// so the fill level is clearly visible through glass.
        /// </summary>
        public void SetLiquidColor(Color color)
        {
            _liquidColor  = color;
            _surfaceColor = color;

            if (_autoEmissionMultiplier > 0f)
            {
                _emissionColor = color * _autoEmissionMultiplier;
            }
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

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only: resets stale baked properties on the shared material asset.
        /// Removes leftover values from the old EditMode sharedMaterial-writing bug
        /// (_PivotWS, _LocalMeshMin, _LocalMeshMax, _WobbleX, _WobbleZ) and old-shader
        /// properties (_FillScale, _FillWorldY, _LocalFillY, _LiquidCenter, _Mutnost,
        /// _SurfaceWidth, _EdgeSmoothness, _WoobleZ).
        /// Right-click the component header in the Inspector and select "Reset Shared Material".
        /// </summary>
        [ContextMenu("Reset Shared Material")]
        private void ResetSharedMaterial()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_renderer == null) return;

            Material mat = _renderer.sharedMaterial;
            if (mat == null)
            {
                Debug.LogWarning("[LiquidWobble] No shared material to reset.", this);
                return;
            }

            UnityEditor.Undo.RecordObject(mat, "Reset LiquidFlask material");

            // Reset runtime-controlled properties to shader defaults
            mat.SetVector(PivotWSId, Vector4.zero);
            mat.SetFloat(WobbleXId, 0f);
            mat.SetFloat(WobbleZId, 0f);
            mat.SetFloat(FillAmountId, 0f);
            mat.SetFloat(LocalMeshMinId, -0.5f);
            mat.SetFloat(LocalMeshMaxId, 0.5f);

            UnityEditor.EditorUtility.SetDirty(mat);
            Debug.Log($"[LiquidWobble] Reset shared material '{mat.name}' on '{gameObject.name}'. " +
                      "Stale runtime properties cleared. Old-shader leftover properties " +
                      "(_FillScale, _Mutnost, etc.) are ignored by the current shader.", this);
        }
#endif
    }
}
